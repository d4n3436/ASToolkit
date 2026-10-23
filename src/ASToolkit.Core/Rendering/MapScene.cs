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

    private readonly Placement[] _placed;
    private readonly int[] _order;
    private readonly int[] _ranks;
    private readonly Rectangle[] _bounds;
    private readonly int[] _animated;
    private readonly OmniLight[] _lights;
    private readonly Rectangle[] _lightBounds;
    private readonly PackedTint? _ambient;
    private readonly List<int>[] _cells;
    private readonly int _columns;

    internal MapScene(Placement[] placed, OmniLight[] lights, PackedTint? ambient, int undrawn)
    {
        _placed = placed;
        _ambient = ambient;
        Undrawn = undrawn;
        if (placed.Length > 0)
        {
            Left = (int)placed.Min(p => p.Left) - Margin;
            Top = (int)placed.Min(p => p.Top) - Margin;
            Width = (int)placed.Max(p => p.Left + p.Width) - Left + Margin;
            Height = (int)placed.Max(p => p.Top + p.Height) - Top + Margin;
        }

        _order = Enumerable.Range(0, placed.Length).OrderBy(index => placed[index].Pass).ToArray();
        _ranks = new int[placed.Length];
        for (int rank = 0; rank < _order.Length; rank++)
        {
            _ranks[_order[rank]] = rank;
        }

        _bounds = placed.Select(GetBounds).ToArray();
        _animated = Enumerable.Range(0, placed.Length).Where(index => placed[index].Animation is not null).ToArray();
        var shift = new Vector2(Left, Top);
        _lights = lights.Select(light => light with
        {
            Position = light.Position - new Vector3(Left, Top, 0), Shadows = light.Shadows.Select(card => card with { Origin = card.Origin - shift }).ToArray()
        }).ToArray();
        _lightBounds = _lights.Select(GetBounds).ToArray();
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
    public bool IsEmpty => _placed.Length == 0;

    /// <summary>
    /// Gets a value indicating whether any object or light of the scene changes over time.
    /// </summary>
    public bool IsAnimated => _animated.Length > 0 || _lights.Any(light => light.Colors.Length > 1);

    /// <summary>
    /// Gets the number of objects drawn in the scene.
    /// </summary>
    public int ObjectCount => _placed.Length;

    /// <summary>
    /// Gets the number of objects whose sprite changes over time.
    /// </summary>
    public int AnimatedCount => _animated.Length;

    /// <summary>
    /// Gets the number of sprites left out because their sprite frame could not be decoded.
    /// </summary>
    public int Undrawn { get; }

    /// <summary>
    /// Draws an area of the scene scaled down by a factor of at most 1 into opaque pixels at a game time in milliseconds, or with
    /// every object on its first frame when the time is null. The area is in scaled pixels from the top left corner of the scene,
    /// and its top left pixel goes to the start of the buffer.
    /// </summary>
    public void Render(Memory<Bgra32> pixels, int stride, Rectangle area, double scale, int? time) =>
        Render((row, colors) =>
        {
            var target = pixels.Span.Slice(row * stride, colors.Length);
            for (int column = 0; column < colors.Length; column++)
            {
                target[column] = new Bgra32(colors[column].R, colors[column].G, colors[column].B);
            }
        }, area, scale, time);

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
            var p = _placed[index];
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
            if (_bounds[index].IntersectsWith(area) && _placed[index].GetAnimationKey(from) != _placed[index].GetAnimationKey(to))
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

    internal void Render(RowWriter writer, Rectangle area, double scale, int? time)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 1);
        var source = Rectangle.FromLTRB((int)Math.Floor(area.Left / scale), (int)Math.Floor(area.Top / scale), (int)Math.Ceiling(area.Right / scale) + 1,
            (int)Math.Ceiling(area.Bottom / scale) + 1);
        int[] indices = Query(source);
        var placed = indices.Select(index => _placed[index]).ToArray();
        var states = placed.Select(p => p.GetState(time, Left, Top)).ToArray();
        int[] lit = _ambient.HasValue ? Enumerable.Range(0, _lights.Length).Where(index => _lightBounds[index].IntersectsWith(source)).ToArray() : [];
        var canvas = new Canvas(placed, states, lit.Select(index => _lights[index]).ToArray(), lit.Select(index => _lights[index].GetColor(time)).ToArray(),
            area, scale, Width, Height, _ambient, writer, Background);
        if (canvas.StripCount == 1)
        {
            canvas.DrawAll();
            return;
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaxStripWorkers) };
        Parallel.For(0, canvas.StripCount, options, canvas.CreateBuffers, canvas.DrawStrip, canvas.ReleaseBuffers);
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
            var frameBounds = new Rectangle((int)Math.Round(p.ScreenX - width / 2.0 - Left), (int)Math.Round(p.ScreenY - height / 2.0 - Top) - reach, width, height + 2 * reach);
            bounds = bounds.IsEmpty ? frameBounds : Rectangle.Union(bounds, frameBounds);
        }

        return bounds;
    }

    private static Rectangle GetBounds(OmniLight light)
    {
        float centerY = light.Position.Y - light.Position.Z + LightQuadDrop;
        return Rectangle.FromLTRB((int)Math.Floor(light.Position.X - light.RadiusX), (int)Math.Floor(centerY - light.RadiusY), (int)Math.Ceiling(light.Position.X + light.RadiusX) + 1,
            (int)Math.Ceiling(centerY + light.RadiusY) + 1);
    }
}
