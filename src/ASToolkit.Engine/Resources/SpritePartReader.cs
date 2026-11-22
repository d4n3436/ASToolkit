using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Provides methods for reading the objects a placed sprite puts on the map and the height points of their frames.
/// </summary>
public sealed class SpritePartReader
{
    internal const string SpriteFileExtension = ".vid";
    private const int MaxLinkDepth = 4;
    private const int FrameCells = 256;
    private const int MaxFramePixels = FrameCells * MovementMap.CellSize;
    private const int DepthShift = 3;
    private const int DepthBias = 0x80;

    private static readonly ConcurrentDictionary<string, byte[]> FileBytes = new(StringComparer.Ordinal);

    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly Dictionary<string, VidResource?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Path, int Frame), Vector3[]> _points = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SpritePartReader"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder, which holds the sprite files.</param>
    /// <param name="objects">The object table of the game.</param>
    public SpritePartReader(string gameFolder, ObjectTable objects)
    {
        _gameFolder = gameFolder;
        _objects = objects;
    }

    internal IEnumerable<SpritePart> GetParts(MergedMapSprite sprite)
    {
        var id = sprite.Id;
        var position = sprite.Position;
        var scale = sprite.Scale;
        for (int depth = 0; depth < MaxLinkDepth && _objects.TryFind(id, out var obj); depth++)
        {
            string path = _objects.GetPath(id);
            var file = GetFile(path);
            var framed = file is { FrameCount: > 0 } ? file : null;
            int frame = framed is null ? 0 : obj.GetFrame(sprite.Direction, framed.Value.FrameCount);
            yield return new SpritePart(id, obj, path, framed, frame, position, scale);

            if (obj.Link == 0 || !_objects.TryFind(obj.Link, out var linked))
            {
                yield break;
            }

            position += obj.GetLinkOffset(linked, file, frame, sprite.Direction);
            scale = Vector3.One;
            id = obj.Link;
        }
    }

    internal Vector3[] GetHeightPoints(SpritePart part)
    {
        if (!_points.TryGetValue((part.Path, part.Frame), out var points))
        {
            points = part.File is null ? [] : ReadHeightPoints(part.File.Value, part.Frame);
            _points[(part.Path, part.Frame)] = points;
        }

        return points;
    }

    internal int CountFrames(string path)
    {
        var file = GetFile(path);

        if (file is not null)
            return file.Value.FrameCount;

        if (!path.EndsWith(BmpImage.Extension, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (GamePath.Resolve(_gameFolder, path) is null)
            return 1;

        string stem = path[..^BmpImage.Extension.Length];
        int digits = stem.Length - stem.AsSpan().TrimEnd("0123456789").Length;

        if (digits == 0)
            return 1;

        var prefix = stem.AsSpan()[..^digits];
        int frames = 1;
        while (GamePath.Resolve(_gameFolder, $"{prefix}{frames}{BmpImage.Extension}") is not null)
        {
            frames++;
        }

        return frames;
    }

    internal ReadOnlyMemory<byte> GetBytes(string path) => GetFile(path)?.Data ?? ReadOnlyMemory<byte>.Empty;

    internal VidResource? GetFile(string path)
    {
        if (!_files.TryGetValue(path, out var file))
        {
            string? resolved = GamePath.Resolve(_gameFolder, path);
            file = resolved is not null && resolved.EndsWith(SpriteFileExtension, StringComparison.OrdinalIgnoreCase) ? new VidResource(FileBytes.GetOrAdd(Path.GetFullPath(resolved), File.ReadAllBytes)) : null;
            _files[path] = file;
        }

        return file;
    }

    /// <summary>
    /// Reads the cells a frame raises.
    /// </summary>
    /// <param name="vid">The sprite file.</param>
    /// <param name="frameIndex">The frame index.</param>
    /// <returns>The offset of each raised cell from the center of the sprite, with its height.</returns>
    public static Vector3[] ReadHeightPoints(VidResource vid, int frameIndex)
    {
        bool usesDepth = vid is { HasZBuffer: true, HasPalette: true } && vid.Type.HasFlag(VidType.Runs8Bit);
        var frame = usesDepth ? vid.GetRunFrame(frameIndex) : vid.GetFrame(frameIndex);
        float[] meshHeights = !usesDepth && frame.Mesh.TriangleCount > 0 ? frame.Mesh.RasterizeHeights(vid.Size) : [];
        var cells = new Dictionary<int, short>();
        foreach (var run in frame)
        {
            for (int i = 0; i < run.Count; i++)
            {
                int x = run.Position.X + i;
                int height = usesDepth ? (run.GetDepth(i) >> DepthShift) - DepthBias : meshHeights.Length > 0 ? GetMeshHeight(meshHeights, vid.Size.Width, new Point(x, run.Position.Y)) : 0;
                int row = run.Position.Y + height;

                if (x is < 0 or >= MaxFramePixels || row is < 0 or >= MaxFramePixels)
                {
                    continue;
                }

                int cell = row / MovementMap.CellSize * FrameCells + x / MovementMap.CellSize;

                if (!cells.TryGetValue(cell, out short current) || current < height)
                {
                    cells[cell] = (short)height;
                }
            }
        }

        var half = vid.Size / 2;
        return cells.Select(pair =>
        {
            (int row, int column) = Math.DivRem(pair.Key, FrameCells);
            return new Vector3(column * MovementMap.CellSize - half.Width, row * MovementMap.CellSize - half.Height, pair.Value);
        }).ToArray();
    }

    private static int GetMeshHeight(float[] heights, int width, Point pixel)
    {
        int i = pixel.Y * width + pixel.X;
        float height = i >= 0 && i < heights.Length ? heights[i] : float.NaN;
        return float.IsNaN(height) ? 0 : (int)MathF.Round(height);
    }
}
