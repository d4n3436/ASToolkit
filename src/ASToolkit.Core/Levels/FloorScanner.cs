namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides straight scans across the floor of a cell model.
/// </summary>
public static class FloorScanner
{
    /// <summary>
    /// Counts the floor cells in a straight line from a cell, one step of (du, dv) at a time, up to the first missing cell or
    /// blocked edge.
    /// </summary>
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
