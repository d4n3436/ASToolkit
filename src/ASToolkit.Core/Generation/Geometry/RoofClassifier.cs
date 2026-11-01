using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Provides the pixels a roof must cover and the pixels it must leave visible, from the shapes of a level.
/// </summary>
internal sealed class RoofClassifier
{
    private const int UnitHeight = 96;
    private const int UnitDrop = 8;
    private const int GroundProbe = 6;
    private const int BucketWidth = 64;
    private const byte KindMask = 7;
    private const byte ZoneForbidden = 8;
    private const byte ZoneTarget = 16;

    private readonly Rectangle _area;
    private readonly Lattice _lattice;
    private readonly IReadOnlySet<Cell> _shown;
    private readonly int _wallOverlap;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoofClassifier"/> class.
    /// </summary>
    public RoofClassifier(Rectangle area, Lattice lattice, IReadOnlySet<Cell> shown, int wallOverlap)
    {
        _area = area;
        _lattice = lattice;
        _shown = shown;
        _wallOverlap = wallOverlap;
    }

    /// <summary>
    /// Returns the visible floor pixels of the shapes.
    /// </summary>
    public ColumnRuns GetFloor(IReadOnlyList<RoofShape> shapes) => Classify(shapes, true).Target;

    /// <summary>
    /// Returns the pixels the roof must cover and the pixels it must not touch.
    /// </summary>
    public (ColumnRuns Target, ColumnRuns Forbidden) Classify(IReadOnlyList<RoofShape> shapes) => Classify(shapes, false);

    private (ColumnRuns Target, ColumnRuns Forbidden) Classify(IReadOnlyList<RoofShape> shapes, bool floorOnly)
    {
        var buckets = Enumerable.Range(0, _area.Width / BucketWidth + 1).Select(_ => new List<int>()).ToArray();
        for (int index = 0; index < shapes.Count; index++)
        {
            int first = Math.Max(0, (shapes[index].Left - _area.Left) / BucketWidth);
            int last = Math.Min(buckets.Length - 1, (shapes[index].Left + shapes[index].Profile.Columns.Length - _area.Left) / BucketWidth);
            for (int bucket = first; bucket <= last; bucket++)
            {
                buckets[bucket].Add(index);
            }
        }

        var (target, forbidden) = (new ColumnRuns(_area.Left, _area.Width), new ColumnRuns(_area.Left, _area.Width));
        byte[] column = new byte[_area.Height];
        for (int x = _area.Left; x < _area.Right; x++)
        {
            int low = _area.Height;
            int high = -1;
            foreach (int index in buckets[(x - _area.Left) / BucketWidth])
            {
                (var profile, int left, int top, var kind, int start, int end) = shapes[index];
                var span = x - left >= 0 && x - left < profile.Columns.Length ? profile.Columns[x - left] : new ColumnSpan(0, 0, -1);
                int from = Math.Max(0, top + span.Top - _area.Top);
                int to = Math.Min(_area.Height - 1, top + span.Bottom - _area.Top);
                if (from > to || x < start || x > end)
                {
                    continue;
                }

                // Past the ends of its edge, take the side of a wall face from the cell in front of its base
                if (kind is PixelKind.ShownFace or PixelKind.HiddenFace && (x < start + _wallOverlap || x > end - _wallOverlap))
                {
                    kind = _shown.Contains(_lattice.GetCell(x + 0.5f, top + span.Bottom + GroundProbe)) ? PixelKind.ShownFace : PixelKind.HiddenFace;
                }

                int cap = Math.Clamp(top + span.Cap - _area.Top - (kind == PixelKind.ShownFace ? 1 : 0), from, to + 1);
                column.AsSpan(from, cap - from).Fill((byte)PixelKind.Cap);
                column.AsSpan(cap, to + 1 - cap).Fill((byte)kind);
                (low, high) = (Math.Min(low, from), Math.Max(high, to));
            }

            if (high >= 0)
            {
                (low, high) = (Math.Max(0, low - UnitHeight), Math.Min(_area.Height - 1, high + UnitDrop));
                Scan(column.AsSpan(low, high + 1 - low), x, _area.Top + low, target, forbidden, floorOnly);
                column.AsSpan(low, high + 1 - low).Clear();
            }
        }

        return (target, forbidden);
    }

    // Forbid a unit's height above shown floor and a few rows below it, and hide a unit's height above closets, up to wall caps
    private static void Scan(Span<byte> column, int x, int top, ColumnRuns target, ColumnRuns forbidden, bool floorOnly)
    {
        int sinceShown = int.MaxValue;
        int sinceCloset = int.MaxValue;
        for (int y = column.Length - 1; y >= 0 && !floorOnly; y--)
        {
            var kind = (PixelKind)column[y];
            sinceShown = kind == PixelKind.ShownFloor ? 0 : kind == PixelKind.Cap || sinceShown == int.MaxValue ? int.MaxValue : sinceShown + 1;
            sinceCloset = kind == PixelKind.ClosetFloor ? 0 : kind == PixelKind.Cap || sinceCloset == int.MaxValue ? int.MaxValue : sinceCloset + 1;
            if (kind != PixelKind.Cap)
            {
                column[y] |= sinceShown <= UnitHeight ? ZoneForbidden : sinceCloset <= UnitHeight ? ZoneTarget : (byte)0;
            }
        }

        int since = int.MaxValue;
        for (int y = 0; y < column.Length; y++)
        {
            var kind = (PixelKind)(column[y] & KindMask);
            since = kind == PixelKind.ShownFloor ? 0 : since == int.MaxValue ? int.MaxValue : since + 1;
            bool isForbidden = kind is PixelKind.ShownFloor or PixelKind.ShownFace || (column[y] & ZoneForbidden) != 0 || (since <= UnitDrop && kind != PixelKind.Cap);
            if (floorOnly ? kind is PixelKind.Floor or PixelKind.ClosetFloor or PixelKind.ShownFloor : isForbidden)
            {
                (floorOnly ? target : forbidden).Add(x, top + y);
            }
            else if (!floorOnly && (kind is >= PixelKind.Bare and <= PixelKind.HiddenFace || (column[y] & ZoneTarget) != 0))
            {
                target.Add(x, top + y);
            }
        }
    }
}
