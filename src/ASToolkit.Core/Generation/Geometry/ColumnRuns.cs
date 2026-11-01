namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Represents a set of pixels as sorted runs of rows in each screen column.
/// </summary>
internal sealed class ColumnRuns
{
    private static readonly List<(int Top, int Bottom)> NoRuns = [];

    private readonly List<(int Top, int Bottom)>[] _columns;
    private readonly int _left;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColumnRuns"/> class.
    /// </summary>
    public ColumnRuns(int left, int width)
    {
        _left = left;
        _columns = new List<(int Top, int Bottom)>[width];
        for (int index = 0; index < width; index++)
        {
            _columns[index] = [];
        }
    }

    /// <summary>
    /// Gets the number of pixels in the set.
    /// </summary>
    public int Count => _columns.Sum(runs => runs.Sum(run => run.Bottom - run.Top + 1));

    /// <summary>
    /// Adds a pixel below the last pixel of its column.
    /// </summary>
    public void Add(int x, int y)
    {
        var runs = _columns[x - _left];
        if (runs.Count > 0 && runs[^1].Bottom == y - 1)
        {
            runs[^1] = (runs[^1].Top, y);
        }
        else
        {
            runs.Add((y, y));
        }
    }

    /// <summary>
    /// Returns the number of pixels a shape covers, with its columns grown down by some rows.
    /// </summary>
    public int Measure(ColumnProfile profile, int left, int top, int grow = 0)
    {
        int total = 0;
        for (int index = 0; index < profile.Columns.Length; index++)
        {
            var span = profile.Columns[index];
            foreach ((int from, int to) in GetRuns(left + index, span))
            {
                total += Math.Max(0, Math.Min(to, top + span.Bottom + grow) - Math.Max(from, top + span.Top) + 1);
            }
        }

        return total;
    }

    /// <summary>
    /// Returns whether a shape grown by a margin on every side touches the set.
    /// </summary>
    public bool Touches(ColumnProfile profile, int left, int top, int margin)
    {
        for (int index = 0; index < profile.Columns.Length; index++)
        {
            var span = profile.Columns[index];
            for (int x = left + index - margin; x <= left + index + margin; x++)
            {
                foreach ((int from, int to) in GetRuns(x, span))
                {
                    if (from <= top + span.Bottom + margin && to >= top + span.Top - margin)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Removes the pixels a shape covers.
    /// </summary>
    public void Remove(ColumnProfile profile, int left, int top)
    {
        for (int index = 0; index < profile.Columns.Length; index++)
        {
            var span = profile.Columns[index];
            var runs = GetRuns(left + index, span);
            for (int runIndex = runs.Count - 1; runIndex >= 0; runIndex--)
            {
                (int from, int to) = runs[runIndex];
                if (to >= top + span.Top && from <= top + span.Bottom)
                {
                    runs.RemoveAt(runIndex);
                    if (to > top + span.Bottom)
                    {
                        runs.Insert(runIndex, (top + span.Bottom + 1, to));
                    }

                    if (from < top + span.Top)
                    {
                        runs.Insert(runIndex, (from, top + span.Top - 1));
                    }
                }
            }
        }
    }

    private List<(int Top, int Bottom)> GetRuns(int x, ColumnSpan span) =>
        span.Top <= span.Bottom && x >= _left && x < _left + _columns.Length ? _columns[x - _left] : NoRuns;
}
