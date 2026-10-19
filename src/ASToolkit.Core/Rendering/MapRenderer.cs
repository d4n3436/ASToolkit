using System.Buffers;
using System.Collections.Concurrent;
using ASToolkit.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Renders maps the way the game's software renderer draws them, with its depth buffer and palette blending.
/// </summary>
/// <remarks>
/// Draws paletted sprites, which are nearly every sprite on a level; texture and bitmap sprites are counted as undrawn.
/// Animated objects show their first frame. Decoded frames are kept for the renderer's lifetime.
/// </remarks>
public sealed class MapRenderer
{
    private const int Margin = 4;
    private const int StripRows = 16;
    private const int MaxStripWorkers = 8;
    private const int PoolMegabytes = 32;
    private const int MissingSize = 8;
    private const int FlatDepth = 0x400;
    private const int MaxFlatDepth = 0x7fff;
    private const int RaisedDepth = 0x3fff;
    private const uint RaisedFlag = 0x8000;
    
    private static readonly MemoryAllocator Allocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = PoolMegabytes });
    private static readonly Configuration ImageConfiguration = CreateConfiguration(Environment.ProcessorCount);
    private static readonly Configuration StripConfiguration = CreateConfiguration(1);

    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly ConcurrentDictionary<string, VidFile?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<FrameKey, SpriteFrame?> _frames = new();
    private readonly ConcurrentDictionary<(string Path, (int R, int G, int B) Tint), (Rgb24[] Colors, byte[] Alphas)> _palettes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MapRenderer"/> class.
    /// </summary>
    public MapRenderer(string gameFolder, ObjectTable objects)
    {
        _gameFolder = gameFolder;
        _objects = objects;
    }

    /// <summary>
    /// Gets the color of the image where no sprite is drawn.
    /// </summary>
    public static Rgb24 Background { get; } = new(20, 20, 24);

    /// <summary>
    /// Renders map sprites in their file order, scaled down by a factor of at most 1, leaving out the objects the skip
    /// function picks.
    /// </summary>
    public MapImage Render(IReadOnlyList<MapSprite> sprites, double scale = 1, Func<GameObject, bool>? skip = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 1);
        var placed = new List<Placement>(sprites.Count);
        int undrawn = 0;
        foreach (var sprite in sprites)
        {
            var obj = _objects.Find(sprite.Id);
            if (obj is null || (skip?.Invoke(obj) ?? false))
            {
                continue;
            }

            var file = GetFile(obj.Path);
            var frame = file is null ? null : GetFrame(obj, file, obj.GetFrame(sprite.Dir, file.FrameCount));
            if (file is not null && frame is null)
            {
                undrawn++;
            }

            (int w, int h) = file is null ? (MissingSize, MissingSize) : (file.Width, file.Height);
            placed.Add(new Placement(sprite, obj, frame, sprite.X - w / 2.0, sprite.Y - sprite.Z - h / 2.0, w, h));
        }

        if (placed.Count == 0)
        {
            return new MapImage { Image = new Image<Rgb24>(ImageConfiguration, 640, 360, Background), OriginX = 0, OriginY = 0, Scale = scale, Undrawn = undrawn };
        }

        int x0 = (int)placed.Min(p => p.Left) - Margin;
        int y0 = (int)placed.Min(p => p.Top) - Margin;
        int width = (int)placed.Max(p => p.Left + p.Width) - x0 + Margin;
        int height = (int)placed.Max(p => p.Top + p.Height) - y0 + Margin;
        var image = new Image<Rgb24>(ImageConfiguration, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), Background);
        var canvas = new Canvas(placed, x0, y0, width, height, scale, image);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaxStripWorkers) };
        Parallel.For(0, canvas.StripCount, options, canvas.CreateBuffers, canvas.DrawStrip, canvas.ReleaseBuffers);
        return new MapImage { Image = image, OriginX = x0, OriginY = y0, Scale = scale, Undrawn = undrawn };
    }

    private static Configuration CreateConfiguration(int threads)
    {
        var configuration = Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = threads;
        configuration.MemoryAllocator = Allocator;
        return configuration;
    }

    private VidFile? GetFile(string relative) => _files.GetOrAdd(relative, r =>
    {
        string? path = GamePath.Resolve(_gameFolder, r);
        return path is not null && path.EndsWith(".vid", StringComparison.OrdinalIgnoreCase) ? VidFile.Load(path) : null;
    });

    private SpriteFrame? GetFrame(GameObject obj, VidFile file, int index) =>
        _frames.GetOrAdd(new FrameKey(obj.Path, obj.Tint, index), _ => DecodeFrame(obj, file, index));

    private SpriteFrame? DecodeFrame(GameObject obj, VidFile file, int index)
    {
        if (!file.HasPalette || file.Palette is not { Length: >= 1024 } palette || index >= file.FrameCount)
        {
            return null;
        }

        (var colors, byte[] alphas) = _palettes.GetOrAdd((obj.Path, obj.Tint), _ => ApplyTint(palette, obj.Tint));
        (var frame, ushort[] indices) = file.DecodeColors(index);
        int count = frame.Xs.Length;
        var result = new SpriteFrame
        {
            Xs = new short[count],
            Indices = new byte[count],
            Depths = file.HasDepth ? new ushort[count] : [],
            Colors = colors,
            Alphas = alphas,
            Top = frame.Top,
            Rows = frame.Rows,
            RowStarts = new int[frame.Rows + 1],
            IsBlended = file.IsBlended
        };
        int row = 0;
        for (int pixelIndex = 0; pixelIndex < count; pixelIndex++)
        {
            while (frame.Ys[pixelIndex] - frame.Top >= row)
            {
                result.RowStarts[row++] = pixelIndex;
            }

            result.Xs[pixelIndex] = (short)frame.Xs[pixelIndex];
            result.Indices[pixelIndex] = (byte)indices[pixelIndex];
            if (result.Depths.Length > 0)
            {
                result.Depths[pixelIndex] = (ushort)frame.Depths[pixelIndex];
            }
        }

        while (row <= frame.Rows)
        {
            result.RowStarts[row++] = count;
        }

        return result;
    }

    // Negative tints scale a channel down and positive ones add to it
    private static (Rgb24[] Colors, byte[] Alphas) ApplyTint(byte[] palette, (int R, int G, int B) tint)
    {
        var colors = new Rgb24[256];
        byte[] alphas = new byte[256];
        for (int index = 0; index < 256; index++)
        {
            colors[index] = new Rgb24(Shift(palette[4 * index + 2], tint.R), Shift(palette[4 * index + 1], tint.G), Shift(palette[4 * index], tint.B));
            alphas[index] = palette[4 * index + 3];
        }

        return (colors, alphas);
    }

    private static byte Shift(byte c, int t) => (byte)(t < 0 ? c * (256 + t) >> 8 : Math.Min(255, c + t));

    private readonly record struct FrameKey(string Path, (int R, int G, int B) Tint, int Frame);

    private sealed record StripBuffers(Image<Rgb24> Source, int[] Depth);

    private sealed record Placement(MapSprite Sprite, GameObject Object, SpriteFrame? Frame, double Left, double Top, int Width, int Height);

    // Draw strips of output rows in parallel, each at full size with its own depth buffer, then scale them into the image
    private sealed class Canvas
    {
        private readonly List<Placement> _placed;
        private readonly (int X, int Y)[] _origins;
        private readonly int _width;
        private readonly int _height;
        private readonly double _scale;
        private readonly Image<Rgb24> _image;
        private readonly int _stripRows;

        public Canvas(List<Placement> placed, int x0, int y0, int width, int height, double scale, Image<Rgb24> image)
        {
            _placed = placed;
            _origins = placed.Select(p => ((int)Math.Round(p.Left - x0), (int)Math.Round(p.Top - y0))).ToArray();
            _width = width;
            _height = height;
            _scale = scale;
            _image = image;
            _stripRows = Math.Max(1, (int)(StripRows * scale));
        }

        public int StripCount => (_image.Height + _stripRows - 1) / _stripRows;

        // One full-size strip image and depth buffer per worker, sized for the tallest strip
        public StripBuffers CreateBuffers()
        {
            int rows = (int)Math.Ceiling(_stripRows / _scale) + 1;
            return new StripBuffers(new Image<Rgb24>(StripConfiguration, _width, rows), ArrayPool<int>.Shared.Rent(_width * rows));
        }

        public void ReleaseBuffers(StripBuffers buffers)
        {
            buffers.Source.Dispose();
            ArrayPool<int>.Shared.Return(buffers.Depth);
        }

        public StripBuffers DrawStrip(int strip, ParallelLoopState state, StripBuffers buffers)
        {
            int out0 = strip * _stripRows;
            int out1 = Math.Min(_image.Height, out0 + _stripRows);
            int y0 = GetSourceStart(out0);
            int y1 = GetSourceStart(out1);
            if (y1 <= y0)
            {
                return buffers;
            }

            (var source, int[] depth) = buffers;
            for (int row = 0; row < y1 - y0; row++)
            {
                source.DangerousGetPixelRowMemory(row).Span.Fill(Background);
            }

            Array.Clear(depth, 0, _width * (y1 - y0));
            for (int placedObjectIndex = 0; placedObjectIndex < _placed.Count; placedObjectIndex++)
            {
                var p = _placed[placedObjectIndex];
                var frame = p.Frame;
                if (frame is not null && _origins[placedObjectIndex].Y + frame.Top < y1 && _origins[placedObjectIndex].Y + frame.Top + frame.Rows > y0)
                {
                    Draw(source, p, frame, _origins[placedObjectIndex], y0, y1, depth);
                }
            }

            if (_scale < 1)
            {
                using var scaled = source.Clone(StripConfiguration, c => c.Crop(new Rectangle(0, 0, _width, y1 - y0)).Resize(_image.Width, out1 - out0, KnownResamplers.Box));
                CopyRows(scaled, out0);
            }
            else
            {
                CopyRows(source, out0, out1 - out0);
            }

            return buffers;
        }

        private void CopyRows(Image<Rgb24> strip, int out0, int? rows = null)
        {
            for (int row = 0; row < (rows ?? strip.Height); row++)
            {
                strip.DangerousGetPixelRowMemory(row).Span.CopyTo(_image.DangerousGetPixelRowMemory(out0 + row).Span);
            }
        }

        // First source row of an output row, clamped to the source height
        private int GetSourceStart(int output) => Math.Min(_height, (int)Math.Ceiling(output / _scale - 1e-9));

        // Engine depth is 8 per unit of height, plus 0x400 for flat sprites and 8 more per row up for standing ones
        private void Draw(Image<Rgb24> target, Placement p, SpriteFrame frame, (int X, int Y) origin, int y0, int y1, int[] depth)
        {
            int baseDepth = (int)(p.Sprite.Z * 8);
            if ((p.Object.Flags & RaisedFlag) != 0 && baseDepth < RaisedDepth)
            {
                baseDepth += RaisedDepth;
            }

            int flat = Math.Min(baseDepth + FlatDepth, MaxFlatDepth);
            bool standing = p.Object.ExtentY < p.Object.ExtentZ;
            (short[] xs, byte[] indices, ushort[] depths, var colors, byte[] alphas) = (frame.Xs, frame.Indices, frame.Depths, frame.Colors, frame.Alphas);
            bool hasDepths = depths.Length > 0;
            int firstRow = Math.Clamp(y0 - origin.Y - frame.Top, 0, frame.Rows);
            int lastRow = Math.Clamp(y1 - origin.Y - frame.Top, 0, frame.Rows);
            for (int row = firstRow; row < lastRow; row++)
            {
                int y = origin.Y + frame.Top + row;
                var pixels = target.DangerousGetPixelRowMemory(y - y0).Span;
                int depthRow = (y - y0) * _width;
                int rowDepth = standing ? flat + (frame.Rows - row) * 8 : flat;
                for (int index = frame.RowStarts[row]; index < frame.RowStarts[row + 1]; index++)
                {
                    int x = origin.X + xs[index];
                    if ((uint)x >= (uint)_width)
                    {
                        continue;
                    }

                    int d = hasDepths ? baseDepth + depths[index] : rowDepth;
                    int k = depthRow + x;
                    if (frame.IsBlended)
                    {
                        if (depth[k] <= d)
                        {
                            var color = colors[indices[index]];
                            float a = alphas[indices[index]] / 255f;
                            ref var under = ref pixels[x];
                            under = new Rgb24(Blend(color.R, under.R, a), Blend(color.G, under.G, a), Blend(color.B, under.B, a));
                        }
                    }
                    else if (!hasDepths ? depth[k] <= d : depth[k] < d)
                    {
                        pixels[x] = colors[indices[index]];
                        depth[k] = d;
                    }
                }
            }
        }

        private static byte Blend(byte over, byte under, float alpha) => (byte)Math.Clamp(over * alpha + under * (1 - alpha), 0, 255);
    }
}
