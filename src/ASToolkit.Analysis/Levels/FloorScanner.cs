namespace ASToolkit.Analysis.Levels;

/// <summary>
/// Provides straight scans across the floor of a cell model.
/// </summary>
public static class FloorScanner
{
    /// <summary>
    /// Counts the floor cells in a straight line from a cell, one step of (du, dv) at a time, up to the first missing cell or
    /// blocked edge.
    /// </summary>
    /// <param name="isFloor">Whether a cell is floor.</param>
    /// <param name="isBlocked">Whether an edge blocks the line.</param>
    /// <param name="start">The cell to start from.</param>
    /// <param name="du">The step along u each time.</param>
    /// <param name="dv">The step along v each time.</param>
    /// <returns>The number of floor cells in the line.</returns>
    public static int CountOpenCells(Func<Cell, bool> isFloor, Func<EdgeKey, bool> isBlocked, Cell start, int du, int dv)
    {
        int count = 0;
        var c = start;
        while (true)
        {
            var next = new Cell(c.U + du, c.V + dv);
            var edge = du != 0 ? new EdgeKey('u', Math.Max(c.U, next.U), c.V) : new EdgeKey('v', c.U, Math.Max(c.V, next.V));
            if (!isFloor(next) || isBlocked(edge))
            {
                return count;
            }

            count++;
            c = next;
        }
    }
}
