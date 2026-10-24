using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;

namespace ASToolkit.Core.Routes;

// Reads the objects a placed sprite puts in the world and the height points of their frames, keeping each file and frame it reads
internal sealed class SpritePartReader
{
    private const int MaxLinkDepth = 4;
    private const int CellSize = WalkGrid.CellSize;
    private const int FrameCells = 256;
    private const int MaxFramePixels = FrameCells * CellSize;
    private const int DepthShift = 3;
    private const int DepthBias = 0x80;

    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly Dictionary<string, VidResource?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Path, int Frame), List<(float X, float Y, short Height)>> _points = [];

    public SpritePartReader(string gameFolder, ObjectTable objects)
    {
        _gameFolder = gameFolder;
        _objects = objects;
    }

    public IEnumerable<SpritePart> GetParts(MergedMapSprite sprite)
    {
        var id = sprite.Id;
        var position = sprite.Position;
        var scale = sprite.Scale;
        for (int depth = 0; depth < MaxLinkDepth && _objects.TryFind(id, out var obj); depth++)
        {
            string path = _objects.GetPath(id);
            var file = GetFile(path);
            int frame = file is null || file.Value.FrameCount == 0 ? 0 : obj.GetFrame(sprite.Facing, file.Value.FrameCount);
            yield return new SpritePart(id, obj, path, file is null || file.Value.FrameCount == 0 ? null : file, frame, position, scale);
            if (obj.Link == 0 || !_objects.TryFind(obj.Link, out var linked))
            {
                yield break;
            }

            position += MapRenderer.GetLinkOffset(obj, linked, file, frame, sprite.Facing);
            scale = Vector3.One;
            id = obj.Link;
        }
    }

    public List<(float X, float Y, short Height)> GetHeightPoints(SpritePart part)
    {
        if (!_points.TryGetValue((part.Path, part.Frame), out var points))
        {
            points = part.File is null ? [] : ReadHeightPoints(part.File.Value, part.Frame);
            _points[(part.Path, part.Frame)] = points;
        }

        return points;
    }

    private VidResource? GetFile(string path)
    {
        if (!_files.TryGetValue(path, out var file))
        {
            string? resolved = GamePath.Resolve(_gameFolder, path);
            file = resolved is not null && resolved.EndsWith(".vid", StringComparison.OrdinalIgnoreCase) ? new VidResource(File.ReadAllBytes(resolved)) : null;
            _files[path] = file;
        }

        return file;
    }

    private static List<(float X, float Y, short Height)> ReadHeightPoints(VidResource vid, int frameIndex)
    {
        bool usesDepth = vid is { HasDepth: true, HasPalette: true } && vid.Flags.HasFlag(VidFlags.Runs8Bit);
        var frame = usesDepth ? vid.GetRunFrame(frameIndex) : vid.GetFrame(frameIndex);
        float[] meshHeights = !usesDepth && frame.Mesh.TriangleCount > 0 ? MapRenderer.RasterizeHeights(frame.Mesh, vid.Width, vid.Height) : [];
        var cells = new Dictionary<int, short>();
        foreach (var run in frame)
        {
            for (int index = 0; index < run.Count; index++)
            {
                int x = run.X + index;
                int height = usesDepth ? (run.GetDepth(index) >> DepthShift) - DepthBias : meshHeights.Length > 0 ? GetMeshHeight(meshHeights, vid.Width, x, run.Y) : 0;
                int row = run.Y + height;
                if (x is < 0 or >= MaxFramePixels || row is < 0 or >= MaxFramePixels)
                {
                    continue;
                }

                int cell = row / CellSize * FrameCells + x / CellSize;
                if (!cells.TryGetValue(cell, out short current) || current < height)
                {
                    cells[cell] = (short)height;
                }
            }
        }

        return cells.Select(pair => ((float)(pair.Key % FrameCells * CellSize - vid.Width / 2), (float)(pair.Key / FrameCells * CellSize - vid.Height / 2), pair.Value)).ToList();
    }

    private static int GetMeshHeight(float[] heights, int width, int x, int y)
    {
        float height = (uint)(y * width + x) < (uint)heights.Length ? heights[y * width + x] : float.NaN;
        return float.IsNaN(height) ? 0 : (int)MathF.Round(height);
    }
}
