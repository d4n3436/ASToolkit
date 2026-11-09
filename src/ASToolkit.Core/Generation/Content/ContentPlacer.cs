using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Generation.Content;

internal static class ContentPlacer
{
    private static readonly PointF BreakerOffset = new(-8, 3);
    private const float ChargeSiteGap = 588;
    private const int BreakerFront = 3;
    private const int BreakerSide = 2;
    private const int BreakerOpenEnd = 3;
    private const int BreakerWallRun = 4;
    private const float BreakerGateReach = 400;
    private const float BreakerGateSlack = 100;
    private const float GoreClearance = 1;
    // Holds how many pixels inside the trigger room's doorway the blackout's reach stops
    private const int BlackoutDoorGap = 94;
    private const float LatestBossStart = 0.65f;
    private static readonly KnownObject[] GorePools = [KnownObject.SmallBloodPool, KnownObject.BloodPool];

    public static BreakerSpot? ChooseBreakerSpot(Random rng, LayoutPlan plan, FineLayout layout, IReadOnlyDictionary<EdgeKey, EdgeKind> built)
    {
        var room = plan.KeyRoom;
        if (room is null)
        {
            return null;
        }

        var cells = layout.GetRegionCells(room.Id).ToHashSet();
        var candidates = built
            .Where(pair => pair is { Value: EdgeKind.Wall, Key.Axis: 'u' } && cells.Contains(new Cell(pair.Key.A - 1, pair.Key.B)) && !cells.Contains(new Cell(pair.Key.A, pair.Key.B)))
            .Select(pair => pair.Key)
            .OrderBy(edge => edge.A)
            .ThenBy(edge => edge.B)
            .Select(edge => (Wall: edge, Front: CountOpenCells(layout, new Cell(edge.A - 1, edge.B), -1, 0), Side: CountOpenCells(layout, new Cell(edge.A - 1, edge.B), 0, 1)))
            .Where(candidate => WallRun.Find(built, candidate.Wall).IsEnclosed(BreakerOpenEnd))
            .ToArray();

        candidates = Prefer(candidates, candidate => !layout.Cells.ContainsKey(new Cell(candidate.Wall.A, candidate.Wall.B)));
        candidates = Prefer(candidates, candidate => WallRun.Find(built, candidate.Wall).Length >= BreakerWallRun);
        var walls = Prefer(candidates, candidate => candidate is { Front: >= BreakerFront, Side: >= BreakerSide })
            .Where(candidate => candidate is { Front: >= BreakerFront - 1, Side: >= BreakerSide - 1 })
            .Select(candidate => candidate.Wall)
            .ToArray();

        var gates = plan.Links.Where(link => plan.Regions[link.From].IsGated != plan.Regions[link.To].IsGated).ToArray();
        if (plan.Gate == GateKind.LockedDoors && gates.Length > 0 && walls.Length > 0)
        {
            var doors = gates.SelectMany(link => layout.LinkEdges[link.Id]).Select(edge => StructureBuilder.GetBasementPosition(layout.Lattice, edge).Position).ToArray();
            float nearest = walls.Min(Reach);
            walls = walls.Where(wall => Reach(wall) <= Math.Max(BreakerGateReach, nearest + BreakerGateSlack)).ToArray();

            float Reach(EdgeKey wall)
            {
                var point = StructureBuilder.GetBasementPosition(layout.Lattice, wall).Position;
                return doors.Min(door => GetDistance(point, door));
            }
        }

        if (walls.Length == 0)
        {
            return null;
        }

        var wall = walls[rng.Next(walls.Length)];
        ((float wx, float wy), _) = StructureBuilder.GetBasementPosition(layout.Lattice, wall);
        return new BreakerSpot(wall, new PointF(wx + BreakerOffset.X, wy + BreakerOffset.Y));
    }

    private static T[] Prefer<T>(T[] items, Func<T, bool> predicate)
    {
        var kept = items.Where(predicate).ToArray();
        return kept.Length > 0 ? kept : items;
    }

    public static void Place(PlacementContext ctx, LevelBudget budget, bool hasCheckpoint, bool blackoutIsCheckpoint, LevelScript script, BreakerSpot? breaker,
        IReadOnlyDictionary<EdgeKey, EdgeKind> builtEdges, IReadOnlyDictionary<EdgeKey, int> spawnWalls, FacingTable facings, StyleModel model, IReadOnlyList<PlannedFight> fights)
    {
        PlaceGate(ctx, script, breaker);
        PlaceBosses(ctx, facings, builtEdges);
        var sites = ctx.Map.Sprites
            .Where(sprite => sprite.Id == KnownObject.DynamiteSite)
            .Select(sprite => (Point: new PointF(sprite.Position.X, sprite.Position.Y), Cell: ctx.Layout.Lattice.GetCell(sprite.Position)))
            .OrderBy(site => ctx.Distance.GetValueOrDefault(site.Cell))
            .ToArray();

        PlaceCharges(ctx, sites, model.Economy);
        new EncounterPlacer(ctx, model.Encounters, spawnWalls).Place(fights);
        new PickupPlacer(ctx, model.Economy).Place(budget);
        if (hasCheckpoint && (script.Blackout is null || !blackoutIsCheckpoint))
        {
            PlaceCheckpoint(ctx, script, fights);
        }
    }

    private static int CountOpenCells(FineLayout layout, Cell start, int du, int dv) =>
        FloorScanner.CountOpenCells(layout.Cells.ContainsKey, layout.Edges.ContainsKey, start, du, dv);

    // Returns whether a room lies on the chain of stations or one room off it, where the originals keep their fights and objectives
    private static bool IsNearBody(PlacementContext ctx, PlanRegion region) =>
        region.Kind == RegionKind.Station || (region.Host is not null && ctx.Plan.Regions[region.Host.Value].Kind == RegionKind.Station);

    private static PointF GetCenter(PlacementContext ctx, Cell c) => ctx.Layout.Lattice.GetCenter(c);

    internal static PointF GetMiddle(PlacementContext ctx, PlanRegion region)
    {
        var cells = ctx.GetRegionCells(region);
        return GetCenter(ctx, cells[cells.Count / 2]);
    }

    internal static float GetDistance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static void PlaceGate(PlacementContext ctx, LevelScript script, BreakerSpot? breaker)
    {
        var plan = ctx.Plan;
        if (plan.Gate is not (GateKind.LockedDoors or GateKind.Blackout) || breaker is null)
        {
            return;
        }

        ctx.Map.Add(KnownObject.Breaker, breaker.Position.X, breaker.Position.Y);
        ctx.Taken.Add(new Cell(breaker.Wall.A - 1, breaker.Wall.B));
        script.DoorsOpen = plan.Gate != GateKind.LockedDoors;
        if (plan is { Gate: GateKind.Blackout, BlackoutLink: not null })
        {
            script.Blackout = PlaceBlackout(ctx, GetDoorwayCell(ctx, plan.BlackoutLink), plan.TriggerRoom);
        }
    }

    // Stands the blackout trigger in the trigger room, its reach spanning the room across and stopping the given gap short of the doorway
    private static TriggerArea PlaceBlackout(PlacementContext ctx, Cell doorway, PlanRegion? room)
    {
        var door = GetCenter(ctx, doorway);
        var cells = room is null ? [] : ctx.GetRegionCells(room);
        if (cells.Count > 0)
        {
            bool alongU = cells.Max(cell => cell.U) - cells.Min(cell => cell.U) >= cells.Max(cell => cell.V) - cells.Min(cell => cell.V);
            int Along(Cell cell) => alongU ? cell.U : cell.V;
            int low = cells.Min(Along), high = cells.Max(Along);
            int step = Math.Abs(Along(doorway) - low) <= Math.Abs(Along(doorway) - high) ? 1 : -1;
            for (int at = step > 0 ? low : high; at >= low && at <= high; at += step)
            {
                var row = cells
                    .Where(cell => Along(cell) == at)
                    .Select(cell => GetCenter(ctx, cell))
                    .ToArray();

                var center = new PointF(row.Average(point => point.X), row.Average(point => point.Y));
                int radius = (int)MathF.Ceiling(row.Max(point => GetDistance(center, point)) + ctx.Step / 2);
                if (GetDistance(center, door) - radius >= BlackoutDoorGap)
                {
                    return new TriggerArea(Point.Truncate(center), radius);
                }
            }
        }

        return new TriggerArea(Point.Truncate(door), BlackoutDoorGap);
    }

    private static Cell GetDoorwayCell(PlacementContext ctx, PlanLink link) => ctx.Layout.GetDoorwayCell(link.Id, link.To);

    // Put bosses in the largest dead-end room outside a wing well along the walk, or else the last station
    private static void PlaceBosses(PlacementContext ctx, FacingTable facings, IReadOnlyDictionary<EdgeKey, EdgeKind> builtEdges)
    {
        var final = ctx.Plan.Regions
            .Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom && r.Id != ctx.Plan.KeyRoom?.Id && ctx.Plan.HoldsFights(r) && (ctx.Plan.Settings.WallSet != WallSet.Basement || IsNearBody(ctx, r)))
            .Where(r => ctx.GetProgress(r) >= LatestBossStart && ctx.Plan.Links.Count(l => l.From == r.Id || l.To == r.Id) == 1)
            .Where(r => ctx.Plan.Links.Any(l => l.To == r.Id))
            .OrderByDescending(r => ctx.GetRegionCells(r).Count)
            .FirstOrDefault() ?? ctx.Plan.Stations[^1];
        
        var entry = ctx.Plan.Links.FirstOrDefault(l => l.To == final.Id) ?? ctx.Plan.Links.First(l => l.From == final.Id);
        var entrance = ctx.Layout.GetDoorwayCell(entry.Id, final.Id);
        
        var far = ctx.GetFreeCells(final, inner: true)
            .OrderByDescending(c => Math.Abs(c.U - entrance.U) + Math.Abs(c.V - entrance.V))
            .ToArray();
        
        foreach (var id in ctx.Brief.Bosses)
        {
            var boss = ctx.Place(far.Take(Math.Max(1, far.Length / 5)).ToArray(), id, dir: 0, payload: MapBuilder.MonsterPayload, army: 1);
            if (boss is null)
            {
                continue;
            }

            for (int thirdIndex = 0; thirdIndex < 6; thirdIndex++)
            {
                float angle = ctx.Rng.NextSingle() * 2 * MathF.PI;
                float distance = 60 + 120 * ctx.Rng.NextSingle();
                var point = new PointF(boss.Value.Point.X + MathF.Cos(angle) * distance, boss.Value.Point.Y + MathF.Sin(angle) * distance * 0.7f);
                (float u, float v) = ctx.Layout.Lattice.ToLattice(point);
                if (!ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(point)) || ctx.IsInWall(point) || !(ctx.Layout.Edges.MeasureClearance(u, v) >= GoreClearance))
                    continue;
                
                var pool = GorePools[ctx.Rng.Next(GorePools.Length)];
                ctx.Map.Add(pool, point.X, point.Y, 0, facings.PickFacing(ctx.Rng, pool, ctx.Layout.Edges, u, v), unique: false);
                if (thirdIndex >= 2)
                    continue;
                    
                var corpse = KnownObject.SoldierCorpse + thirdIndex;
                byte facing = FurnitureGrammar.Default.Poses[corpse].DrawLying(ctx.Rng);
                if (facings.Fits(corpse, facing, builtEdges, u, v))
                {
                    ctx.Map.Add(corpse, point.X, point.Y, 0, facing, unique: false);
                }
            }
        }
    }

    // Lays each phase's charges beside the last key of the phase before, else in the start room on the fitted share of levels,
    // else on the walk the fitted lead before the phase's first site
    private static void PlaceCharges(PlacementContext ctx, (PointF Point, Cell Cell)[] sites, EconomyModel economy)
    {
        var keys = ctx.Plan.GeneratorRooms.Append(ctx.Plan.KeyRoom).OfType<PlanRegion>().ToArray();
        bool startBatch = ctx.Rng.NextSingle() < economy.StartBatchShare.Value;
        foreach (var batch in sites.GroupBy(site => ctx.Layout.Cells.TryGetValue(site.Cell, out int region) ? ctx.Plan.Regions[region].Phase : 0))
        {
            var key = keys.Where(room => room.Phase == batch.Key - 1).MaxBy(ctx.GetProgress);
            var first = batch.MinBy(site => ctx.Distance.GetValueOrDefault(site.Cell));
            var start = ctx.GetFreeCells(ctx.Plan.Stations[0]);
            var options = key is not null || startBatch && batch.Key == 0 ? new[] { ctx.GetFreeCells(key ?? ctx.Plan.Stations[0]), GetWalkCells(), start } : [GetWalkCells(), start];
            var spot = options
                .Select(cells => cells.Where(c => ctx.FindSpot(c) is not null && sites.All(site => GetDistance(GetCenter(ctx, c), site.Point) >= ChargeSiteGap)).ToArray())
                .Select(free => free.Where(ctx.IsClearAround).Concat(free).Cast<Cell?>().FirstOrDefault())
                .FirstOrDefault(found => found is not null);

            if (spot is null)
            {
                continue;
            }

            int count = batch.Count();
            var origin = spot.Value;
            var room = ctx.GetFreeCells(ctx.Plan.Regions[ctx.Layout.Cells[origin]])
                .Where(c => ctx.FindSpot(c) is not null && sites.All(site => GetDistance(GetCenter(ctx, c), site.Point) >= ChargeSiteGap))
                .Prepend(origin)
                .Distinct()
                .ToArray();

            var cluster = room.OrderBy(c => (c.U - origin.U) * (c.U - origin.U) + (c.V - origin.V) * (c.V - origin.V)).Take(count).ToArray();
            foreach (var cell in cluster)
            {
                _ = ctx.Place([cell], KnownObject.Dynamite, dir: 0, take: false) ?? ctx.Place(room, KnownObject.Dynamite, dir: 0, take: false);
            }

            ctx.Taken.UnionWith(cluster);
            continue;

            // Keep to rooms wholly before the site on the walk, a room on a loop around it reached either way
            Cell[] GetWalkCells()
            {
                float target = ctx.Distance.GetValueOrDefault(first.Cell) - economy.ChargeLead.Value * ctx.RouteLength;
                int reach = ctx.Distance.GetValueOrDefault(first.Cell);
                var before = ctx.Plan.Regions
                    .Where(region => region.Phase <= batch.Key && ctx.GetRegionCells(region).All(other => ctx.Distance.GetValueOrDefault(other) < reach))
                    .Select(region => region.Id)
                    .Append(ctx.Layout.Cells[first.Cell])
                    .ToHashSet();

                return ctx.TraceWalk(first.Cell)
                    .SkipWhile(c => ctx.Distance[c] > target)
                    .Where(c => ctx.IsFree(c) && before.Contains(ctx.Layout.Cells[c]) && ctx.Plan.Regions[ctx.Layout.Cells[c]].Phase <= batch.Key)
                    .ToArray();
            }
        }
    }

    // Puts the checkpoint on the last clear cell of the walk to the biggest fight before the fight's room
    private static void PlaceCheckpoint(PlacementContext ctx, LevelScript script, IReadOnlyList<PlannedFight> fights)
    {
        var climax = fights.MaxBy(fight => fight.HitPoints);
        if (climax is null)
        {
            return;
        }

        var cells = ctx.GetRegionCells(ctx.Plan.Regions[climax.Region]).Where(ctx.Distance.ContainsKey).ToArray();
        if (cells.Length == 0)
        {
            return;
        }

        var spot = ctx.TraceWalk(cells.MinBy(c => ctx.Distance[c]))
            .Where(c => ctx.Layout.Cells.TryGetValue(c, out int region) && region != climax.Region && ctx.IsFree(c) && ctx.IsClearAround(c) && !ctx.IsInWall(GetCenter(ctx, c), 24))
            .Cast<Cell?>()
            .FirstOrDefault();

        if (spot is not null)
        {
            script.Checkpoint = GetCenter(ctx, spot.Value);
        }
    }
}
