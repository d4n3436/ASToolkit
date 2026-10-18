using System.Buffers.Binary;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rules.Checks;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Provides the placement of a level's gameplay content by its progress along the route.
/// </summary>
public static class ContentPlacer
{
    private const double EarliestSite = 0.6;
    private const double MinSiteStartProgress = 0.3;
    private const int BreakerFront = 3;
    private const int BreakerSide = 2;
    private const int BreakerOpenEnd = 3;
    private const double BreakerPropFloor = 5;
    private const double KeyWayMargin = 600;
    private const double ClutterHeight = 5;
    private const double StandingHeight = 20;
    private const int BreakerWallRun = 4;
    private const double WeaponSpacing = 600;
    private const double WeaponCrateShare = 0.35;
    private static readonly int[] SiteRankCap = [0, 0, 3, 6, 6, 8, 9, 10, 11, 14, 14];

    /// <summary>
    /// Chooses the far wall of the key room the breaker hangs on, with the original breaker room props that fit best.
    /// </summary>
    public static BreakerSpot? ChooseBreakerSpot(Random rng, LayoutPlan plan, FineLayout layout, IReadOnlyList<IReadOnlyList<PropPlacement>> scenes, ObjectTable objects,
        WallClearanceTable clearance, FacingTable facings)
    {
        var room = plan.KeyRoom;
        if (room is null)
        {
            return null;
        }

        var cells = layout.GetRegionCells(room.Id).ToHashSet();
        (EdgeKey Wall, int Front, int Side)[] backWalls = layout.Edges
            .Where(p => p is { Value: 'W', Key.Axis: 'u' } && cells.Contains(new Cell(p.Key.A - 1, p.Key.B)) && !cells.Contains(new Cell(p.Key.A, p.Key.B)))
            .Select(p => p.Key).OrderBy(k => k.A).ThenBy(k => k.B)
            .Select(k => (k, CountOpenCells(layout, new Cell(k.A - 1, k.B), -1, 0), CountOpenCells(layout, new Cell(k.A - 1, k.B), 0, 1)))
            .ToArray();
        backWalls = backWalls.Where(w => WallRun.Find(layout.Edges, w.Wall).IsEnclosed(BreakerOpenEnd)).ToArray();
        var longWalls = backWalls.Where(w => WallRun.Find(layout.Edges, w.Wall).Length >= BreakerWallRun).ToArray();
        if (longWalls.Length > 0)
        {
            backWalls = longWalls;
        }

        var walls = backWalls
            .Where(w => w is { Front: >= BreakerFront, Side: >= BreakerSide })
            .Select(w => w.Wall)
            .ToArray();
        
        if (walls.Length == 0)
        {
            walls = backWalls
                .Where(w => w is { Front: >= BreakerFront - 1, Side: >= BreakerSide - 1 })
                .Select(w => w.Wall)
                .ToArray();
        }

        if (walls.Length == 0)
        {
            return null;
        }

        var keep = new HashSet<Cell>();
        foreach (var edges in layout.LinkEdges)
        {
            foreach (var edge in edges)
            {
                foreach (var cell in new[] { edge.Cells.First, edge.Cells.Second })
                {
                    for (int index = -1; index <= 1; index++)
                    {
                        for (int innerIndex = -1; innerIndex <= 1; innerIndex++)
                        {
                            keep.Add(new Cell(cell.U + index, cell.V + innerIndex));
                        }
                    }
                }
            }
        }

        (int Transformers, int Count, double Roll, EdgeKey Wall, List<PropPlacement> Fit)? best = null;
        foreach (var wall in walls)
        {
            ((double ax, double ay), _) = StructureBuilder.GetLabPosition(layout.Lattice, wall);
            (double bx, double by) = (ax - 8, ay + 3);
            var front = new HashSet<Cell>(keep) { new(wall.A - 1, wall.B) };
            foreach (var scene in scenes)
            {
                var fit = new List<PropPlacement>();
                foreach (var placement in scene)
                {
                    double x = bx + placement.Dx;
                    double y = by + placement.Dy;
                    var footprint = Dresser.GetFootprint(layout.Lattice, objects, placement.Id, x, y);
                    bool solid = footprint.Count > 0;
                    if (!solid)
                    {
                        footprint.Add(layout.Lattice.GetCell(x, y));
                    }

                    if (footprint.All(cells.Contains) && !(solid && footprint.Overlaps(front)) && IsWallFit(layout, clearance, facings, placement, x, y))
                    {
                        fit.Add(placement);
                    }
                }

                var score = (fit.Count(p => p.Id == KnownObject.Transformer), fit.Count, rng.NextDouble(), wall, fit);
                if (best is null || (score.Item1, score.Item2, score.Item3).CompareTo((best.Value.Transformers, best.Value.Count, best.Value.Roll)) > 0)
                {
                    best = score;
                }
            }
        }

        ((double wx, double wy), _) = StructureBuilder.GetLabPosition(layout.Lattice, best!.Value.Wall);
        return new BreakerSpot(best.Value.Wall, wx - 8, wy + 3, best.Value.Fit);
    }

    /// <summary>
    /// Puts a broken keypad next to jammed doors, as often as the originals do.
    /// </summary>
    public static void PlaceBrokenKeypads(Random rng, MapBuilder map, KeypadTable keypads)
    {
        foreach (var door in map.Sprites.Where(s => s.Id is KnownObject.DoubleDoorFrame or KnownObject.SmallDoorFrame).ToArray())
        {
            if (rng.NextDouble() >= keypads.GetShare(door.Id))
            {
                continue;
            }

            var keypad = keypads.Pick(rng, door.Id, door.Dir, broken: true);
            if (keypad is not null)
            {
                map.Add(KnownObject.BrokenKeypad, door.X + keypad.Dx, door.Y + keypad.Dy, keypad.Z, keypad.Dir);
            }
        }
    }

    /// <summary>
    /// Places the gate keys, bosses, mounted cannon, dynamite, monsters, ambushes, stashes, pickups and the checkpoint.
    /// </summary>
    public static void Place(PlacementContext ctx, LevelBudget budget, bool hasCheckpoint, bool blackoutIsCheckpoint, LevelScript script, BreakerSpot? breaker,
        IReadOnlyList<SiteTemplate> siteTemplates, IReadOnlyDictionary<EdgeKey, char> visibleEdges, IReadOnlyDictionary<EdgeKey, int> spawnWalls, KeypadTable keypads, FacingTable facings)
    {
        PlaceGate(ctx, budget, script, breaker, keypads);
        PlaceBosses(ctx, budget, facings);
        var cannonHall = PlaceMountedCannons(ctx, budget);
        var sites = PlaceSites(ctx, budget, siteTemplates, visibleEdges);
        if (ctx.Plan.KeyRoom is null)
        {
            PlaceCharges(ctx, sites, ctx.Distance, _ => true);
        }
        else
        {
            // Keep charges off the way to the key
            var key = GetKeyCell(ctx, ctx.Plan.KeyRoom);
            var fromKey = ctx.MeasureDistances(key);
            int detour = (int)(KeyWayMargin / ctx.Step);
            PlaceCharges(ctx, sites, fromKey, c => ctx.Distance[c] + fromKey.GetValueOrDefault(c) > ctx.Distance[key] + detour);
        }
        PlacePlacedGroups(ctx, budget);
        PlaceAmbushes(ctx, budget, spawnWalls, sites, cannonHall);
        PlaceStashes(ctx, budget);
        PlaceItems(ctx, budget);
        if (hasCheckpoint && (script.Blackout is null || !blackoutIsCheckpoint))
        {
            PlaceCheckpoint(ctx, script);
        }
    }

    private static bool IsWallFit(FineLayout layout, WallClearanceTable clearance, FacingTable facings, PropPlacement p, double x, double y)
    {
        if (p.Z >= BreakerPropFloor)
        {
            return true;
        }

        (double u, double v) = layout.Lattice.ToLattice(x, y);
        return facings.Fits(p.Id, p.Dir, layout.Edges, u, v)
               && (!clearance.TryGetClearance(p.Id, out double minimum, out _) || WallClearanceTable.MeasureDistance(layout.Edges, u, v) >= minimum);
    }

    private static int CountOpenCells(FineLayout layout, Cell start, int du, int dv) =>
        FloorScanner.CountOpenCells(layout.Cells.ContainsKey, layout.Edges.ContainsKey, start, du, dv);

    private static PlanRegion[] GetRooms(PlacementContext ctx) =>
        ctx.Plan.Regions.Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing && r != ctx.Plan.KeyRoom).ToArray();

    private static (double X, double Y) GetCenter(PlacementContext ctx, Cell c) => ctx.Layout.Lattice.GetCenter(c);

    private static double GetDistance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static void PlaceGate(PlacementContext ctx, LevelBudget budget, LevelScript script, BreakerSpot? breaker, KeypadTable keypads)
    {
        var plan = ctx.Plan;
        if (plan.Gate is GateKind.LockedDoors or GateKind.Blackout && breaker is not null)
        {
            ctx.Map.Add(KnownObject.Breaker, breaker.X, breaker.Y);
            ctx.Taken.Add(new Cell(breaker.Wall.A - 1, breaker.Wall.B));
            foreach ((var edge, char kind) in ctx.Layout.Edges.Where(p => p.Value is 'D' or 'E').OrderBy(p => p.Key.Axis).ThenBy(p => p.Key.A).ThenBy(p => p.Key.B))
            {
                ((double x, double y), bool slash) = StructureBuilder.GetLabPosition(ctx.Layout.Lattice, edge);
                var door = kind == 'D' ? KnownObject.SmallDoor : KnownObject.DoubleDoor;
                byte doorDir = (byte)(slash ? 0 : kind == 'D' ? 128 : 64);
                var keypad = keypads.Pick(ctx.Rng, door, doorDir, broken: false) ?? new PropPlacement(0, 0, 0, 5, doorDir);
                ctx.Map.Add(plan.Gate == GateKind.LockedDoors ? KnownObject.KeypadOff : KnownObject.KeypadOn, x + keypad.Dx, y + keypad.Dy, keypad.Z, keypad.Dir);
            }

            script.DoorsOpen = plan.Gate != GateKind.LockedDoors;
            if (plan is { Gate: GateKind.Blackout, BlackoutLink: not null })
            {
                var doorway = GetDoorwayCell(ctx, plan.BlackoutLink);
                script.Blackout = GetCenter(ctx, doorway);
            }
        }

        if (plan is { Gate: GateKind.Fences, KeyRoom: not null })
        {
            var entrance = GetDoorwayCell(ctx, plan.Links.First(l => l.To == plan.KeyRoom.Id));
            var back = ctx.GetFreeCells(plan.KeyRoom, inner: true)
                .OrderByDescending(c => Math.Abs(c.U - entrance.U) + Math.Abs(c.V - entrance.V)).ToList();
            if (back.Count == 0)
            {
                back = ctx.GetFreeCells(plan.KeyRoom).ToList();
            }

            for (int index = 0; index < Math.Max(1, budget.Generators) && back.Count > 0; index++)
            {
                var pool = back.Take(Math.Max(1, back.Count / 3)).ToArray();
                var placed = ctx.Place(pool, KnownObject.Generator, dir: 0);
                if (placed is null)
                {
                    break;
                }

                if (ctx.Level == 10 && GetDistance((placed.Value.X, placed.Value.Y), (7541, 4501)) < 150)
                {
                    // Avoid the 415 spot the level 10 script handles specially
                    ctx.Map.Sprites.RemoveAt(ctx.Map.Sprites.Count - 1);
                    back.Remove(placed.Value.Cell);
                    index--;
                    continue;
                }

                ctx.Map.Add(KnownObject.ShootHere, placed.Value.X, placed.Value.Y, 0, 0, unique: false);
                back.Remove(placed.Value.Cell);
            }

            foreach (var cell in back.Take(Math.Min(2, back.Count / 3)))
            {
                ctx.Place([cell], KnownObject.Transformer, dir: ctx.Rng.Next(2) == 0 ? 0 : 128);
            }
        }
    }

    private static Cell GetDoorwayCell(PlacementContext ctx, PlanLink link)
    {
        int index = ctx.Plan.Links.IndexOf(link);
        var edges = ctx.Layout.LinkEdges[index];
        var middle = edges[edges.Length / 2];
        var (a, b) = middle.Cells;
        return ctx.Layout.Cells.GetValueOrDefault(a, -1) == link.To ? a : b;
    }

    private static void PlaceBosses(PlacementContext ctx, LevelBudget budget, FacingTable facings)
    {
        var final = ctx.Plan.Stations[^1];
        var entrance = GetDoorwayCell(ctx, ctx.Plan.Links.First(l => l.To == final.Id));
        
        var far = ctx.GetFreeCells(final, inner: true)
            .OrderByDescending(c => Math.Abs(c.U - entrance.U) + Math.Abs(c.V - entrance.V))
            .ToArray();
        
        foreach ((var id, int count) in budget.Bosses.OrderBy(p => p.Key))
        {
            for (int index = 0; index < count; index++)
            {
                var boss = ctx.Place(far.Take(Math.Max(1, far.Length / 5)).ToArray(), id, dir: 0, payload: MapBuilder.MonsterPayload, army: 1);
                if (boss is null)
                {
                    continue;
                }

                for (int thirdIndex = 0; thirdIndex < 6; thirdIndex++)
                {
                    double angle = ctx.Rng.NextDouble() * 2 * Math.PI;
                    double distance = 60 + 120 * ctx.Rng.NextDouble();
                    (double x, double y) = (boss.Value.X + Math.Cos(angle) * distance, boss.Value.Y + Math.Sin(angle) * distance * 0.7);
                    if (ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(x, y)) && !ctx.IsInWall(x, y))
                    {
                        var prop = thirdIndex < 2 ? KnownObject.SoldierCorpse + thirdIndex : KnownObject.BloodPools[ctx.Rng.Next(KnownObject.BloodPools.Count)];
                        (double u, double v) = ctx.Layout.Lattice.ToLattice(x, y);
                        ctx.Map.Add(prop, x, y, 0, facings.PickFacing(ctx.Rng, prop, ctx.Layout.Edges, u, v), unique: false);
                    }
                }
            }
        }
    }

    private static PlanRegion? PlaceMountedCannons(PlacementContext ctx, LevelBudget budget)
    {
        if (budget.MountedCannons == 0)
        {
            return null;
        }

        var halls = ctx.Plan.Stations
            .Where(s => ctx.GetProgress(s) is >= 0.2 and <= 0.65)
            .OrderByDescending(s => s.Units.Count)
            .ToArray();
        
        foreach (var hall in halls)
        {
            var cells = ctx.GetFreeCells(hall, inner: true).ToArray();
            ctx.Rng.Shuffle(cells);
            foreach (var cell in cells)
            {
                (double x, double y) = GetCenter(ctx, cell);
                var ring = new List<((double X, double Y) Point, Cell Cell)>();
                foreach (int dx in new[] { -60, 0, 60 })
                {
                    foreach (int dy in new[] { -45, 0, 45 })
                    {
                        ring.Add(((x + dx, y + dy), ctx.Layout.Lattice.GetCell(x + dx, y + dy)));
                    }
                }

                if (ring.All(r => ctx.Layout.Cells.ContainsKey(r.Cell) && ctx.IsFree(r.Cell) && !ctx.IsInWall(r.Point.X, r.Point.Y, 8)))
                {
                    ctx.Map.Add(KnownObject.MountedCannon, x, y, 0, ctx.Rng.Next(2) == 0 ? 0 : 128, unique: false);
                    ctx.Taken.UnionWith(ring.Select(r => r.Cell));
                    return hall;
                }
            }
        }

        return null;
    }

    private static (double X, double Y, Cell Cell)[] PlaceSites(PlacementContext ctx, LevelBudget budget, IReadOnlyList<SiteTemplate> templates, IReadOnlyDictionary<EdgeKey, char> visibleEdges)
    {
        var sites = new List<(double X, double Y, Cell Cell)>();
        if (budget.DynamiteSites == 0 || templates.Count == 0)
        {
            return [];
        }

        var allowed = templates.Where(t => t.Rank <= SiteRankCap[ctx.Level]).ToArray();
        if (allowed.Length == 0)
        {
            allowed = templates.ToArray();
        }

        int n = budget.DynamiteSites;
        double earliest = n > 4 ? EarliestSite + 0.05 : EarliestSite;
        // With a key room, put sites in the rooms a nearest-room walk from it reaches last
        Func<PlanRegion, double> progressOf = ctx.GetProgress;
        if (ctx.Plan.KeyRoom is not null)
        {
            var walk = MeasureWalkOrder(ctx, GetKeyCell(ctx, ctx.Plan.KeyRoom), GetRooms(ctx));
            progressOf = walk.GetValueOrDefault;
        }

        var ordered = (ctx.Plan.KeyRoom is null ? ctx.Plan.Stations : GetRooms(ctx).Where(r => ctx.GetProgress(r) >= MinSiteStartProgress)).OrderBy(progressOf).ToArray();
        
        var regions = ordered.Where(r => progressOf(r) >= earliest).ToArray();
        if (regions.Length == 0)
        {
            regions = ordered.Skip(ordered.Length * 2 / 3).ToArray();
        }

        var chosen = Enumerable.Range(0, n).Select(j => regions[(int)((j + 0.5) * regions.Length / n)]).ToArray();
        bool lab = ctx.Plan.Theme.Theme == LevelTheme.Lab;
        foreach (var region in chosen)
        {
            (double X, double Y, Cell Cell)? site = null;
            if (lab)
            {
                double progress = progressOf(region);
                var late = GetRooms(ctx)
                    .Where(r => progressOf(r) >= earliest)
                    .Concat(regions)
                    .Distinct()
                    .ToArray();
                
                foreach (var innerRegion in late.OrderBy(r => Math.Abs(progressOf(r) - progress)).ThenBy(r => r.Id))
                {
                    site = PlaceLabSite(ctx, innerRegion, allowed, visibleEdges);
                    if (site is not null)
                    {
                        break;
                    }
                }
            }

            if (site is null)
            {
                var pool = ctx.GetFreeCells(region, inner: true).ToArray();
                if (pool.Length == 0)
                {
                    pool = ctx.GetFreeCells(region).ToArray();
                }

                ctx.Rng.Shuffle(pool);
                foreach (var cell in pool)
                {
                    (double x, double y) = GetCenter(ctx, cell);
                    if (!ctx.IsInWall(x, y, 20) && !IsCrowded(ctx, x, y))
                    {
                        StampSite(ctx, allowed[ctx.Rng.Next(allowed.Length)], x, y, lab ? 0.3 : 1.0);
                        ctx.Taken.Add(cell);
                        site = (x, y, cell);
                        break;
                    }
                }

                if (site is null)
                {
                    continue;
                }
            }

            sites.Add(site.Value);
        }

        return sites.OrderBy(s => ctx.Distance.GetValueOrDefault(s.Cell)).ToArray();
    }

    // Turn a far wall piece into a monster hole, with the 179 in front and the endless generators behind it
    private static (double X, double Y, Cell Cell)? PlaceLabSite(PlacementContext ctx, PlanRegion region, SiteTemplate[] templates, IReadOnlyDictionary<EdgeKey, char> visibleEdges)
    {
        var cells = ctx.GetRegionCells(region).ToHashSet();

        var walls = visibleEdges.Keys
            .Where(k =>
            {
                var floor = GetFloorSide(k);
                var next = k.Axis == 'v' ? new EdgeKey('v', k.A + 1, k.B) : new EdgeKey('u', k.A, k.B + 1);
                return floor is not null && !ctx.Taken.Contains(floor.Value) && ctx.Distance.ContainsKey(floor.Value)
                       && GetFloorSide(StructureBuilder.GetPreviousEdge(k)) is not null && GetFloorSide(next) is not null;
            }).OrderBy(k => k.Axis)
            .ThenBy(k => k.A)
            .ThenBy(k => k.B)
            .ToArray();
        
        walls = walls.Where(k =>
        {
            ((double x, double y), bool slash) = StructureBuilder.GetLabPosition(ctx.Layout.Lattice, k);
            return !IsCrowded(ctx, x + 25 * (slash ? 1 : -1), y + 20);
        }).ToArray();
        
        if (walls.Length == 0)
        {
            return null;
        }

        var wall = walls[ctx.Rng.Next(walls.Length)];
        ((double hx, double hy), bool slash) = StructureBuilder.GetLabPosition(ctx.Layout.Lattice, wall);
        int index = ctx.Map.Sprites.FindIndex(s => s.Id is KnownObject.LabWallSlash or KnownObject.LabWallBack && Math.Abs(s.X - hx) < 1 && Math.Abs(s.Y - hy) < 1);
        if (index < 0)
        {
            return null;
        }

        var hole = ctx.Level >= 3 || ctx.Rng.Next(2) == 0 ? KnownObject.BigMonsterHole : KnownObject.SmallMonsterHole;
        ctx.Map.Sprites[index] = ctx.Map.Sprites[index] with { Id = hole, Dir = (byte)(slash ? 0 : 128) };
        int side = slash ? 1 : -1;
        (double sx, double sy) = (hx + 25 * side, hy + 20);
        ctx.Map.Add(KnownObject.DynamiteSite, sx, sy, 0, 0, unique: false);
        var template = templates[ctx.Rng.Next(templates.Length)];
        
        var generators = template.Members
            .Where(m => m.Id.IsEndlessGenerator)
            .ToArray();
        
        if (generators.Length == 0)
        {
            generators = [new SiteMember(KnownObject.FirstEndlessGenerator, 0, 0, 0, 0, 1, new byte[8])];
        }

        foreach (var generator in generators)
        {
            ctx.Map.Add(generator.Id, hx - 30 * side + ctx.Rng.NextDouble() * 16 - 8, hy - 42 + ctx.Rng.NextDouble() * 12 - 6, generator.Z, generator.Dir, generator.Payload, generator.Army, unique: false);
        }

        var floorCell = GetFloorSide(wall)!.Value;
        ctx.Taken.Add(floorCell);
        return (sx, sy, floorCell);

        Cell? GetFloorSide(EdgeKey k)
        {
            var (c1, c2) = k.Cells;
            var (floor, outside) = k.Axis == 'v' ? (c2, c1) : (c1, c2);
            return visibleEdges.GetValueOrDefault(k) == 'W' && cells.Contains(floor) && !ctx.Layout.Cells.ContainsKey(outside) ? floor : null;
        }
    }

    // Count crates, barrels, chairs and the bio-container too, even though they aren't solid
    private static bool IsCrowded(PlacementContext ctx, double x, double y) => ctx.Map.Sprites.Any(s =>
    {
        var obj = ctx.Objects.Find(s.Id);
        if (obj is null || s.Z >= ClutterHeight || obj is { IsSolid: false, ExtentZ: < StandingHeight } || !s.Id.IsDecor && s.Id != KnownObject.Crate)
        {
            return false;
        }

        double gap = SiteClearanceRule.MeasureGap(obj, s.X, s.Y, x, y);
        return gap < SiteClearanceRule.GetMinGap(obj);
    });

    private static void StampSite(PlacementContext ctx, SiteTemplate template, double x, double y, double shrink)
    {
        ctx.Map.Add(KnownObject.DynamiteSite, x, y, template.Z, template.Dir, unique: false);
        foreach (var member in template.Members)
        {
            double mx = x + member.Dx * shrink;
            double my = y + member.Dy * shrink;
            if (shrink == 1.0 && (!ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(mx, my)) || ctx.IsInWall(mx, my, 2)))
            {
                (mx, my) = (x + member.Dx * 0.3, y + member.Dy * 0.3);
            }

            if (member.Id == KnownObject.TeleportPad && ctx.IsInWall(mx, my, 2))
            {
                (mx, my) = (x, y);
            }

            ctx.Map.Add(member.Id, mx, my, member.Z, member.Dir, member.Payload, member.Army, unique: false);
        }
    }

    // Group sites in runs of up to three and put each run's charges together before its first site
    private static void PlaceCharges(PlacementContext ctx, (double X, double Y, Cell Cell)[] sites, IReadOnlyDictionary<Cell, int> distance, Func<Cell, bool> isAllowed)
    {
        int previousDistance = 0;
        int start = 0;
        while (start < sites.Length)
        {
            int remaining = sites.Length - start;
            var run = sites[start..(start + (remaining == 4 ? 2 : Math.Min(3, remaining)))];
            start += run.Length;

            int siteDistance = distance.GetValueOrDefault(run[0].Cell);
            double ideal = siteDistance - 1200 / ctx.Step;
            var onRoute = ctx.Plan.Stations.Select(r => r.Id).ToHashSet();
            var candidates = FindSpots(true, previousDistance, 400);
            if (candidates.Length == 0)
            {
                candidates = FindSpots(false, previousDistance, 400);
            }

            if (candidates.Length == 0)
            {
                candidates = FindSpots(false, 0, 300);
            }

            if (candidates.Length == 0)
            {
                continue;
            }

            var spot = candidates[ctx.Rng.Next(candidates.Length)];
            var cluster = new List<Cell> { spot };
            foreach (var (n, _) in EdgeKey.GetNeighbors(spot))
            {
                if (ctx.IsFree(n))
                {
                    cluster.Add(n);
                }
            }

            for (int runIndex = 0; runIndex < run.Length; runIndex++)
            {
                ctx.Place([cluster[Math.Min(runIndex, cluster.Count - 1)]], KnownObject.Dynamite, dir: 0, take: false);
            }

            ctx.Taken.UnionWith(cluster);
            previousDistance = distance.GetValueOrDefault(run[^1].Cell);
            continue;

            Cell[] FindSpots(bool routeOnly, int lowest, double closest) => distance
                .Where(p => p.Value >= lowest && p.Value < siteDistance - 4 && ctx.IsFree(p.Key) && isAllowed(p.Key) && (!routeOnly || onRoute.Contains(ctx.Layout.Cells[p.Key])))
                .Select(p => p.Key)
                .Where(c =>
                {
                    var point = GetCenter(ctx, c);
                    double nearest = sites.Min(s => GetDistance(point, (s.X, s.Y)));
                    return nearest >= closest && nearest <= 2800;
                })
                .OrderBy(c => Math.Abs(distance[c] - ideal)).ThenBy(c => c.U).ThenBy(c => c.V)
                .Take(12)
                .ToArray();
        }
    }

    private static void PlacePlacedGroups(PlacementContext ctx, LevelBudget budget)
    {
        var groups = budget.PlacedGroups
            .OrderBy(g => g.Sum(m => ctx.Objects.Find(m.Id)?.HitPoints ?? 0))
            .ToArray();
        
        var rooms = ctx.Plan.Stations
            .Skip(1)
            .Concat(ctx.Plan.Regions.Where(r => r.Kind == RegionKind.Wing))
            .Concat(ctx.Plan.Branches.SelectMany(b => b))
            .ToArray();
        
        var start = GetCenter(ctx, ctx.Start);
        int last = budget.Bosses.Count == 0 ? groups.Length - 1 : -1;
        var tips = new Dictionary<int, PlanRegion>();
        foreach (var tip in ctx.Plan.Branches.Select(b => b.MaxBy(r => GetFarthestDistance(ctx, r))!).OrderByDescending(r => GetFarthestDistance(ctx, r)))
        {
            double progress = ctx.GetProgress(tip);
            int best = Enumerable.Range(0, groups.Length).Where(i => i != last && !tips.ContainsKey(i))
                .OrderBy(i => Math.Abs(i / (double)Math.Max(1, groups.Length - 1) - progress)).DefaultIfEmpty(-1).First();
            if (best >= 0)
            {
                tips[best] = tip;
            }
        }

        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            double f = groupIndex / (double)Math.Max(1, groups.Length - 1);
            var candidates = groupIndex == last
                ? [ctx.Plan.Stations[^1]]
                : tips.TryGetValue(groupIndex, out var tip) ? [tip] : rooms.Where(r => Math.Abs(ctx.GetProgress(r) - f) < 0.2).ToArray();
            if (candidates.Length == 0)
            {
                candidates = rooms;
            }

            if (!TryPlaceGroup(ctx, groups[groupIndex], candidates, start))
            {
                foreach (var group in groups[groupIndex])
                {
                    var r = candidates[ctx.Rng.Next(candidates.Length)];
                    ctx.Place(ctx.GetFreeCells(r).Where(c => GetDistance(GetCenter(ctx, c), start) >= 900).ToArray(), group.Id, group.Dir, MapBuilder.MonsterPayload, 1, take: false);
                }
            }
        }
    }

    private static Cell GetKeyCell(PlacementContext ctx, PlanRegion keyRoom) => ctx.GetRegionCells(keyRoom).First(ctx.Distance.ContainsKey);

    private static Dictionary<PlanRegion, double> MeasureWalkOrder(PlacementContext ctx, Cell start, IReadOnlyList<PlanRegion> rooms)
    {
        var order = new Dictionary<PlanRegion, double>();
        var left = rooms.ToHashSet();
        var current = start;
        while (left.Count > 0)
        {
            var distance = ctx.MeasureDistances(current);
            var reached = left
                .SelectMany(r => ctx.GetRegionCells(r).Where(distance.ContainsKey).Select(c => (Region: r, Cell: c)))
                .ToArray();
            
            if (reached.Length == 0)
            {
                break;
            }

            var next = reached.MinBy(p => distance[p.Cell]);
            order[next.Region] = order.Count / (double)Math.Max(1, rooms.Count - 1);
            left.Remove(next.Region);
            var cells = ctx.GetRegionCells(next.Region).Where(distance.ContainsKey).ToArray();
            current = cells[cells.Length / 2];
        }

        return order;
    }

    private static int GetFarthestDistance(PlacementContext ctx, PlanRegion region) =>
        ctx.GetRegionCells(region).Select(c => ctx.Distance.GetValueOrDefault(c)).DefaultIfEmpty(0).Max();

    private static bool TryPlaceGroup(PlacementContext ctx, IReadOnlyList<GroupMember> group, IReadOnlyList<PlanRegion> regions, (double X, double Y) start)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var r = regions[ctx.Rng.Next(regions.Count)];
            var anchors = ctx.GetFreeCells(r).Where(c => GetDistance(GetCenter(ctx, c), start) >= 900).ToArray();
            if (anchors.Length == 0)
            {
                continue;
            }

            (double ax, double ay) = GetCenter(ctx, anchors[ctx.Rng.Next(anchors.Length)]);
            var spots = new List<(GroupMember Member, double X, double Y)>();
            foreach (var member in group)
            {
                double x = ax + member.Dx;
                double y = ay + member.Dy;
                var c = ctx.Layout.Lattice.GetCell(x, y);
                bool closet = ctx.Layout.Cells.TryGetValue(c, out int region) && ctx.Plan.Regions[region].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset;
                if (ctx.Distance.ContainsKey(c) && !closet && !ctx.IsInWall(x, y, 2) && GetDistance((x, y), start) >= 900)
                {
                    spots.Add((member, x, y));
                }
            }

            if (spots.Count >= Math.Max(1, (int)(group.Count * 0.8)))
            {
                foreach ((var member, double x, double y) in spots)
                {
                    ctx.Map.Add(member.Id, x, y, 0, member.Dir, MapBuilder.MonsterPayload, 1, unique: false);
                }

                return true;
            }
        }

        return false;
    }

    // Fill spawn closets with the weakest ambushes, then the blackout trap, military sites and the cannon hall.
    // Trigger the rest at station doorways in the first half of the route.
    private static void PlaceAmbushes(PlacementContext ctx, LevelBudget budget, IReadOnlyDictionary<EdgeKey, int> spawnWalls,
        (double X, double Y, Cell Cell)[] sites, PlanRegion? cannonHall)
    {
        var ambushes = budget.Ambushes
            .OrderBy(a => a.Sum(m => ctx.Objects.Find(m)?.HitPoints ?? 0))
            .ToList();
        
        if (ambushes.Count == 0)
        {
            return;
        }

        var closets = ctx.Plan.Regions
            .Where(r => r.Kind == RegionKind.SpawnCloset)
            .OrderBy(r => ctx.Plan.Stations.FindIndex(st => st.Id == r.Host))
            .ToArray();
        
        foreach (var closet in closets)
        {
            if (ambushes.Count == 0)
            {
                return;
            }

            if (PlaceClosetAmbush(ctx, closet, ambushes[0], spawnWalls, budget))
            {
                ambushes.RemoveAt(0);
            }
        }

        if (ctx.Plan is { Gate: GateKind.Blackout, KeyRoom: not null } && ambushes.Count > 0)
        {
            // Spring an ambush when the blackout breaker restores power
            var keyLink = ctx.Plan.Links.First(l => l.To == ctx.Plan.KeyRoom.Id);
            PlaceAmbush(ctx, ambushes[0], GetDoorwayCell(ctx, keyLink));
            ambushes.RemoveAt(0);
        }

        if (ctx.Plan.Theme.Theme == LevelTheme.Military)
        {
            for (int siteIndex = 0; siteIndex < sites.Length && ambushes.Count > 0; siteIndex++)
            {
                int index = Math.Min(ambushes.Count - 1, (int)((siteIndex + 0.5) * ambushes.Count / sites.Length));
                var region = ctx.Plan.Regions[ctx.Layout.Cells[sites[siteIndex].Cell]];
                var entry = ctx.Plan.Links.FirstOrDefault(l => l.To == region.Id);
                var atPad = ctx.GetFreeCells(region).Where(c => GetDistance(GetCenter(ctx, c), (sites[siteIndex].X, sites[siteIndex].Y)) < 260).ToArray();
                if (entry is null || atPad.Length == 0)
                {
                    continue;
                }

                PlaceAmbush(ctx, ambushes[index], GetDoorwayCell(ctx, entry), atPad[ctx.Rng.Next(atPad.Length)]);
                ambushes.RemoveAt(index);
            }
        }

        if (cannonHall is not null)
        {
            var entry = ctx.Plan.Links.FirstOrDefault(l => l.To == cannonHall.Id);
            for (int thirdIndex = 0; thirdIndex < 2 && entry is not null && ambushes.Count > 0; thirdIndex++)
            {
                int index = ambushes.Count / 2;
                PlaceAmbush(ctx, ambushes[index], GetDoorwayCell(ctx, entry));
                ambushes.RemoveAt(index);
            }
        }

        var doorways = new List<(Cell Cell, double Progress)>();
        for (int stationIndex = 1; stationIndex < ctx.Plan.Stations.Count; stationIndex++)
        {
            var link = ctx.Plan.Links.First(l => l.To == ctx.Plan.Stations[stationIndex].Id);
            var c = GetDoorwayCell(ctx, link);
            if (ctx.Distance.ContainsKey(c))
            {
                doorways.Add((c, ctx.GetProgress(c)));
            }
        }

        for (int ambushIndex = 0; ambushIndex < ambushes.Count && doorways.Count > 0; ambushIndex++)
        {
            double target = 0.04 + 0.42 * (ambushIndex + 0.5) / ambushes.Count;
            var trigger = doorways.OrderBy(d => Math.Abs(d.Progress - target)).First().Cell;
            PlaceAmbush(ctx, ambushes[ambushIndex], trigger);
        }
    }

    // Blow the closet wall open on the same trigger as its spawner
    private static bool PlaceClosetAmbush(PlacementContext ctx, PlanRegion closet, IReadOnlyList<KnownObject> monsters, IReadOnlyDictionary<EdgeKey, int> spawnWalls, LevelBudget budget)
    {
        int linkIndex = ctx.Plan.Links.FindIndex(l => l.To == closet.Id);
        var hole = ctx.Layout.LinkEdges[linkIndex][0];
        if (!spawnWalls.TryGetValue(hole, out int wallIndex))
        {
            return false;
        }

        var (a, b) = hole.Cells;
        var (inside, outside) = ctx.Layout.Cells.GetValueOrDefault(a, -1) == closet.Id ? (a, b) : (b, a);
        var front = new Cell(outside.U + 2 * (outside.U - inside.U), outside.V + 2 * (outside.V - inside.V));
        var trigger = ctx.Layout.Cells.ContainsKey(front) ? front : outside;
        (double tx, double ty) = GetCenter(ctx, trigger);
        double radius = new[] { 180, 220, 260 }[ctx.Rng.Next(3)];
        var cells = ctx.GetRegionCells(closet);
        int triggerDistance = ctx.Distance.GetValueOrDefault(trigger);
        var onRoute = ctx.Plan.Stations.Select(r => r.Id).ToHashSet();
        var onward = ctx.Distance
            .Where(p => p.Value >= triggerDistance + 500 / ctx.Step && p.Value <= triggerDistance + 800 / ctx.Step && onRoute.Contains(ctx.Layout.Cells[p.Key]))
            .OrderBy(p => GetDistance(GetCenter(ctx, p.Key), (tx, ty))).ThenBy(p => p.Key.U).ThenBy(p => p.Key.V)
            .Select(p => (Cell?)p.Key).FirstOrDefault();
        List<(double X, double Y)> way = onward is null ? [(tx, ty)] : [(tx, ty), GetCenter(ctx, onward.Value)];
        byte[] payload = SpawnerProgram.Create(monsters, (tx, ty, radius), way).ToPayload();
        (double sx, double sy) = GetCenter(ctx, inside);
        ctx.Map.Add(KnownObject.Spawner, sx, sy, 0, 0, payload, 1, unique: false);
        var wallProgram = new SpawnerProgram
        {
            Actions = [new SpawnerAction(SpawnerAction.Stop, 0, 0, 0), new SpawnerAction(15, 0, 0, 0), new SpawnerAction(SpawnerAction.Trigger, (int)tx, (int)ty, (int)radius)],
            Trailer = new byte[4]
        };
        ctx.Map.Sprites[wallIndex] = ctx.Map.Sprites[wallIndex] with { Payload = wallProgram.ToPayload() };
        if (ctx.Rng.NextDouble() < 0.3)
        {
            foreach (var item in ItemSchedule.CreateStash(ctx.Level, ctx.Rng, () => TakeWeapon(ctx, budget)).Take(2))
            {
                var c = cells[ctx.Rng.Next(cells.Count)];
                (double x, double y) = GetCenter(ctx, c);
                ctx.Map.Add(item, x, y, 0, 0, unique: false);
                TakeFromBudget(budget, item);
            }
        }

        return true;
    }

    private static void PlaceAmbush(PlacementContext ctx, IReadOnlyList<KnownObject> monsters, Cell trigger, Cell? spawnerCell = null)
    {
        (double tx, double ty) = GetCenter(ctx, trigger);
        int triggerDistance = ctx.Distance.GetValueOrDefault(trigger);
        int ahead = (int)(0.05 * ctx.RouteLength);

        Cell spawner;
        if (spawnerCell is not null)
        {
            spawner = spawnerCell.Value;
        }
        else
        {
            var candidates = ctx.Distance
                .Where(p => p.Value >= triggerDistance + ahead && IsHidden(p.Key, 1000, 3200))
                .Select(p => p.Key)
                .OrderBy(c => c.U)
                .ThenBy(c => c.V)
                .ToArray();
            
            if (candidates.Length == 0)
            {
                candidates = ctx.Distance
                    .Where(p => p.Value > triggerDistance && IsHidden(p.Key, 900, 3500))
                    .Select(p => p.Key)
                    .OrderBy(c => c.U)
                    .ThenBy(c => c.V)
                    .ToArray();
            }

            if (candidates.Length == 0)
            {
                candidates = ctx.Distance
                    .Where(p => p.Value > triggerDistance && ctx.IsFree(p.Key))
                    .OrderByDescending(p => p.Value)
                    .Take(20)
                    .Select(p => p.Key)
                    .ToArray();
            }

            if (candidates.Length == 0)
            {
                return;
            }

            spawner = candidates[ctx.Rng.Next(candidates.Length)];
        }

        var path = ctx.FindPath(spawner, trigger);
        int meeting = Math.Max(1, (int)(path.Length * 0.4));
        meeting = Math.Min(meeting, path.Length - 1);
        int stride = Math.Max(1, meeting / 4);
        var way = new List<(double X, double Y)>();
        for (int index = stride; index < meeting; index += stride)
        {
            way.Add(GetCenter(ctx, path[index]));
        }

        way.Add(GetCenter(ctx, path[meeting]));
        double radius = ctx.Plan.Theme.Theme == LevelTheme.Lab ? new[] { 250, 300, 350 }[ctx.Rng.Next(3)] : new[] { 300, 350, 400, 450 }[ctx.Rng.Next(4)];
        byte[] payload = SpawnerProgram.Create(monsters, (tx, ty, radius), way.TakeLast(6)).ToPayload();
        ctx.Place([spawner], KnownObject.Spawner, dir: 0, payload: payload, army: 1, take: false);
        return;

        bool IsHidden(Cell c, double low, double high)
        {
            (double x, double y) = GetCenter(ctx, c);
            double d = GetDistance((x, y), (tx, ty));
            return d >= low && d <= high && (Math.Abs(x - tx) >= 640 || Math.Abs(y - ty) >= 400) && ctx.IsFree(c);
        }
    }

    private static void PlaceStashes(PlacementContext ctx, LevelBudget budget)
    {
        foreach (var closet in ctx.Plan.Regions.Where(r => r.Kind == RegionKind.SecretCloset))
        {
            var cells = ctx.GetRegionCells(closet);
            foreach (var item in ItemSchedule.CreateStash(ctx.Level, ctx.Rng, () => TakeWeapon(ctx, budget)))
            {
                var c = cells[ctx.Rng.Next(cells.Count)];
                (double x, double y) = ctx.Layout.Lattice.ToWorld(c.U + 0.3 + 0.4 * ctx.Rng.NextDouble(), c.V + 0.3 + 0.4 * ctx.Rng.NextDouble());
                ctx.Map.Add(item, x, y, 0, 0, unique: false);
                TakeFromBudget(budget, item);
            }
        }
    }

    // Stash items come out of the level's pickups
    private static void TakeFromBudget(LevelBudget budget, KnownObject item)
    {
        if (budget.Items.TryGetValue(item, out int left) && left > 0)
        {
            budget.Items[item] = left - 1;
        }
    }

    // Hand out weapons that are early for the level first
    private static KnownObject? TakeWeapon(PlacementContext ctx, LevelBudget budget)
    {
        if (budget.Weapons.Count == 0)
        {
            return null;
        }

        int index = budget.Weapons.FindIndex(w => !ItemSchedule.IsAllowed(w, ctx.Level));
        if (index < 0)
        {
            index = ctx.Rng.Next(budget.Weapons.Count);
        }

        var weapon = budget.Weapons[index];
        budget.Weapons.RemoveAt(index);
        return weapon;
    }

    private static List<(double X, double Y)> GetWeaponSpots(PlacementContext ctx) => ctx.Map.Sprites
        .Where(s => ItemSchedule.IsWeapon(s.Id) || (s.Id == KnownObject.Crate && ItemSchedule.IsWeapon(KnownObject.GetCrateItem(s.Payload))))
        .Select(s => (s.X, s.Y)).ToList();

    private static int CountNear(IReadOnlyList<(double X, double Y)> spots, (double X, double Y) p) => spots.Count(s => GetDistance(s, p) < WeaponSpacing);

    // Put about a third of the weapons in crates and the rest in the middle side rooms, spaced apart
    private static void PlaceWeapons(PlacementContext ctx, LevelBudget budget, IReadOnlyList<int> crates)
    {
        var weapons = new List<KnownObject>();
        foreach (var weapon in budget.Weapons)
        {
            var replacement = ItemSchedule.IsAllowed(weapon, ctx.Level) ? weapon
                : ItemSchedule.GetWeapons(ctx.Level).Where(a => !weapons.Contains(a) && !budget.Weapons.Contains(a)).DefaultIfEmpty(ItemSchedule.GetAmmo(weapon)).Last();
            weapons.Add(replacement);
        }

        budget.Weapons.Clear();
        var spots = GetWeaponSpots(ctx);
        int inCrates = (int)Math.Round(weapons.Count(ItemSchedule.IsWeapon) * WeaponCrateShare);
        foreach (int crate in crates)
        {
            var s = ctx.Map.Sprites[crate];
            int index = weapons.FindIndex(ItemSchedule.IsWeapon);
            if (inCrates == 0 || index < 0)
            {
                break;
            }

            if (KnownObject.GetCrateItem(s.Payload) != 0 || CountNear(spots, (s.X, s.Y)) > 0)
            {
                continue;
            }

            ctx.Map.Sprites[crate] = s with { Payload = CreateCratePayload(weapons[index]) };
            spots.Add((s.X, s.Y));
            weapons.RemoveAt(index);
            inCrates--;
        }

        var rooms = GetRooms(ctx);
        var middle = rooms.Where(r => ctx.GetProgress(r) is >= 0.25 and <= 0.75 && r.Kind == RegionKind.SideRoom).ToArray();
        foreach (var item in weapons)
        {
            if (!ItemSchedule.IsWeapon(item))
            {
                ctx.Place(ctx.GetFreeCells(rooms[ctx.Rng.Next(rooms.Length)]), item, dir: 0);
                continue;
            }

            (double X, double Y, Cell Cell)? placed = null;
            foreach (int allowed in new[] { 0, 1 })
            {
                foreach (var pool in new[] { middle, rooms })
                {
                    var cells = pool.SelectMany(r => ctx.GetFreeCells(r)).Where(c => CountNear(spots, GetCenter(ctx, c)) <= allowed).ToArray();
                    placed = ctx.Place(cells, item, dir: 0);
                    if (placed is not null)
                    {
                        break;
                    }
                }

                if (placed is not null)
                {
                    break;
                }
            }

            if (placed is not null)
            {
                spots.Add((placed.Value.X, placed.Value.Y));
            }
        }
    }

    private static byte[] CreateCratePayload(KnownObject item)
    {
        byte[] payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)item);
        return payload;
    }

    private static void PlaceItems(PlacementContext ctx, LevelBudget budget)
    {
        var items = budget.Items.OrderBy(p => p.Key).SelectMany(p => Enumerable.Repeat(p.Key, p.Value)).ToList();
        int[] order = Enumerable.Range(0, items.Count).ToArray();
        ctx.Rng.Shuffle(order);
        items = order.Select(i => items[i]).ToList();

        int[] crates = Enumerable.Range(0, ctx.Map.Sprites.Count).Where(i => ctx.Map.Sprites[i].Id == KnownObject.Crate).ToArray();
        int[] crateOrder = crates.ToArray();
        ctx.Rng.Shuffle(crateOrder);
        PlaceWeapons(ctx, budget, crateOrder.ToArray());
        crateOrder = crateOrder.Where(i => KnownObject.GetCrateItem(ctx.Map.Sprites[i].Payload) == 0).ToArray();
        int nextCrate = 0;
        foreach ((var category, double share) in new[] { (ItemCategory.Ammo, .32), (ItemCategory.Armor, .45), (ItemCategory.Health, .2) })
        {
            var ofCategory = items.Where(i => ItemCategorizer.GetCategory(i) == category).ToArray();
            foreach (var item in ofCategory.Take((int)Math.Round(ofCategory.Length * share)))
            {
                if (nextCrate >= crateOrder.Length)
                {
                    break;
                }

                int index = crateOrder[nextCrate++];
                ctx.Map.Sprites[index] = ctx.Map.Sprites[index] with { Payload = CreateCratePayload(item) };
                items.Remove(item);
            }
        }

        var rooms = GetRooms(ctx);
        foreach (var item in items.Where(i => i != KnownObject.Dynamite))
        {
            var r = rooms[ctx.Rng.Next(rooms.Length)];
            ctx.Place(ctx.GetFreeCells(r), item, dir: 0);
        }
    }

    private static void PlaceCheckpoint(PlacementContext ctx, LevelScript script)
    {
        var station = ctx.Plan.Stations.OrderBy(s => Math.Abs(ctx.GetProgress(s) - 0.3)).First();
        var safe = ctx.GetFreeCells(station, inner: true)
            .Where(c => ctx.IsClearAround(c) && !ctx.IsInWall(GetCenter(ctx, c).X, GetCenter(ctx, c).Y, 24))
            .ToArray();
        if (safe.Length > 0)
        {
            script.Checkpoint = GetCenter(ctx, safe[safe.Length / 2]);
        }
    }
}
