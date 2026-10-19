using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Provides the dressing of a generated level from a <see cref="DressingLibrary"/>.
/// </summary>
public static class Dresser
{
    /// <summary>
    /// Covers the cells with copied windows, largest first, and drops objects whose support was not copied.
    /// </summary>
    public static PlacedDecor[] Dress(Random rng, DressingLibrary library, IReadOnlySet<Cell> cells, IReadOnlyDictionary<EdgeKey, char> edges)
    {
        var covered = new HashSet<Cell>();
        var placed = new List<PlacedDecor>();
        foreach (var corner in cells.OrderBy(c => c.U).ThenBy(c => c.V))
        {
            if (covered.Contains(corner))
            {
                continue;
            }

            foreach (int windowSize in library.WindowSizes)
            {
                if (!DressingLibrary.IsWindowInside(cells, corner, windowSize) || HasCovered(covered, corner, windowSize))
                {
                    continue;
                }

                if (!library.TryCopy(rng, edges, corner, windowSize, out var copied))
                {
                    continue;
                }

                placed.AddRange(copied);
                for (int u = corner.U; u < corner.U + windowSize; u++)
                {
                    for (int v = corner.V; v < corner.V + windowSize; v++)
                    {
                        covered.Add(new Cell(u, v));
                    }
                }

                break;
            }
        }

        return KeepSupported(placed);
    }

    /// <summary>
    /// Returns the objects whose support is still present, repeated until nothing changes.
    /// </summary>
    public static PlacedDecor[] KeepSupported(IReadOnlyList<PlacedDecor> placed)
    {
        while (true)
        {
            var present = placed.Select(o => o.Source).ToHashSet();
            var kept = placed.Where(o => o.Support is null || present.Contains(o.Support.Value)).ToArray();
            if (kept.Length == placed.Count)
            {
                return kept;
            }

            placed = kept;
        }
    }

    /// <summary>
    /// Returns the cells a solid object's collision box touches, sampled at its corners, edge midpoints and center.
    /// </summary>
    public static HashSet<Cell> GetFootprint(Lattice lattice, ObjectTable objects, KnownObject id, double x, double y)
    {
        var cells = new HashSet<Cell>();
        if (!objects.TryFind(id, out var obj) || !obj.IsSolid)
        {
            return cells;
        }

        double hx = .33 * obj.ExtentX + 14;
        double hy = .33 * obj.ExtentY + 14;
        foreach (double dx in new[] { -hx, 0, hx })
        {
            foreach (double dy in new[] { -hy, 0, hy })
            {
                cells.Add(lattice.GetCell(x + dx, y + dy));
            }
        }

        return cells;
    }

    /// <summary>
    /// Drops solid objects that touch the cells to keep clear, then removes solid objects until every cell is reachable from
    /// the start. Returns the objects kept and how many solid objects cover each cell.
    /// </summary>
    public static (PlacedDecor[] Kept, Dictionary<Cell, int> Blocked) ClearAndConnect(
        IReadOnlyList<PlacedDecor> placed, IReadOnlySet<Cell> cells, IReadOnlyDictionary<EdgeKey, char> edges, IReadOnlySet<Cell> keepClear, Cell start, Lattice lattice, ObjectTable objects)
    {
        var footprints = new Dictionary<PlacedDecor, HashSet<Cell>>(ReferenceEqualityComparer.Instance);
        foreach (var placedObject in placed)
        {
            (double x, double y) = lattice.ToWorld(placedObject.U, placedObject.V);
            var fp = GetFootprint(lattice, objects, placedObject.Id, x, y);
            if (fp.Count > 0)
            {
                footprints[placedObject] = fp;
            }
        }

        placed = placed.Where(o => !footprints.TryGetValue(o, out var fp) || !fp.Overlaps(keepClear)).ToArray();
        var floor = new HashSet<Cell>(cells);
        while (true)
        {
            var blocked = new Dictionary<Cell, int>();
            foreach (var placedObject in placed)
            {
                if (footprints.TryGetValue(placedObject, out var fp))
                {
                    foreach (var cell in fp)
                    {
                        blocked[cell] = blocked.GetValueOrDefault(cell) + 1;
                    }
                }
            }

            var seen = Flood(floor, edges, start, c => blocked.ContainsKey(c));
            var target = floor.Where(c => !seen.Contains(c) && !blocked.ContainsKey(c)).OrderBy(c => c.U).ThenBy(c => c.V).Cast<Cell?>().FirstOrDefault();
            if (target is null)
            {
                return (KeepSupported(placed), blocked);
            }

            var path = FindPath(floor, edges, start, target.Value);
            if (path.Length == 0)
            {
                floor.Remove(target.Value);
                continue;
            }

            var onPath = path.ToHashSet();
            placed = placed.Where(o => !footprints.TryGetValue(o, out var fp) || !fp.Overlaps(onPath)).ToArray();
        }
    }

    private static bool HasCovered(HashSet<Cell> covered, Cell corner, int size)
    {
        for (int u = corner.U; u < corner.U + size; u++)
        {
            for (int v = corner.V; v < corner.V + size; v++)
            {
                if (covered.Contains(new Cell(u, v)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HashSet<Cell> Flood(IReadOnlySet<Cell> floor, IReadOnlyDictionary<EdgeKey, char> edges, Cell start, Func<Cell, bool> isBlocked)
    {
        var seen = new HashSet<Cell> { start };
        var queue = new Queue<Cell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var (n, k) in EdgeKey.GetNeighbors(c))
            {
                if (floor.Contains(n) && !seen.Contains(n) && !IsWall(edges, k) && !isBlocked(n))
                {
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }

    private static Cell[] FindPath(IReadOnlySet<Cell> floor, IReadOnlyDictionary<EdgeKey, char> edges, Cell from, Cell to)
    {
        var previous = new Dictionary<Cell, Cell> { [from] = from };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c == to)
            {
                break;
            }

            foreach (var (n, k) in EdgeKey.GetNeighbors(c))
            {
                if (floor.Contains(n) && !previous.ContainsKey(n) && !IsWall(edges, k))
                {
                    previous[n] = c;
                    queue.Enqueue(n);
                }
            }
        }

        if (!previous.ContainsKey(to))
        {
            return [];
        }

        var path = new List<Cell>();
        for (var c = to; c != from; c = previous[c])
        {
            path.Add(c);
        }

        path.Add(from);
        return path.ToArray();
    }

    private static bool IsWall(IReadOnlyDictionary<EdgeKey, char> edges, EdgeKey k) => edges.TryGetValue(k, out char kind) && RouteGrid.IsBlocking(kind);
}
