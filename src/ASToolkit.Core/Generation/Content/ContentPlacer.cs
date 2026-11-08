using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Scripting;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Provides the placement of a level's gameplay content by its progress along the route.
/// </summary>
public static class ContentPlacer
{
    private static readonly PointF BreakerOffset = new(-8, 3);

    // Holds the 415 spot the level 10 script handles specially; generators keep away from it
    private static readonly PointF ScriptedGeneratorSpot = new(7541, 4501);
    private const float ScriptedGeneratorReach = 150;
    private const float ChargeSiteGap = 150;
    private const int RayCells = 8;
    private const int BreakerFront = 3;
    private const int BreakerSide = 2;
    private const int BreakerOpenEnd = 3;
    private const float BreakerPropFloor = 5;
    private const int BreakerWallRun = 4;
    private const float BreakerGateReach = 400;
    private const float BreakerGateSlack = 100;
    private const float GeneratorFenceGap = 900;
    private const float GoreClearance = 1;
    private const float CannonWalk = 3600;
    private const float CannonClearance = 180;
    private const float CannonLatest = 0.75f;
    private const float CannonNearEnd = 0.3f;
    private const float CannonEnd = 0.6f;
    private const float FarEnd = 0.85f;
    private const float KillZoneDistance = 1000;
    private const float KillZoneReach = 450;
    private const float HallSiteShare = 0.35f;
    private const int MaxHallSites = 4;
    private const float PadWallReach = 1.5f;
    private const float SiteSpacing = 250;
    private const float FarWallGap = 1.0f;
    private const float NearWallGap = 2.2f;
    private const float WallGapSlack = 0.2f;
    private const float OpeningReach = 2.5f;
    private const float MinApproach = -0.2f;
    private const float MinCannonApproach = 0.5f;
    private const float RoomReach = 0.9f;
    private const float CornerWeight = 0.5f;
    private const float FarEndWeight = 1.5f;
    // Holds how many pixels inside the trigger room's doorway the blackout's reach stops
    private const int BlackoutDoorGap = 94;
    private const float PadSpacing = 520;
    private const float PadGap = 80;
    private const float PadWallDistance = 0.85f;
    private const float PadWallMargin = 20;
    private const float LatestBossStart = 0.65f;
    private static readonly KnownObject[] GorePools = [KnownObject.SmallBloodPool, KnownObject.BloodPool];

    private const float DoorMarkWallReach = 1;
    private static readonly int[] SiteRankCap = [0, 0, 3, 6, 6, 8, 9, 10, 11, 14, 14];

    /// <summary>
    /// Chooses the far wall of the key room the breaker hangs on, judged on the walls as built, nearest the locked door it opens first,
    /// with the original breaker room props that fit best.
    /// </summary>
    public static BreakerSpot? ChooseBreakerSpot(Random rng, LayoutPlan plan, FineLayout layout, IReadOnlyDictionary<EdgeKey, EdgeKind> built,
        IReadOnlyList<IReadOnlyList<PropPlacement>> scenes, ObjectTable objects, WallClearanceTable clearance, FacingTable facings)
    {
        var room = plan.KeyRoom;
        if (room is null)
        {
            return null;
        }

        var cells = layout.GetRegionCells(room.Id).ToHashSet();
        (EdgeKey Wall, int Front, int Side)[] backWalls = built
            .Where(p => p is { Value: EdgeKind.Wall, Key.Axis: 'u' } && cells.Contains(new Cell(p.Key.A - 1, p.Key.B)) && !cells.Contains(new Cell(p.Key.A, p.Key.B)))
            .Select(p => p.Key).OrderBy(k => k.A).ThenBy(k => k.B)
            .Select(k => (k, CountOpenCells(layout, new Cell(k.A - 1, k.B), -1, 0), CountOpenCells(layout, new Cell(k.A - 1, k.B), 0, 1)))
            .ToArray();
        backWalls = backWalls.Where(w => WallRun.Find(built, w.Wall).IsEnclosed(BreakerOpenEnd)).ToArray();
        // Stand the breaker on an outer wall with no room behind it
        var outer = backWalls.Where(w => !layout.Cells.ContainsKey(new Cell(w.Wall.A, w.Wall.B))).ToArray();
        if (outer.Length > 0)
        {
            backWalls = outer;
        }

        var longWalls = backWalls.Where(w => WallRun.Find(built, w.Wall).Length >= BreakerWallRun).ToArray();
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

        var gates = plan.Links.Where(link => plan.Regions[link.From].IsGated != plan.Regions[link.To].IsGated).ToArray();
        if (plan is { Gate: GateKind.LockedDoors } && gates.Length > 0)
        {
            var doors = gates.SelectMany(link => layout.LinkEdges[link.Id]).Select(edge => StructureBuilder.GetBasementPosition(layout.Lattice, edge).Item1).ToArray();
            float Reach(EdgeKey wall) => StructureBuilder.GetBasementPosition(layout.Lattice, wall).Item1 is var (x, y) ? doors.Min(door => GetDistance(new PointF(x, y), new PointF(door.X, door.Y))) : 0;
            float nearest = walls.Min(Reach);
            walls = walls.Where(wall => Reach(wall) <= Math.Max(BreakerGateReach, nearest + BreakerGateSlack)).ToArray();
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

        (int Transformers, int Count, float Roll, EdgeKey Wall, List<PropPlacement> Fit)? best = null;
        foreach (var wall in walls)
        {
            ((float ax, float ay), _) = StructureBuilder.GetBasementPosition(layout.Lattice, wall);
            (float bx, float by) = (ax + BreakerOffset.X, ay + BreakerOffset.Y);
            var front = new HashSet<Cell>(keep) { new(wall.A - 1, wall.B) };
            foreach (var scene in scenes)
            {
                var fit = new List<PropPlacement>();
                foreach (var placement in scene)
                {
                    float x = bx + placement.Dx;
                    float y = by + placement.Dy;
                    var footprint = Dresser.GetFootprint(layout.Lattice, objects, placement.Id, x, y);
                    bool solid = footprint.Count > 0;
                    if (!solid)
                    {
                        footprint.Add(layout.Lattice.GetCell(x, y));
                    }

                    if (footprint.All(cells.Contains) && !(solid && footprint.Any(front.Contains)) && IsWallFit(layout.Lattice, built, clearance, facings, placement, x, y))
                    {
                        fit.Add(placement);
                    }
                }

                var score = (fit.Count(p => p.Id == KnownObject.Transformer), fit.Count, rng.NextSingle(), wall, fit);
                if (best is null || (score.Item1, score.Item2, score.Item3).CompareTo((best.Value.Transformers, best.Value.Count, best.Value.Roll)) > 0)
                {
                    best = score;
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        ((float wx, float wy), _) = StructureBuilder.GetBasementPosition(layout.Lattice, best.Value.Wall);
        return new BreakerSpot(best.Value.Wall, new PointF(wx + BreakerOffset.X, wy + BreakerOffset.Y), best.Value.Fit);
    }

    /// <summary>
    /// Copies the original's wall marks onto doors with the same ID and facing, as often as the original marks its doors.
    /// </summary>
    public static void PlaceDoorMarks(Random rng, MapBuilder map, Lattice lattice, IReadOnlyDictionary<EdgeKey, EdgeKind> edges,
        IReadOnlyDictionary<(KnownObject Id, byte Dir), List<IReadOnlyList<PropPlacement>>> marks, float share)
    {
        foreach (var door in map.Sprites.Where(s => marks.ContainsKey((s.Id, s.Facing))).ToArray())
        {
            if (rng.NextSingle() >= share)
            {
                continue;
            }

            var options = marks[(door.Id, door.Facing)];
            foreach (var mark in options[rng.Next(options.Count)])
            {
                (float u, float v) = lattice.ToLattice(door.Position.X + mark.Dx, door.Position.Y + mark.Dy);
                if (edges.MeasureClearance(u, v) < DoorMarkWallReach)
                {
                    map.Add(mark.Id, door.Position.X + mark.Dx, door.Position.Y + mark.Dy, mark.Z, mark.Dir, unique: false, source: SpriteSource.CopiedScene);
                }
            }
        }
    }

    /// <summary>
    /// Puts a broken keypad next to jammed doors, as often as the originals do.
    /// </summary>
    public static void PlaceBrokenKeypads(Random rng, MapBuilder map, KeypadTable keypads, Lattice lattice, FacingTable facings, IReadOnlyDictionary<EdgeKey, EdgeKind> edges)
    {
        foreach (var door in map.Sprites.Where(s => s.Id is KnownObject.DoubleDoorFrame or KnownObject.SmallDoorFrame).ToArray())
        {
            if (rng.NextSingle() >= keypads.GetShare(door.Id))
            {
                continue;
            }

            var keypad = keypads.Pick(rng, door.Id, door.Facing, broken: true);
            if (keypad is not null)
            {
                float x = door.Position.X + keypad.Dx;
                float y = door.Position.Y + keypad.Dy;
                (float u, float v) = lattice.ToLattice(x, y);
                if (!facings.HasFacing(KnownObject.BrokenKeypad, edges, u, v))
                {
                    continue;
                }

                map.Add(KnownObject.BrokenKeypad, x, y, keypad.Z, facings.PickFacing(rng, KnownObject.BrokenKeypad, edges, u, v), source: SpriteSource.Relation);
            }
        }
    }

    /// <summary>
    /// Places the gate keys, bosses, mounted cannon, dynamite, monsters, ambushes, stashes, pickups and the checkpoint.
    /// </summary>
    public static void Place(PlacementContext ctx, LevelBudget budget, bool hasCheckpoint, bool blackoutIsCheckpoint, LevelScript script, BreakerSpot? breaker,
        IReadOnlyList<SiteTemplate> siteTemplates, IReadOnlyDictionary<EdgeKey, EdgeKind> visibleEdges, IReadOnlyDictionary<EdgeKey, EdgeKind> builtEdges, IReadOnlyDictionary<EdgeKey, int> spawnWalls, KeypadTable keypads, FacingTable facings,
        IReadOnlyList<IReadOnlyList<PropPlacement>> cannonScenes, StyleModel model, IReadOnlyList<PlannedFight> fights, PlacedSites? placed)
    {
        PlaceGate(ctx, model.Encounters.GeneratorsPerRoom.Value, script, breaker, keypads, facings);
        PlaceBosses(ctx, facings, builtEdges);
        var cannon = placed is null ? PlaceMountedCannons(ctx, budget, cannonScenes) : placed.Cannon;
        var sites = placed?.Sites ?? PlaceSites(ctx, budget, siteTemplates, visibleEdges, cannon, model.Economy);
        PlaceCharges(ctx, sites, model.Economy);
        new EncounterPlacer(ctx, model.Encounters, spawnWalls).Place(fights);
        new PickupPlacer(ctx, model.Economy).Place(budget);
        if (hasCheckpoint && (script.Blackout is null || !blackoutIsCheckpoint))
        {
            PlaceCheckpoint(ctx, script, fights);
        }
    }

    /// <summary>
    /// Places the mounted cannon and the dynamite sites of a concrete level before its decor and returns them.
    /// </summary>
    public static PlacedSites PlaceConcreteSites(PlacementContext ctx, LevelBudget budget, IReadOnlyList<SiteTemplate> siteTemplates, IReadOnlyList<IReadOnlyList<PropPlacement>> cannonScenes,
        EconomyModel economy)
    {
        var cannon = PlaceMountedCannons(ctx, budget, cannonScenes);
        return new PlacedSites(cannon, PlaceSites(ctx, budget, siteTemplates, new Dictionary<EdgeKey, EdgeKind>(), cannon, economy));
    }

    private static bool IsWallFit(Lattice lattice, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, WallClearanceTable clearance, FacingTable facings, PropPlacement p, float x, float y)
    {
        if (p.Z >= BreakerPropFloor)
        {
            return true;
        }

        (float u, float v) = lattice.ToLattice(x, y);
        return facings.Fits(p.Id, p.Dir, edges, u, v)
               && (!clearance.TryGetClearance(p.Id, out float minimum, out _) || edges.MeasureClearance(u, v) >= minimum);
    }

    private static int CountOpenCells(FineLayout layout, Cell start, int du, int dv) =>
        FloorScanner.CountOpenCells(layout.Cells.ContainsKey, layout.Edges.ContainsKey, start, du, dv);

    private static PlanRegion[] GetObjectiveRooms(PlacementContext ctx) =>
        ctx.Plan.Regions.Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Pen).ToArray();

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

    private static void PlaceGate(PlacementContext ctx, float generatorsPerRoom, LevelScript script, BreakerSpot? breaker, KeypadTable keypads, FacingTable facings)
    {
        var plan = ctx.Plan;
        if (plan.Gate is GateKind.LockedDoors or GateKind.Blackout && breaker is not null)
        {
            ctx.Map.Add(KnownObject.Breaker, breaker.Position.X, breaker.Position.Y);
            ctx.Taken.Add(new Cell(breaker.Wall.A - 1, breaker.Wall.B));
            foreach (var (edge, kind) in ctx.Layout.Edges.Where(p => p.Value is EdgeKind.Door or EdgeKind.DoubleDoor).OrderBy(p => p.Key.Axis).ThenBy(p => p.Key.A).ThenBy(p => p.Key.B))
            {
                ((float x, float y), bool slash) = StructureBuilder.GetBasementPosition(ctx.Layout.Lattice, edge);
                var door = kind == EdgeKind.Door ? KnownObject.SmallDoor : KnownObject.DoubleDoor;
                byte doorDir = (byte)(slash ? 0 : kind == EdgeKind.Door ? 128 : 64);
                var keypad = keypads.Pick(ctx.Rng, door, doorDir, broken: false) ?? new PropPlacement(0, 0, 0, 5, doorDir);
                ctx.Map.Add(plan.Gate == GateKind.LockedDoors ? KnownObject.KeypadOff : KnownObject.KeypadOn, x + keypad.Dx, y + keypad.Dy, keypad.Z, keypad.Dir);
            }

            script.DoorsOpen = plan.Gate != GateKind.LockedDoors;
            if (plan is { Gate: GateKind.Blackout, BlackoutLink: not null })
            {
                script.Blackout = PlaceBlackout(ctx, GetDoorwayCell(ctx, plan.BlackoutLink), plan.TriggerRoom);
            }
        }

        if (plan is { Gate: GateKind.Fences, KeyRoom: not null })
        {
            var fences = plan.Links.Where(link => link.Kind == LinkKind.Fence).Select(link => ctx.MeasureDistances(GetDoorwayCell(ctx, link))).ToArray();
            bool IsClearOfFences(Cell c) => fences.All(walk => walk.GetValueOrDefault(c, int.MaxValue) * ctx.Step >= GeneratorFenceGap);
            bool IsNearScriptedSpot(Cell c) => ctx.Brief.Level == 10 && GetDistance(GetCenter(ctx, c), ScriptedGeneratorSpot) < ScriptedGeneratorReach + ctx.Step;
            var rooms = plan.GeneratorRooms.Count > 0 ? plan.GeneratorRooms.Select(entry => entry.Room).ToArray() : [plan.KeyRoom];
            int count = Math.Max(rooms.Length, (int)MathF.Round(rooms.Length * generatorsPerRoom));
            int keyRoomCells = 0;
            for (int roomIndex = 0; roomIndex < rooms.Length; roomIndex++)
            {
                var room = rooms[roomIndex];
                var entrance = GetDoorwayCell(ctx, plan.Links.First(l => l.To == room.Id));
                var cells = ctx.GetFreeCells(room, inner: true)
                    .OrderByDescending(c => Math.Abs(c.U - entrance.U) + Math.Abs(c.V - entrance.V))
                    .ToList();

                if (cells.Count == 0)
                {
                    cells = ctx.GetFreeCells(room).ToList();
                }

                cells.RemoveAll(IsNearScriptedSpot);
                var anywhere = cells.ToList();
                var clear = cells.Where(IsClearOfFences).ToList();
                if (clear.Count > 0)
                {
                    cells = clear;
                }

                int here = count / rooms.Length + (roomIndex < count % rooms.Length ? 1 : 0);
                for (int index = 0; index < here && cells.Count > 0; index++)
                {
                    var placed = ctx.Place(cells.Take(Math.Max(1, cells.Count / 3)).ToArray(), KnownObject.Generator, dir: 0)
                        ?? ctx.Place(cells.ToArray(), KnownObject.Generator, dir: 0)
                        ?? ctx.Place(anywhere.Where(cell => !cells.Contains(cell)).ToArray(), KnownObject.Generator, dir: 0);
                    if (placed is null)
                    {
                        break;
                    }

                    ctx.Map.Add(KnownObject.ShootHere, placed.Value.X, placed.Value.Y, 0, 0, unique: false);
                    cells.Remove(placed.Value.Cell);
                }

                if (roomIndex == 0)
                {
                    keyRoomCells = cells.Count;
                }
            }

            var keyEntrance = GetDoorwayCell(ctx, plan.Links.First(l => l.To == plan.KeyRoom.Id));

            var walls = ctx.GetFreeCells(plan.KeyRoom)
                .Where(c => facings.IsByWall(KnownObject.Transformer, ctx.Layout.Edges, c.U + 0.5f, c.V + 0.5f) && facings.HasFacing(KnownObject.Transformer, ctx.Layout.Edges, c.U + 0.5f, c.V + 0.5f))
                .OrderByDescending(c => Math.Abs(c.U - keyEntrance.U) + Math.Abs(c.V - keyEntrance.V))
                .ToArray();

            foreach (var cell in walls.Take(Math.Min(2, keyRoomCells / 3)))
            {
                ctx.Place([cell], KnownObject.Transformer, dir: facings.PickFacing(ctx.Rng, KnownObject.Transformer, ctx.Layout.Edges, cell.U + 0.5f, cell.V + 0.5f));
            }
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
                    return new TriggerArea(new Point((int)center.X, (int)center.Y), radius);
                }
            }
        }

        return new TriggerArea(new Point((int)door.X, (int)door.Y), BlackoutDoorGap);
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
                (float x, float y) = (boss.Value.X + MathF.Cos(angle) * distance, boss.Value.Y + MathF.Sin(angle) * distance * 0.7f);
                (float u, float v) = ctx.Layout.Lattice.ToLattice(x, y);
                if (!ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(x, y)) || ctx.IsInWall(x, y) || !(ctx.Layout.Edges.MeasureClearance(u, v) >= GoreClearance))
                    continue;
                
                var pool = GorePools[ctx.Rng.Next(GorePools.Length)];
                ctx.Map.Add(pool, x, y, 0, facings.PickFacing(ctx.Rng, pool, ctx.Layout.Edges, u, v), unique: false);
                if (thirdIndex >= 2)
                    continue;
                    
                var corpse = KnownObject.SoldierCorpse + thirdIndex;
                byte facing = facings.PickFacing(ctx.Rng, corpse, builtEdges, u, v);
                if (facings.Fits(corpse, facing, builtEdges, u, v))
                {
                    ctx.Map.Add(corpse, x, y, 0, facing, unique: false);
                }
            }
        }
    }

    // Stand the cannon at the near end of the largest hall, with its kill zone at the far end
    private static (PlanRegion Hall, PointF Far)? PlaceMountedCannons(PlacementContext ctx, LevelBudget budget, IReadOnlyList<IReadOnlyList<PropPlacement>> scenes)
    {
        if (budget.MountedCannons == 0)
        {
            return null;
        }

        var halls = ctx.Plan.Stations
            .Where(s => ctx.GetProgress(s) is >= 0.15f and <= 0.8f)
            .OrderByDescending(s => ctx.GetRegionCells(s).Any(cell => ctx.Distance.GetValueOrDefault(cell) * ctx.Step >= CannonWalk))
            .ThenByDescending(s => ctx.GetRegionCells(s).Count)
            .ToArray();

        foreach (bool strict in new[] { true, false })
        {
            float clear = strict ? CannonClearance : CannonClearance / 2;
            foreach (var hall in halls)
            {
                var axis = HallAxis.Measure(ctx, hall);
                var cells = ctx.GetFreeCells(hall, inner: true)
                    .Where(cell => ctx.Distance.GetValueOrDefault(cell) * ctx.Step >= CannonWalk && ctx.GetProgress(cell) <= CannonLatest
                        && (!strict || axis.GetEntryShare(cell) >= CannonNearEnd))
                    .ToArray();
                ctx.Rng.Shuffle(cells);
                var far = axis.GetFarPoint(FarEnd);
                var ordered = cells.OrderBy(cell => MathF.Abs(axis.GetEntryShare(cell) - CannonEnd)).ToArray();
                foreach (var cell in ordered)
                {
                    (float x, float y) = GetCenter(ctx, cell);
                    if (strict && GetDistance(new PointF(x, y), far) < KillZoneDistance)
                    {
                        continue;
                    }

                    var ring = new List<(PointF Point, Cell Cell)>();
                    foreach (float dx in new[] { -clear, -clear / 2, 0, clear / 2, clear })
                    {
                        foreach (float dy in new[] { -clear * 0.7f, -clear * 0.35f, 0, clear * 0.35f, clear * 0.7f })
                        {
                            ring.Add((new PointF(x + dx, y + dy), ctx.Layout.Lattice.GetCell(x + dx, y + dy)));
                        }
                    }

                    if (!ring.All(r => ctx.Layout.Cells.ContainsKey(r.Cell) && ctx.IsFree(r.Cell) && !ctx.IsInWall(r.Point.X, r.Point.Y, 8)))
                    {
                        continue;
                    }

                    ctx.Map.Add(KnownObject.MountedCannon, x, y, 0, ctx.Rng.Next(2) == 0 ? 0 : 128, unique: false);
                    ctx.Occupancy.Add(KnownObject.MountedCannon, x, y, 0);
                    ctx.Taken.UnionWith(ring.Select(r => r.Cell));
                    if (scenes.Count > 0)
                    {
                        foreach (var stripe in scenes[ctx.Rng.Next(scenes.Count)])
                        {
                            if (ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(x + stripe.Dx, y + stripe.Dy)) && !ctx.IsInWall(x + stripe.Dx, y + stripe.Dy, 0)
                                && ctx.Occupancy.Fits(stripe.Id, x + stripe.Dx, y + stripe.Dy, stripe.Z))
                            {
                                ctx.Map.Add(stripe.Id, x + stripe.Dx, y + stripe.Dy, stripe.Z, stripe.Dir, unique: false, source: SpriteSource.CopiedScene);
                                ctx.Occupancy.Add(stripe.Id, x + stripe.Dx, y + stripe.Dy, stripe.Z);
                            }
                        }
                    }

                    return (hall, far);
                }
            }
        }

        return null;
    }

    private static (float X, float Y, Cell Cell)[] PlaceSites(PlacementContext ctx, LevelBudget budget, IReadOnlyList<SiteTemplate> templates, IReadOnlyDictionary<EdgeKey, EdgeKind> visibleEdges,
        (PlanRegion Hall, PointF Far)? cannon, EconomyModel economy)
    {
        ctx.EndlessGeneratorsLeft = budget.EndlessGenerators;
        var sites = new List<(float X, float Y, Cell Cell)>();
        if (budget.DynamiteSites == 0 || templates.Count == 0)
        {
            return [];
        }

        var allowed = templates.Where(t => t.Rank <= SiteRankCap[ctx.Brief.Level]).ToArray();
        if (allowed.Length == 0)
        {
            allowed = templates.ToArray();
        }

        var own = allowed.Where(t => t.Level == ctx.Brief.DonorLevel).ToArray();
        if (own.Length > 0)
        {
            allowed = own;
        }

        int n = budget.DynamiteSites;
        bool basement = ctx.Plan.Settings.WallSet == WallSet.Basement;
        var rooms = GetObjectiveRooms(ctx)
            .Where(room => room != ctx.Plan.KeyRoom && room != ctx.Plan.TriggerRoom && ctx.Plan.HoldsFights(room))
            .ToArray();

        // Stand about a third of the sites, one at least, in the locked rooms when the building names any, spread among the others
        var lockedRooms = rooms.Where(ctx.Plan.LockedSiteRooms.Contains).ToArray();
        int lockedSites = lockedRooms.Length == 0 ? 0 : Math.Max(1, (int)MathF.Round(n / 3f));
        var inLocked = Enumerable.Range(0, lockedSites).Select(index => (index + 1) * n / (lockedSites + 1)).ToHashSet();
        float[] targets = Enumerable.Range(0, n).Select(_ => economy.SitePositions.Value[ctx.Rng.Next(economy.SitePositions.Value.Count)]).Order().ToArray();

        // Put some of the sites in the cannon's kill zone, in place of the ones due nearest to it
        var inHall = new HashSet<int>();
        if (cannon is not null && !basement)
        {
            float farProgress = ctx.GetProgress(ctx.Layout.Lattice.GetCell(cannon.Value.Far.X, cannon.Value.Far.Y));
            int count = Math.Clamp((int)MathF.Round(n * HallSiteShare), 1, MaxHallSites);
            inHall = Enumerable.Range(0, n).OrderBy(siteIndex => MathF.Abs(targets[siteIndex] - farProgress)).Take(count).ToHashSet();
        }

        // Give every room one site template
        var roomTemplates = new Dictionary<int, SiteTemplate>();
        for (int siteIndex = 0; siteIndex < n; siteIndex++)
        {
            var candidates = OrderByProgress(ctx, inLocked.Contains(siteIndex) ? lockedRooms : rooms, targets[siteIndex]);
            if (candidates.Length == 0)
            {
                break;
            }

            var region = candidates[0];
            (float X, float Y, Cell Cell)? site;
            bool hallSite = inHall.Contains(siteIndex);
            var home = hallSite ? cannon!.Value.Hall : region;
            if (!basement)
            {
                // Try the room's walls, then its cells with pads against a far wall, then the walls of the next rooms of its role
                site = PlaceRoomWallSite(home) ?? PlaceCellSite(home, openAllowed: false);
                if (site is null && !hallSite)
                {
                    site = candidates.Skip(1).Select(PlaceRoomWallSite).FirstOrDefault(placed => placed is not null);
                }
            }
            else
            {
                site = candidates.Select(room => PlaceBasementSite(ctx, room, allowed, visibleEdges)).FirstOrDefault(placed => placed is not null);
            }

            site ??= PlaceCellSite(home, openAllowed: true);
            if (site is null)
            {
                continue;
            }

            sites.Add(site.Value);

            (float X, float Y, Cell Cell)? PlaceRoomWallSite(PlanRegion room)
            {
                if (!roomTemplates.TryGetValue(room.Id, out var template))
                {
                    template = allowed[ctx.Rng.Next(allowed.Length)];
                }

                var placed = PlaceWallSite(ctx, room, template, sites, hallSite ? cannon : null);
                if (placed is not null)
                {
                    roomTemplates[room.Id] = template;
                }

                return placed;
            }

            // Try every cell for pads against a far wall before standing them in the open when allowed, and fall back to a site without pads
            (float X, float Y, Cell Cell)? PlaceCellSite(PlanRegion room, bool openAllowed)
            {
                var pool = GetSiteCells(ctx, room, hallSite ? cannon!.Value.Far : null);
                var passes = openAllowed ? new (bool, bool?)[] { (true, true), (false, true), (true, false), (false, false), (false, null) } : [(true, true), (false, true)];
                foreach ((bool spaced, bool? againstWall) in passes)
                {
                    foreach (var cell in pool)
                    {
                        (float x, float y) = GetCenter(ctx, cell);
                        if (ctx.IsInWall(x, y, 20) || !ctx.Occupancy.HasRoomAround(x, y) || sites.Any(other => GetDistance(new PointF(other.X, other.Y), new PointF(x, y)) < SiteSpacing))
                        {
                            continue;
                        }

                        SiteTemplate? template;
                        if (againstWall is null)
                        {
                            var bare = allowed.Where(t => t.Members.All(member => member.Id != KnownObject.TeleportPad)).ToArray();
                            template = bare.Length > 0 ? bare[ctx.Rng.Next(bare.Length)] : null;
                        }
                        else
                        {
                            template = OrientPads(ctx, allowed[ctx.Rng.Next(allowed.Length)], x, y, againstWall.Value);
                        }

                        if (template is null || spaced && !hallSite && template.Members.Any(member => member.Id == KnownObject.TeleportPad) && IsNearPads(ctx, x, y))
                        {
                            continue;
                        }

                        StampSite(ctx, template, x, y, basement ? 0.3f : 1.0f);
                        ctx.Taken.Add(cell);
                        return (x, y, cell);
                    }
                }

                return null;
            }
        }

        return sites.OrderBy(s => ctx.Distance.GetValueOrDefault(s.Cell)).ToArray();
    }

    private static PlanRegion[] OrderByProgress(PlacementContext ctx, IEnumerable<PlanRegion> rooms, float progress) => rooms
        .OrderBy(room => Math.Abs(ctx.GetProgress(room) - progress))
        .ThenBy(room => room.Id)
        .ToArray();

    // Turn a far wall piece into a monster hole, with the 179 in front and the endless generators behind it
    private static (float X, float Y, Cell Cell)? PlaceBasementSite(PlacementContext ctx, PlanRegion region, SiteTemplate[] templates, IReadOnlyDictionary<EdgeKey, EdgeKind> visibleEdges)
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
            ((float x, float y), bool isSlash) = StructureBuilder.GetBasementPosition(ctx.Layout.Lattice, k);
            return ctx.Occupancy.HasRoomAround(x + 25 * (isSlash ? 1 : -1), y + 20);
        }).ToArray();
        
        if (walls.Length == 0)
        {
            return null;
        }

        var wall = walls[ctx.Rng.Next(walls.Length)];
        ((float hx, float hy), bool slash) = StructureBuilder.GetBasementPosition(ctx.Layout.Lattice, wall);
        int index = ctx.Map.Sprites.FindIndex(s => s.Id is KnownObject.BasementWallSlash or KnownObject.BasementWallBack && Math.Abs(s.Position.X - hx) < 1 && Math.Abs(s.Position.Y - hy) < 1);
        if (index < 0)
        {
            return null;
        }

        var hole = ctx.Brief.Level >= 3 || ctx.Rng.Next(2) == 0 ? KnownObject.BigMonsterHole : KnownObject.SmallMonsterHole;
        ctx.Map.Sprites[index] = ctx.Map.Sprites[index] with { Id = hole, Dir = (byte)(slash ? 0 : 128) };
        int side = slash ? 1 : -1;
        (float sx, float sy) = (hx + 25 * side, hy + 20);
        ctx.Map.Add(KnownObject.DynamiteSite, sx, sy, 0, 0, unique: false);
        var template = templates[ctx.Rng.Next(templates.Length)];
        
        var generators = template.Members
            .Where(m => m.Id.IsEndlessGenerator)
            .ToArray();
        
        if (generators.Length == 0)
        {
            generators = [new SiteMember(KnownObject.FirstEndlessGenerator, 0, 0, 0, 0, 1, new byte[8])];
        }

        foreach (var generator in generators.Take(Math.Max(0, ctx.EndlessGeneratorsLeft)))
        {
            ctx.EndlessGeneratorsLeft--;
            ctx.Map.Add(generator.Id, hx - 30 * side + ctx.Rng.NextSingle() * 16 - 8, hy - 42 + ctx.Rng.NextSingle() * 12 - 6, generator.Z, generator.Dir, generator.Payload, generator.Army, unique: false, source: SpriteSource.CopiedScene);
        }

        var floorCell = GetFloorSide(wall)!.Value;
        ctx.Taken.Add(floorCell);
        return (sx, sy, floorCell);

        Cell? GetFloorSide(EdgeKey k)
        {
            var (c1, c2) = k.Cells;
            var (floor, outside) = k.Axis == 'v' ? (c2, c1) : (c1, c2);
            return visibleEdges.GetValueOrDefault(k) == EdgeKind.Wall && cells.Contains(floor) && !ctx.Layout.Cells.ContainsKey(outside) ? floor : null;
        }
    }

    // Keep a padded site apart from other pads to keep each site's pads one mirrored group
    private static bool IsNearPads(PlacementContext ctx, float x, float y) => ctx.Map.Pads.Any(pad => GetDistance(pad, new PointF(x, y)) < PadSpacing);

    // Stand a site's pads a cell off a far wall or two off a near one, in a corner when the template points into one, with the pads
    // behind the site as seen from the room's entry or the cannon, preferring corners and the room's far end
    private static (float X, float Y, Cell Cell)? PlaceWallSite(PlacementContext ctx, PlanRegion room, SiteTemplate template, List<(float X, float Y, Cell Cell)> sites,
        (PlanRegion Hall, PointF Far)? cannon)
    {
        var lattice = ctx.Layout.Lattice;
        var edges = ctx.Layout.Edges;
        var cells = ctx.GetRegionCells(room).ToHashSet();
        var entry = cells.Where(ctx.Distance.ContainsKey).DefaultIfEmpty(cells.First()).MinBy(c => ctx.Distance.GetValueOrDefault(c));
        var from = cannon?.Hall == room ? FindCannon(ctx) ?? GetCenter(ctx, entry) : GetCenter(ctx, entry);
        var axis = HallAxis.Measure(ctx, room);
        (float X, float Y, Cell Cell, SiteTemplate Template)? best = null;
        float bestScore = float.MinValue;
        for (int turn = 0; turn < 4; turn++)
        {
            var turned = TurnTemplate(lattice, template, turn);
            var pads = turned.Members.Where(member => member.Id == KnownObject.TeleportPad).Select(member => lattice.ToLattice(member.Dx, member.Dy)).Select(p => (U: p.U - lattice.ToLattice(0, 0).U, V: p.V - lattice.ToLattice(0, 0).V)).ToArray();
            if (pads.Length == 0)
            {
                return null;
            }

            float meanU = pads.Average(p => p.U);
            float meanV = pads.Average(p => p.V);
            int signU = MathF.Abs(meanU) >= 0.5f ? Math.Sign(meanU) : 0;
            int signV = MathF.Abs(meanV) >= 0.5f ? Math.Sign(meanV) : 0;
            if (signU == 0 && signV == 0)
            {
                // Stand a flanking pair in line with its site against the wall across its spread
                bool spreadAlongV = pads.Max(p => p.V) - pads.Min(p => p.V) >= pads.Max(p => p.U) - pads.Min(p => p.U);
                foreach (int sign in new[] { 1, -1 })
                {
                    Evaluate(spreadAlongV ? sign : 0, spreadAlongV ? 0 : sign, true);
                }
            }
            else
            {
                Evaluate(signU, signV, false);
            }

            void Evaluate(int su, int sv, bool flanking)
            {
                foreach (var cell in cells.OrderBy(c => c.U).ThenBy(c => c.V))
                {
                    float? u = su == 0 ? null : GetAnchor('u', cell, su, flanking ? 0 : su > 0 ? pads.Max(p => p.U) : pads.Min(p => p.U));
                    float? v = sv == 0 ? null : GetAnchor('v', cell, sv, flanking ? 0 : sv > 0 ? pads.Max(p => p.V) : pads.Min(p => p.V));
                    if (su != 0 && u is null || sv != 0 && v is null)
                    {
                        continue;
                    }

                    float siteU = u ?? cell.U + 0.5f - meanU;
                    float siteV = v ?? cell.V + 0.5f - meanV;
                    (float x, float y) = lattice.ToWorld(siteU, siteV);
                    var siteCell = lattice.GetCell(x, y);
                    if (!cells.Contains(siteCell) || !ctx.IsFree(siteCell) || ctx.IsInWall(x, y, 20) || !ctx.Occupancy.HasRoomAround(x, y) || !HasRoom(siteU, siteV)
                        || sites.Any(other => GetDistance(new PointF(other.X, other.Y), new PointF(x, y)) < SiteSpacing)
                        || cannon is not null && GetDistance(new PointF(x, y), cannon.Value.Far) >= KillZoneReach
                        || !pads.All(pad => IsWallPad(siteU + pad.U, siteV + pad.V)))
                    {
                        continue;
                    }

                    float padX = pads.Average(pad => lattice.ToWorld(siteU + pad.U, siteV + pad.V).X) - x;
                    float padY = pads.Average(pad => lattice.ToWorld(siteU + pad.U, siteV + pad.V).Y) - y;
                    float toX = x - from.X, toY = y - from.Y;
                    float approach = (toX * padX + toY * padY) / (MathF.Sqrt(toX * toX + toY * toY) * MathF.Sqrt(padX * padX + padY * padY) + 1e-3f);
                    if (approach < (cannon is null ? MinApproach : MinCannonApproach))
                    {
                        continue;
                    }

                    float score = CornerWeight * (su != 0 && sv != 0 ? 1 : 0) + approach + FarEndWeight * (1 - axis.GetEntryShare(siteCell)) + 0.2f * ctx.Rng.NextSingle();
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = (x, y, siteCell, turned);
                    }
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        StampSite(ctx, best.Value.Template, best.Value.X, best.Value.Y, 1.0f);
        ctx.Taken.Add(best.Value.Cell);
        return (best.Value.X, best.Value.Y, best.Value.Cell);

        // Keep the room's floor all around a point, clear of wall ends
        bool HasRoom(float u, float v)
        {
            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * MathF.PI / 4;
                (float x, float y) = lattice.ToWorld(u + RoomReach * MathF.Cos(angle), v + RoomReach * MathF.Sin(angle));
                if (!cells.Contains(lattice.GetCell(x, y)) || ctx.IsInWall(x, y, 4))
                {
                    return false;
                }
            }

            return true;
        }

        // Returns the site coordinate that puts the outermost pad its gap off the wall on the given side of a cell
        float? GetAnchor(char edgeAxis, Cell cell, int sign, float outer)
        {
            var edge = edgeAxis == 'u'
                ? new EdgeKey('u', sign > 0 ? cell.U + 1 : cell.U, cell.V)
                : new EdgeKey('v', cell.U, sign > 0 ? cell.V + 1 : cell.V);
            if (edges.GetValueOrDefault(edge) != EdgeKind.Wall)
            {
                return null;
            }

            float wall = edgeAxis == 'u' ? edge.A : edge.B;
            return wall - sign * GetGap(edgeAxis, sign) - outer;
        }

        // Keep a pad its gap off every wall and clear of doorways
        bool IsWallPad(float u, float v)
        {
            (float x, float y) = lattice.ToWorld(u, v);
            if (!cells.Contains(lattice.GetCell(x, y)) || !IsPadSpot(ctx, x, y) || !HasRoom(u, v))
            {
                return false;
            }

            foreach ((char edgeAxis, int sign) in new[] { ('u', 1), ('u', -1), ('v', 1), ('v', -1) })
            {
                (float distance, var kind) = CastRay(ctx, edges, u, v, edgeAxis, sign);
                if (kind != EdgeKind.Wall && distance < OpeningReach || distance < GetGap(edgeAxis, sign) - WallGapSlack)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // Treat the +u and -v sides as far walls
    private static float GetGap(char edgeAxis, int sign) => edgeAxis == 'u' == (sign > 0) ? FarWallGap : NearWallGap;

    // Count a fake door as an opening
    private static (float Distance, EdgeKind Kind) CastRay(PlacementContext ctx, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v, char edgeAxis, int sign)
    {
        var side = edgeAxis == 'u' ? sign > 0 ? WallSides.UpperBack : WallSides.LowerBack : sign > 0 ? WallSides.LowerSlash : WallSides.UpperSlash;
        var hit = edges.CastRay(u, v, side, ctx.Layout.Cells.ContainsKey, RayCells);
        if (hit is null)
        {
            return (float.MaxValue, EdgeKind.Wall);
        }

        (float distance, var edge, var kind) = hit.Value;
        var fakeDoors = ctx.Layout.FakeDoors;
        bool door = fakeDoors.ContainsKey(edge) || fakeDoors.ContainsKey(StructureBuilder.GetPreviousEdge(edge)) || fakeDoors.ContainsKey(edgeAxis == 'u' ? new EdgeKey('u', edge.A, edge.B + 1) : new EdgeKey('v', edge.A + 1, edge.B));
        return (distance, door ? EdgeKind.Door : kind == EdgeKind.None ? EdgeKind.Wall : kind);
    }

    private static PointF? FindCannon(PlacementContext ctx)
    {
        int index = ctx.Map.Sprites.FindIndex(s => s.Id == KnownObject.MountedCannon);
        return index < 0 ? null : new PointF(ctx.Map.Sprites[index].Position.X, ctx.Map.Sprites[index].Position.Y);
    }

    private static SiteTemplate TurnTemplate(Lattice lattice, SiteTemplate template, int quarters)
    {
        (float u0, float v0) = lattice.ToLattice(0, 0);
        return template with
        {
            Members = template.Members.Select(member =>
            {
                (float u, float v) = lattice.ToLattice(member.Dx, member.Dy);
                (float du, float dv) = (u - u0, v - v0);
                for (int quarter = 0; quarter < quarters; quarter++)
                {
                    (du, dv) = (-dv, du);
                }

                (float x, float y) = lattice.ToWorld(u0 + du, v0 + dv);
                return member with { Dx = x, Dy = y };
            }).ToArray()
        };
    }

    // Rank a room's cells from its far end, or from the kill zone, with some jitter
    private static Cell[] GetSiteCells(PlacementContext ctx, PlanRegion region, PointF? killZone)
    {
        var pool = ctx.GetFreeCells(region);
        if (killZone is not null)
        {
            return pool
                .Select(cell => (Cell: cell, Distance: GetDistance(GetCenter(ctx, cell), killZone.Value)))
                .Where(p => p.Distance < KillZoneReach)
                .OrderBy(p => p.Distance + ctx.Rng.NextSingle() * ctx.Step * 2)
                .Select(p => p.Cell)
                .ToArray();
        }

        var axis = HallAxis.Measure(ctx, region);
        return pool
            .OrderBy(cell => axis.GetEntryShare(cell) + ctx.Rng.NextSingle() * 0.3f)
            .ToArray();
    }

    // Turn a template's pads a quarter at a time until they stand against far walls, or in the open when asked
    private static SiteTemplate? OrientPads(PlacementContext ctx, SiteTemplate template, float x, float y, bool againstWall)
    {
        if (template.Members.All(member => member.Id != KnownObject.TeleportPad))
        {
            return template;
        }

        var lattice = ctx.Layout.Lattice;
        (float u0, float v0) = lattice.ToLattice(x, y);
        int first = ctx.Rng.Next(4);
        for (int turn = 0; turn < 4; turn++)
        {
            var members = template.Members.Select(member => Turn(member, (first + turn) % 4)).ToArray();
            if (members.Where(member => member.Id == KnownObject.TeleportPad).All(pad => IsPadSpot(ctx, x + pad.Dx, y + pad.Dy) && (!againstWall || IsAgainstFarWall(x + pad.Dx, y + pad.Dy))))
            {
                return template with { Members = members };
            }
        }

        return null;

        SiteMember Turn(SiteMember member, int quarters)
        {
            (float u, float v) = lattice.ToLattice(x + member.Dx, y + member.Dy);
            (float du, float dv) = (u - u0, v - v0);
            for (int quarter = 0; quarter < quarters; quarter++)
            {
                (du, dv) = (-dv, du);
            }

            (float px, float py) = lattice.ToWorld(u0 + du, v0 + dv);
            return member with { Dx = px - x, Dy = py - y };
        }

        bool IsAgainstFarWall(float px, float py)
        {
            (float u, float v) = lattice.ToLattice(px, py);
            return ctx.Layout.Edges.FindNearestWall(u, v) is { Side: WallSides.UpperBack or WallSides.UpperSlash, Distance: <= PadWallReach };
        }
    }

    // Stand a pad in the open or off a far wall, in view and clear of other pads, as the originals stand theirs
    private static bool IsPadSpot(PlacementContext ctx, float x, float y)
    {
        (float u, float v) = ctx.Layout.Lattice.ToLattice(x, y);
        var wall = ctx.Layout.Edges.FindNearestWall(u, v);
        return ctx.Distance.ContainsKey(ctx.Layout.Lattice.GetCell(x, y)) && !ctx.IsInWall(x, y, PadWallMargin) && ctx.IsVisible(x, y)
            && (!wall.HasValue || wall.Value.Side is WallSides.UpperBack or WallSides.UpperSlash && wall.Value.Distance >= PadWallDistance)
            && !ctx.Map.Pads.Any(pad => GetDistance(pad, new PointF(x, y)) < PadGap);
    }

    private static void StampSite(PlacementContext ctx, SiteTemplate template, float x, float y, float shrink)
    {
        ctx.Map.Add(KnownObject.DynamiteSite, x, y, template.Z, template.Dir, unique: false, source: SpriteSource.CopiedScene);

        // Keep the pads at the original's spacing, mirrored about the site
        foreach (var member in template.Members)
        {
            if (member.Id.IsEndlessGenerator)
            {
                if (ctx.EndlessGeneratorsLeft <= 0)
                {
                    continue;
                }

                ctx.EndlessGeneratorsLeft--;
            }

            float factor = member.Id == KnownObject.TeleportPad ? 1 : shrink;
            float mx = x + member.Dx * factor;
            float my = y + member.Dy * factor;
            if (member.Id != KnownObject.TeleportPad && !Fits(mx, my))
            {
                (mx, my) = (x + member.Dx * 0.3f, y + member.Dy * 0.3f);
                if (!Fits(mx, my))
                {
                    continue;
                }
            }

            ctx.Map.Add(member.Id, mx, my, member.Z, member.Dir, member.Payload, member.Army, unique: false, source: SpriteSource.CopiedScene);
            ctx.Occupancy.Add(member.Id, mx, my, member.Z);
        }

        return;

        bool Fits(float px, float py) => ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(px, py)) && !ctx.IsInWall(px, py, 2);
    }

    // Put one batch of charges in each room of the original's charge rooms' roles
    // Lays each phase's charges beside the last key of the phase before, else in the start room on the fitted share of levels,
    // else on the walk the fitted lead before the phase's first site
    private static void PlaceCharges(PlacementContext ctx, (float X, float Y, Cell Cell)[] sites, EconomyModel economy)
    {
        var keys = ctx.Plan.GeneratorRooms.Select(entry => entry.Room).Append(ctx.Plan.KeyRoom).OfType<PlanRegion>().ToArray();
        bool startBatch = ctx.Rng.NextSingle() < economy.StartBatchShare.Value;
        foreach (var batch in sites.GroupBy(site => ctx.Layout.Cells.TryGetValue(site.Cell, out int region) ? ctx.Plan.Regions[region].Phase : 0))
        {
            var key = keys.Where(room => room.Phase == batch.Key - 1).MaxBy(ctx.GetProgress);
            Cell[] cells;
            if (key is not null || startBatch && batch.Key == 0)
            {
                cells = ctx.GetFreeCells(key ?? ctx.Plan.Stations[0]);
            }
            else
            {
                var first = batch.MinBy(site => ctx.Distance.GetValueOrDefault(site.Cell));
                float target = ctx.Distance.GetValueOrDefault(first.Cell) - economy.ChargeLead.Value * ctx.RouteLength;
                cells = ctx.TraceWalk(first.Cell).SkipWhile(c => ctx.Distance[c] > target).Where(ctx.IsFree).ToArray();
            }

            var free = cells.Where(c => ctx.FindSpot(c) is not null && sites.All(site => GetDistance(GetCenter(ctx, c), new PointF(site.X, site.Y)) >= ChargeSiteGap)).ToArray();
            var spot = free.Where(ctx.IsClearAround).Concat(free).Cast<Cell?>().FirstOrDefault();
            if (spot is null)
            {
                continue;
            }

            int count = batch.Count();
            var origin = spot.Value;
            var room = ctx.GetFreeCells(ctx.Plan.Regions[ctx.Layout.Cells[origin]])
                .Where(c => ctx.FindSpot(c) is not null && sites.All(site => GetDistance(GetCenter(ctx, c), new PointF(site.X, site.Y)) >= ChargeSiteGap))
                .Prepend(origin)
                .Distinct()
                .ToArray();

            var cluster = room.OrderBy(c => (c.U - origin.U) * (c.U - origin.U) + (c.V - origin.V) * (c.V - origin.V)).Take(count).ToArray();
            foreach (var cell in cluster)
            {
                _ = ctx.Place([cell], KnownObject.Dynamite, dir: 0, take: false) ?? ctx.Place(room, KnownObject.Dynamite, dir: 0, take: false);
            }

            ctx.Taken.UnionWith(cluster);
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
            .Where(c => ctx.Layout.Cells.TryGetValue(c, out int region) && region != climax.Region && ctx.IsFree(c) && ctx.IsClearAround(c) && !ctx.IsInWall(GetCenter(ctx, c).X, GetCenter(ctx, c).Y, 24))
            .Cast<Cell?>()
            .FirstOrDefault();

        if (spot is not null)
        {
            script.Checkpoint = GetCenter(ctx, spot.Value);
        }
    }
}
