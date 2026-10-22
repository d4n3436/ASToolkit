using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Provides rows of solid and breakable objects along walls until a level reaches the clutter of the original it replaces.
/// </summary>
public static class ClutterPlanner
{
    private const double ClearanceMargin = 0.02;
    private const int MaxRowTries = 5000;
    private const int MaxFailedRows = 1500;
    private static readonly HashSet<KnownObject> NotFreestanding =
    [
        KnownObject.ZDummy, KnownObject.LatticeWall, KnownObject.LatticeFence, KnownObject.Windows, KnownObject.Boards,
        KnownObject.Books, KnownObject.Toilet, KnownObject.WoodenDoor, KnownObject.BrokenKeypad
    ];

    /// <summary>
    /// Returns the solid and breakable decor IDs of a map with how often it uses each.
    /// </summary>
    public static ((KnownObject Id, int Count)[] Solid, (KnownObject Id, int Count)[] Breakable) GetPalette(MapResourceBuilder original, ObjectTable objects)
    {
        var solid = new Dictionary<KnownObject, int>();
        var breakable = new Dictionary<KnownObject, int>();
        foreach (var sprite in original.Sprites)
        {
            if (!objects.TryFind(sprite.Id, out var obj) || !sprite.Id.IsDecor || NotFreestanding.Contains(sprite.Id) || sprite.Z > 2)
            {
                continue;
            }

            if (obj.IsSolid)
            {
                solid[sprite.Id] = solid.GetValueOrDefault(sprite.Id) + 1;
            }
            else if (obj.IsHittable)
            {
                breakable[sprite.Id] = breakable.GetValueOrDefault(sprite.Id) + 1;
            }
        }

        var solidArray = solid
            .OrderBy(p => p.Key)
            .Select(p => (p.Key, p.Value))
            .ToArray();
        
        var breakableArray = breakable
            .OrderBy(p => p.Key)
            .Select(p => (p.Key, p.Value))
            .ToArray();

        return (solidArray, breakableArray);
    }

    /// <summary>
    /// Adds rows of objects along the walls of the given cells, each as far from the wall as the originals keep that object.
    /// Solid rows go in until the blocked share of the routing grid reaches its target, then breakable ones up to their
    /// target count. A row that would cut any floor off is dropped.
    /// </summary>
    public static PlacedDecor[] AddClutter(Random rng, IReadOnlyList<PlacedDecor> decor, IReadOnlySet<Cell> floor, IReadOnlySet<Cell> candidates,
        IReadOnlyDictionary<EdgeKey, char> edges, IReadOnlySet<Cell> keepClear, Cell start, Lattice lattice, ObjectTable objects,
        IReadOnlyList<(KnownObject Id, int Count)> solidPalette, IReadOnlyList<(KnownObject Id, int Count)> breakablePalette, double blockedShare, int breakables, WallClearanceTable clearance, FacingTable facings)
    {
        var fine = lattice.Halve();
        var fineCells = new HashSet<Cell>();
        foreach (var cell in floor)
        {
            for (int uOffset = 0; uOffset < 2; uOffset++)
            {
                for (int vOffset = 0; vOffset < 2; vOffset++)
                {
                    fineCells.Add(new Cell(2 * cell.U + uOffset, 2 * cell.V + vOffset));
                }
            }
        }

        var boxes = SolidObjectMapper.GetCollisionBoxes(ToSprites(decor, lattice), objects);
        var blockedFine = SolidObjectMapper.FindBlockedCells(fine, fineCells, boxes);
        var blocked = new Dictionary<Cell, int>();
        foreach (var decorObject in decor)
        {
            (double x, double y) = lattice.ToWorld(decorObject.U, decorObject.V);
            foreach (var cell in Dresser.GetFootprint(lattice, objects, decorObject.Id, x, y))
            {
                blocked[cell] = blocked.GetValueOrDefault(cell) + 1;
            }
        }

        var wallCells = candidates
            .Where(c => !keepClear.Contains(c) && EdgeKey.GetNeighbors(c).HasEdge(edges, 'W'))
            .OrderBy(c => c.U)
            .ThenBy(c => c.V)
            .ToArray();
        
        var wallSet = wallCells.ToHashSet();
        var seen = new HashSet<Cell>();
        var queue = new Queue<Cell>();
        var result = new List<PlacedDecor>(decor);
        int index = 0;
        int failed = 0;
        for (int attempt = 0; attempt < MaxRowTries && failed < MaxFailedRows && solidPalette.Count > 0 && blockedFine.Count < blockedShare * fineCells.Count; attempt++)
        {
            var id = Pick(rng, solidPalette);
            var row = PlanRow(rng, wallCells, wallSet, edges, lattice, objects, id, clearance);
            var footprint = new HashSet<Cell>();
            foreach ((double u, double v) in row)
            {
                (double x, double y) = lattice.ToWorld(u, v);
                footprint.UnionWith(Dresser.GetFootprint(lattice, objects, id, x, y).Where(floor.Contains));
            }

            if (row.Length == 0 || footprint.Overlaps(keepClear) || footprint.Any(blocked.ContainsKey)
                || !IsConnected(floor, edges, start, c => blocked.ContainsKey(c) || footprint.Contains(c), seen, queue))
            {
                failed++;
                continue;
            }

            failed = 0;

            foreach (var cell in footprint)
            {
                blocked[cell] = blocked.GetValueOrDefault(cell) + 1;
            }

            var rowSprites = new List<MergedMapSprite>();
            foreach ((double u, double v) in row)
            {
                byte dir = facings.PickFacing(rng, id, edges, u, v);
                result.Add(new PlacedDecor(id, u, v, 0, dir, new byte[8], (-2, index++), null));
                (double x, double y) = lattice.ToWorld(u, v);
                rowSprites.Add(new MergedMapSprite(0, id, x, y, 0, dir, 0, []));
            }

            blockedFine.UnionWith(SolidObjectMapper.FindBlockedCells(fine, fineCells, SolidObjectMapper.GetCollisionBoxes(rowSprites, objects)));
        }

        int present = result.Count(o => objects.TryFind(o.Id, out var obj) && obj.IsHittable);
        for (int attempt = 0; attempt < 400 && breakablePalette.Count > 0 && present < breakables; attempt++)
        {
            var id = Pick(rng, breakablePalette);
            var row = PlanRow(rng, wallCells, wallSet, edges, lattice, objects, id, clearance);
            var cells = row.Select(p => new Cell((int)Math.Floor(p.U), (int)Math.Floor(p.V))).ToArray();
            
            if (row.Length == 0 || cells.Any(c => keepClear.Contains(c) || blocked.ContainsKey(c) || !floor.Contains(c)))
            {
                continue;
            }

            foreach ((double u, double v) in row.Take(breakables - present))
            {
                result.Add(new PlacedDecor(id, u, v, 0, facings.PickFacing(rng, id, edges, u, v), id == KnownObject.Crate ? new byte[12] : new byte[8], (-2, index++), null));
                present++;
            }
        }

        return result.ToArray();
    }

    private static IEnumerable<MergedMapSprite> ToSprites(IEnumerable<PlacedDecor> decor, Lattice lattice) => decor.Select(o =>
    {
        (double x, double y) = lattice.ToWorld(o.U, o.V);
        return new MergedMapSprite(0, o.Id, x, y, o.Z, o.Dir, 0, o.Payload);
    });

    private static KnownObject Pick(Random rng, IReadOnlyList<(KnownObject Id, int Count)> palette)
    {
        int roll = rng.Next(palette.Sum(p => p.Count));
        foreach ((var id, int count) in palette)
        {
            roll -= count;
            if (roll < 0)
            {
                return id;
            }
        }

        return palette[^1].Id;
    }

    private static (double U, double V)[] PlanRow(Random rng, Cell[] wallCells, IReadOnlySet<Cell> wallSet, IReadOnlyDictionary<EdgeKey, char> edges, Lattice lattice,
        ObjectTable objects, KnownObject id, WallClearanceTable clearance)
    {
        if (wallCells.Length == 0)
        {
            return [];
        }

        var first = wallCells[rng.Next(wallCells.Length)];
        var wall = default(EdgeKey);
        foreach (var (_, edge) in EdgeKey.GetNeighbors(first))
        {
            if (edges.TryGetValue(edge, out char kind) && kind == 'W')
            {
                wall = edge;
                break;
            }
        }

        (int du, int dv) = wall.Axis == 'v' ? (1, 0) : (0, 1);
        if (rng.Next(2) == 0)
        {
            (du, dv) = (-du, -dv);
        }

        int stride = objects.TryFind(id, out var obj) && Math.Max(obj.ExtentX, obj.ExtentY) > lattice.StepX ? 2 : 1;
        double low = clearance.TryGetClearance(id, out _, out double l) ? l : 0;
        double offset = Math.Max(0.5, low + ClearanceMargin);
        double across = wall.Axis == 'v'
            ? wall.B + (first.V >= wall.B ? offset : -offset)
            : wall.A + (first.U >= wall.A ? offset : -offset);
        var row = new List<(double U, double V)>();
        int length = rng.Next(2, 6);
        for (int index = 0; index < length; index++)
        {
            var c = new Cell(first.U + du * stride * index, first.V + dv * stride * index);
            (double U, double V) p = wall.Axis == 'v' ? (c.U + 0.5, across) : (across, c.V + 0.5);
            if (!wallSet.Contains(c) || WallClearanceTable.MeasureDistance(edges, p.U, p.V) < low)
            {
                break;
            }

            row.Add(p);
        }

        return row.ToArray();
    }

    private static bool IsConnected(IReadOnlySet<Cell> floor, IReadOnlyDictionary<EdgeKey, char> edges, Cell start, Func<Cell, bool> isBlocked, HashSet<Cell> seen,
        Queue<Cell> queue)
    {
        seen.Clear();
        seen.Add(start);
        queue.Clear();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var (n, k) in EdgeKey.GetNeighbors(c))
            {
                if (floor.Contains(n) && !seen.Contains(n) && !isBlocked(n) && !(edges.TryGetValue(k, out char kind) && RouteGrid.IsBlocking(kind)))
                {
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return floor.All(c => seen.Contains(c) || isBlocked(c));
    }
}
