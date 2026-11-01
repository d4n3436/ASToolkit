using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Provides the dressing of a generated level from a <see cref="DressingLibrary"/>.
/// </summary>
public static class Dresser
{
    private const float PlayerRadius = 14;

    /// <summary>
    /// Covers the cells with copied windows, largest first and each source cell once, and drops objects whose support was not copied.
    /// With cells to keep clear, windows whose solid pieces stay off them come first. Cells already dressed are skipped.
    /// </summary>
    public static PlacedDecor[] Dress(Random rng, DressingLibrary library, IReadOnlySet<Cell> cells, IReadOnlyDictionary<EdgeKey, EdgeKind> edges,
        IReadOnlySet<Cell>? keepClear = null, IReadOnlySet<Cell>? dressed = null)
    {
        var covered = new HashSet<Cell>(dressed ?? new HashSet<Cell>());
        var used = new HashSet<(int, Cell)>();
        var placed = new List<PlacedDecor>();
        foreach (var corner in cells.OrderBy(c => c.U).ThenBy(c => c.V))
        {
            if (covered.Contains(corner))
            {
                continue;
            }

            foreach (int windowSize in library.WindowSizes)
            {
                if (!DressingLibrary.IsWindowInside(cells, corner, windowSize) || HasCovered(covered, corner, windowSize)
                    || !library.TryCopy(rng, edges, corner, windowSize, used, out var copied, keepClear))
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
    /// Returns the cells a solid object's collision box, grown by a margin, touches, sampled at its corners, edge midpoints and center.
    /// </summary>
    public static HashSet<Cell> GetFootprint(Lattice lattice, ObjectTable objects, KnownObject id, float x, float y, float grow = PlayerRadius)
    {
        var cells = new HashSet<Cell>();
        if (!objects.TryFind(id, out var obj) || !obj.IsSolid)
        {
            return cells;
        }

        float hx = 0.33f * obj.Size.X + grow;
        float hy = 0.33f * obj.Size.Y + grow;
        foreach (float dx in new[] { -hx, 0, hx })
        {
            foreach (float dy in new[] { -hy, 0, hy })
            {
                cells.Add(lattice.GetCell(x + dx, y + dy));
            }
        }

        return cells;
    }

    /// <summary>
    /// Drops solid objects whose collision box, grown by a margin, touches the cells to keep clear, then removes solid objects until
    /// every cell is reachable from the start. Returns the objects kept and how many solid objects cover each cell.
    /// </summary>
    public static (PlacedDecor[] Kept, Dictionary<Cell, int> Blocked) ClearAndConnect(
        IReadOnlyList<PlacedDecor> placed, IReadOnlySet<Cell> cells, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, IReadOnlySet<Cell> keepClear, Cell start, Lattice lattice, ObjectTable objects,
        float grow = PlayerRadius)
    {
        var footprints = new Dictionary<PlacedDecor, HashSet<Cell>>(ReferenceEqualityComparer.Instance);
        foreach (var placedObject in placed)
        {
            (float x, float y) = lattice.ToWorld(placedObject.U, placedObject.V);
            var fp = GetFootprint(lattice, objects, placedObject.Id, x, y, grow);
            if (fp.Count > 0)
            {
                footprints[placedObject] = fp;
            }
        }

        placed = placed.Where(o => !footprints.TryGetValue(o, out var fp) || !fp.Any(keepClear.Contains)).ToArray();
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

            var seen = Flood(floor, edges, start, blocked.ContainsKey);
            var target = floor.Where(c => !seen.Contains(c) && !blocked.ContainsKey(c)).OrderBy(c => c.U).ThenBy(c => c.V).Cast<Cell?>().FirstOrDefault();
            if (target is null)
            {
                return (KeepSupported(placed), blocked);
            }

            var path = FindPath(floor, edges, start, target.Value, blocked);
            if (path.Length == 0)
            {
                floor.Remove(target.Value);
                continue;
            }

            var onPath = path.ToHashSet();
            placed = placed.Where(o => !footprints.TryGetValue(o, out var fp) || !fp.Any(onPath.Contains)).ToArray();
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

    private static HashSet<Cell> Flood(IReadOnlySet<Cell> floor, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, Cell start, Func<Cell, bool> isBlocked)
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

    // Find the way from one cell to another through the fewest blocked cells
    private static Cell[] FindPath(IReadOnlySet<Cell> floor, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, Cell from, Cell to, IReadOnlyDictionary<Cell, int> blocked)
    {
        var cost = new Dictionary<Cell, int> { [from] = 0 };
        var previous = new Dictionary<Cell, Cell> { [from] = from };
        var queue = new LinkedList<Cell>();
        queue.AddFirst(from);
        while (queue.Count > 0)
        {
            var c = queue.First!.Value;
            queue.RemoveFirst();
            if (c == to)
            {
                break;
            }

            foreach (var (n, k) in EdgeKey.GetNeighbors(c))
            {
                if (!floor.Contains(n) || IsWall(edges, k))
                {
                    continue;
                }

                int step = blocked.ContainsKey(n) ? 1 : 0;
                if (cost.TryGetValue(n, out int known) && known <= cost[c] + step)
                {
                    continue;
                }

                cost[n] = cost[c] + step;
                previous[n] = c;
                if (step == 0)
                {
                    queue.AddFirst(n);
                }
                else
                {
                    queue.AddLast(n);
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

    private static bool IsWall(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, EdgeKey k) => edges.TryGetValue(k, out EdgeKind kind) && kind.BlocksWalking;
}

