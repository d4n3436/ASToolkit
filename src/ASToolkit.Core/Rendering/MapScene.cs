using System.Collections.Frozen;
using ASToolkit.Core.Formats;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static ASToolkit.Core.Rendering.MapRenderer;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents the placed objects of a map ready to be drawn in parts at any point of their animations.
/// </summary>
public sealed class MapScene
{
    private const int CellSize = 256;
    private const int DepthWriteLayers = 8;

    private readonly int[] _order;
    private readonly int[] _ranks;
    private readonly long[] _keys;
    private readonly Rectangle[] _bounds;
    private readonly int[] _animated;
    private readonly OmniLight[] _lights;
    private readonly Rectangle[] _lightBounds;
    private readonly long[] _lightKeys;
    private readonly PaletteTint? _ambient;
    private readonly PaletteTint _paletteTint;
    private readonly List<int>[] _cells;
    private readonly int _columns;
    private readonly List<LiveEntry> _live = [];
    private readonly List<LiveLight> _liveLights = [];
    private int _liveUndrawn;

    internal Placement[] Placed { get; }

    internal SceneObjects LiveObjects { get; }

    internal int NextSequence { get; }

    internal PaletteTint? Ambient => _ambient;

    internal MapScene(Placement[] placed, OmniLight[] lights, PaletteTint? ambient, int undrawn, PaletteTint paletteTint = default)
    {
        Placed = placed;
        _ambient = ambient;
        _paletteTint = paletteTint;
        Undrawn = undrawn;
        NextSequence = placed.Length == 0 ? 0 : placed.Max(p => p.Sequence) + 1;
        LiveObjects = new SceneObjects([], [], [], true, paletteTint);
        if (placed.Length > 0)
        {
            Left = (int)placed.Min(p => p.Left) - Margin;
            Top = (int)placed.Min(p => p.Top) - Margin;
            Width = (int)placed.Max(p => p.Left + p.Width) - Left + Margin;
            Height = (int)placed.Max(p => p.Top + p.Height) - Top + Margin;
        }

        _order = Enumerable.Range(0, placed.Length).OrderBy(index => GetDrawKey(placed[index].Pass, placed[index].Sequence)).ToArray();
        _ranks = new int[placed.Length];
        _keys = new long[placed.Length];
        for (int rank = 0; rank < _order.Length; rank++)
        {
            _ranks[_order[rank]] = rank;
            _keys[_order[rank]] = GetDrawKey(placed[_order[rank]].Pass, placed[_order[rank]].Sequence);
        }

        _bounds = placed.Select(GetBounds).ToArray();
        _animated = Enumerable.Range(0, placed.Length).Where(index => placed[index].Animation is not null).ToArray();
        var shift = new Vector2(Left, Top);
        _lights = lights.Select(light => light with
        {
            Position = light.Position - new Vector3(Left, Top, 0), Shadows = light.Shadows.Select(card => card with { Origin = card.Origin - shift }).ToArray()
        }).ToArray();
        _lightBounds = _lights.Select(light => ambient.HasValue ? GetBounds(light) : HeightLight.GetQuad(light)).ToArray();
        _lightKeys = _lights.Select(light => GetDrawKey(light.Pass, light.Sequence)).ToArray();
        _columns = Math.Max(1, (Width + CellSize - 1) / CellSize);
        int rows = Math.Max(1, (Height + CellSize - 1) / CellSize);
        _cells = new List<int>[_columns * rows];
        for (int cell = 0; cell < _cells.Length; cell++)
        {
            _cells[cell] = [];
        }

        for (int index = 0; index < placed.Length; index++)
        {
            var bounds = Rectangle.Intersect(_bounds[index], new Rectangle(0, 0, _columns * CellSize, rows * CellSize));
            for (int row = bounds.Top / CellSize; row <= (bounds.Bottom - 1) / CellSize && !bounds.IsEmpty; row++)
            {
                for (int column = bounds.Left / CellSize; column <= (bounds.Right - 1) / CellSize; column++)
                {
                    _cells[row * _columns + column].Add(index);
                }
            }
        }
    }

    /// <summary>
    /// Gets the map x of the left edge of the scene.
    /// </summary>
    public int Left { get; }

    /// <summary>
    /// Gets the screen y (map y minus height) of the top edge of the scene.
    /// </summary>
    public int Top { get; }

    /// <summary>
    /// Gets the width of the scene in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the scene in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the scene has no objects to draw.
    /// </summary>
    public bool IsEmpty => Placed.Length == 0;

    /// <summary>
    /// Gets a value indicating whether any object or light of the scene changes over time.
    /// </summary>
    public bool IsAnimated => _animated.Length > 0 || _lights.Any(light => light.Colors.Length > 1);

    /// <summary>
    /// Gets the number of objects drawn in the scene.
    /// </summary>
    public int ObjectCount => Placed.Length;

    /// <summary>
    /// Gets the number of objects whose sprite changes over time.
    /// </summary>
    public int AnimatedCount => _animated.Length;

    /// <summary>
    /// Gets the number of sprites left out because their sprite frame could not be decoded.
    /// </summary>
    public int Undrawn => field + _liveUndrawn;

    /// <summary>
    /// Draws an area of the scene scaled down by a factor of at most 1 into opaque pixels at a game time in milliseconds, or with
    /// every object on its first frame when the time is null. The area is in scaled pixels from the top left corner of the scene,
    /// and its top left pixel goes to the start of the buffer.
    /// </summary>
    public void Render(Memory<Bgra32> pixels, int stride, Rectangle area, float scale, int? time) =>
        Render(pixels, stride, area, scale, time, FrozenSet<MergedMapSprite>.Empty, null, null);

    /// <summary>
    /// Draws a replay frame without the given placed sprites and with an Alien Shooter palette tint for the current time.
    /// </summary>
    public void Render(Memory<Bgra32> pixels, int stride, Rectangle area, float scale, int? time,
        IReadOnlySet<MergedMapSprite> hidden, uint? ambientNegative, uint? ambientPositive) =>
        Render((row, colors) =>
        {
            var target = pixels.Span.Slice(row * stride, colors.Length);
            for (int column = 0; column < colors.Length; column++)
            {
                target[column] = new Bgra32(colors[column].R, colors[column].G, colors[column].B);
            }
        }, area, scale, time, hidden, ambientNegative is null && ambientPositive is null ? null : new PaletteTint(ambientNegative ?? 0, ambientPositive ?? 0));

    /// <summary>
    /// Returns the object whose pixel is in front at a point of the scene, in pixels from its top left corner, or null when no
    /// sprite covers the point.
    /// </summary>
    public SceneObject? FindObject(int x, int y, int? time)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return null;
        }

        SceneObject? found = null;
        int frontDepth = int.MinValue;
        foreach (int index in Query(new Rectangle(x, y, 1, 1)))
        {
            var p = Placed[index];
            var state = p.GetState(time, Left, Top);
            var frame = state.Frame;
            int row = y - state.Y - (frame?.Top ?? 0);
            if (frame is null || p.Source is null || (uint)row >= (uint)frame.Rows)
            {
                continue;
            }

            for (int pixel = frame.RowStarts[row]; pixel < frame.RowStarts[row + 1]; pixel++)
            {
                if (state.X + frame.Xs[pixel] != x || (!frame.IsPaletted && frame.Colors[pixel].A == 0))
                {
                    continue;
                }

                int depth = p.GetPixelDepth(frame, state.BaseDepth, row, pixel);
                if (depth >= frontDepth)
                {
                    frontDepth = depth;
                    found = p.Source;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Adds the areas of the scene, in pixels from its top left corner, that touch a given area and look different at one game
    /// time than at another.
    /// </summary>
    public void GetChangedAreas(int from, int to, Rectangle area, ICollection<Rectangle> changes)
    {
        foreach (int index in _animated)
        {
            if (_bounds[index].IntersectsWith(area) && Placed[index].GetAnimationKey(from) != Placed[index].GetAnimationKey(to))
            {
                changes.Add(_bounds[index]);
            }
        }

        for (int index = 0; index < _lights.Length; index++)
        {
            var timing = _lights[index].Timing;
            if (_lights[index].Colors.Length > 1 && _lightBounds[index].IntersectsWith(area) && timing.GetIndex(from) != timing.GetIndex(to))
            {
                changes.Add(_lightBounds[index]);
            }
        }
    }

    internal void Render(RowWriter writer, Rectangle area, float scale, int? time,
        IReadOnlySet<MergedMapSprite> hidden, PaletteTint? paletteTint = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 1);
        var source = Rectangle.FromLTRB((int)MathF.Floor(area.Left / scale), (int)MathF.Floor(area.Top / scale), (int)MathF.Ceiling(area.Right / scale) + 1,
            (int)MathF.Ceiling(area.Bottom / scale) + 1);
        var drawn = new List<Placement>();
        var drawnKeys = new List<long>();
        int next = 0;
        foreach (int index in Query(source))
        {
            if (!IsVisible(index, hidden))
                continue;

            next = AddLive(next, _keys[index], source, drawn, drawnKeys);
            drawn.Add(Placed[index]);
            drawnKeys.Add(_keys[index]);
        }

        AddLive(next, long.MaxValue, source, drawn, drawnKeys);
        var placed = drawn.ToArray();
        long[] keys = drawnKeys.ToArray();
        var states = placed.Select(p => p.GetState(time, Left, Top)).ToArray();
        var tint = paletteTint ?? _paletteTint;
        if (tint != default)
        {
            var palettes = new Dictionary<PaletteSource, Rgba32[]>();
            for (int index = 0; index < states.Length; index++)
            {
                var palette = states[index].Frame?.OriginalPalette;
                if (palette is null || placed[index].Flags.HasFlag(ObjectFlags.Unlit))
                    continue;

                var sourcePalette = palette.Value;
                if (!palettes.TryGetValue(sourcePalette, out var colors))
                {
                    colors = states[index].Frame!.Colors.Select(color => new Rgba32(
                        (byte)tint.Apply(color.R, 16), (byte)tint.Apply(color.G, 8), (byte)tint.Apply(color.B, 0), color.A)).ToArray();
                    palettes.Add(sourcePalette, colors);
                }
                states[index] = states[index] with { Palette = colors };
            }
        }
        var lit = new List<LiveLight>();
        for (int index = 0; index < _lights.Length; index++)
        {
            if (_lightBounds[index].IntersectsWith(source))
            {
                lit.Add(new LiveLight(_lights[index], _lightKeys[index], _lightBounds[index]));
            }
        }

        foreach (var light in _liveLights)
        {
            if (light.Bounds.IntersectsWith(source))
            {
                lit.Add(light);
            }
        }

        var heightLights = _ambient.HasValue ? [] : lit.Select(light => CreateHeightLight(light, keys, source, time, hidden)).ToArray();
        var canvas = new Canvas(placed, states, _ambient.HasValue ? lit.Select(light => light.Light).ToArray() : [],
            lit.Select(light => light.Light.GetColor(time)).ToArray(), heightLights, area, scale, Width, Height, _ambient, writer, Background);
        if (canvas.StripCount == 1)
        {
            canvas.DrawAll();
            return;
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaxStripWorkers) };
        Parallel.For(0, canvas.StripCount, options, canvas.CreateBuffers, (strip, _, buffers) => canvas.DrawStrip(strip, buffers), canvas.ReleaseBuffers);
    }

    // Orders by pass, then from the last created object to the first
    private static long GetDrawKey(int pass, int sequence) => (long)pass << 32 | (uint)(int.MaxValue - sequence);

    private static int GetLowerBound(long[] keys, long key)
    {
        int found = Array.BinarySearch(keys, key);
        return found >= 0 ? found : ~found;
    }

    private bool IsVisible(int index, IReadOnlySet<MergedMapSprite> hidden) =>
        hidden.Count == 0 || Placed[index].Source is null || !hidden.Contains(Placed[index].Source!.Sprite);

    private HeightLight CreateHeightLight(LiveLight lit, long[] drawnKeys, Rectangle view, int? time, IReadOnlySet<MergedMapSprite> hidden)
    {
        var light = new HeightLight(lit.Light, lit.Light.GetColor(time), GetLowerBound(drawnKeys, lit.Key));
        var samples = light.Samples;
        Span<int> depth = new int[samples.Width * samples.Height];
        depth.Fill(HeightLight.ClearDepth);
        var reach = Rectangle.Union(samples, light.Quad);
        foreach (int index in Query(reach))
        {
            if (_keys[index] < lit.Key && IsVisible(index, hidden))
            {
                BlockAndWrite(Placed[index], time, depth, light);
            }
        }

        foreach (var entry in _live)
        {
            if (entry.Key < lit.Key && entry.Bounds.IntersectsWith(reach))
            {
                BlockAndWrite(entry.Placement, time, depth, light);
            }
        }

        light.Fill(depth, view);
        return light;
    }

    private void BlockAndWrite(Placement p, int? time, Span<int> depth, HeightLight light)
    {
        var state = p.GetState(time, Left, Top);
        WriteDepths(p, state, depth, light.Samples);
        BlockLight(light, p, state);
    }

    // Adds the live placements drawn before a key that reach into an area, and returns the first live entry left
    private int AddLive(int next, long key, Rectangle area, List<Placement> drawn, List<long> keys)
    {
        for (; next < _live.Count && _live[next].Key < key; next++)
        {
            if (_live[next].Bounds.IntersectsWith(area))
            {
                drawn.Add(_live[next].Placement);
                keys.Add(_live[next].Key);
            }
        }

        return next;
    }

    internal void UpdateLive(int undrawn)
    {
        _liveUndrawn = undrawn;
        _live.Clear();
        foreach (var p in LiveObjects.Placed)
        {
            _live.Add(new LiveEntry(p, GetDrawKey(p.Pass, p.Sequence), GetBounds(p)));
        }

        _live.Sort();
        _liveLights.Clear();
        foreach (var light in LiveObjects.Lights)
        {
            var shifted = light with { Position = light.Position - new Vector3(Left, Top, 0) };
            _liveLights.Add(new LiveLight(shifted, GetDrawKey(light.Pass, light.Sequence), _ambient.HasValue ? GetBounds(shifted) : HeightLight.GetQuad(shifted)));
        }
    }

    // Mirrors the hardware depth the light quad tests against, written by the surface quads of layers 0 to 7
    private static void BlockLight(HeightLight light, Placement p, PlacementState state)
    {
        var frame = state.Frame;
        if (frame is null || p.Pass >= DepthWriteLayers * 2)
        {
            return;
        }

        int depth = state.BaseDepth / DepthPerHeight;
        foreach (var quad in frame.Quads)
        {
            var screen = new Rectangle(state.X + quad.Left, state.Y + quad.Top, quad.Width, quad.Height);
            if (frame.HasFarQuads)
            {
                light.Block(screen, float.MaxValue, float.MaxValue);
            }
            else
            {
                int lift = p.IsStanding ? quad.Height : 0;
                light.Block(screen, depth + lift, depth - lift);
            }
        }
    }

    private static void WriteDepths(Placement p, PlacementState state, Span<int> depth, Rectangle region)
    {
        var frame = state.Frame;
        if (frame is null || frame.IsCombined || frame.IsBlended || !frame.WritesDepth)
        {
            return;
        }

        bool hasDepths = frame.Depths.Length > 0;
        int firstRow = Math.Clamp(region.Top - state.Y - frame.Top, 0, frame.Rows);
        int lastRow = Math.Clamp(region.Bottom - state.Y - frame.Top, 0, frame.Rows);
        for (int row = firstRow; row < lastRow; row++)
        {
            int y = state.Y + frame.Top + row - region.Top;
            for (int index = frame.RowStarts[row]; index < frame.RowStarts[row + 1]; index++)
            {
                int x = state.X + frame.Xs[index] - region.Left;
                if ((uint)x >= (uint)region.Width || !frame.IsPaletted && frame.Colors[index].A == 0)
                {
                    continue;
                }

                int d = p.GetPixelDepth(frame, state.BaseDepth, row, index);
                ref int target = ref depth[y * region.Width + x];
                if (!frame.TestsDepth || (hasDepths ? target < d : target <= d))
                {
                    target = d;
                }
            }
        }
    }

    // Indices of the placements that may reach into an area, in draw order
    private int[] Query(Rectangle area)
    {
        if (area is { Left: <= 0, Top: <= 0 } && area.Right >= Width && area.Bottom >= Height)
        {
            return _order;
        }

        int rows = _cells.Length / _columns;
        int firstColumn = Math.Max(0, area.Left / CellSize);
        int lastColumn = Math.Min(_columns - 1, (area.Right - 1) / CellSize);
        int firstRow = Math.Max(0, area.Top / CellSize);
        int lastRow = Math.Min(rows - 1, (area.Bottom - 1) / CellSize);
        var ranks = new HashSet<int>();
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                foreach (int index in _cells[row * _columns + column])
                {
                    if (_bounds[index].IntersectsWith(area))
                    {
                        ranks.Add(_ranks[index]);
                    }
                }
            }
        }

        int[] sorted = ranks.ToArray();
        Array.Sort(sorted);
        return sorted.Select(rank => _order[rank]).ToArray();
    }

    private Rectangle GetBounds(Placement p)
    {
        int reach = p.GetBobReach();
        var bounds = Rectangle.Empty;
        foreach (var frame in p.GetFrames())
        {
            (int width, int height) = frame is null ? (p.Width, p.Height) : (frame.Width, frame.Height);
            var frameBounds = new Rectangle((int)MathF.Round(p.ScreenX - width / 2.0f - Left), (int)MathF.Round(p.ScreenY - height / 2.0f - Top) - reach, width, height + 2 * reach);
            bounds = bounds.IsEmpty ? frameBounds : Rectangle.Union(bounds, frameBounds);
        }

        return bounds;
    }

    private static Rectangle GetBounds(OmniLight light)
    {
        float centerY = light.Position.Y - light.Position.Z + LightQuadDrop;
        return Rectangle.FromLTRB((int)MathF.Floor(light.Position.X - light.RadiusX), (int)MathF.Floor(centerY - light.RadiusY), (int)MathF.Ceiling(light.Position.X + light.RadiusX) + 1,
            (int)MathF.Ceiling(centerY + light.RadiusY) + 1);
    }

    private readonly record struct LiveEntry(Placement Placement, long Key, Rectangle Bounds) : IComparable<LiveEntry>
    {
        public int CompareTo(LiveEntry other) => Key.CompareTo(other.Key);
    }

    private readonly record struct LiveLight(OmniLight Light, long Key, Rectangle Bounds);
}
