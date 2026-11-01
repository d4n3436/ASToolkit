using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Provides the placement of a level's gameplay content by its progress along the route.
/// </summary>
public static class ContentPlacer
{
    private static readonly PointF BreakerOffset = new(-8, 3);
    private const float GroupStartGap = 900;
    private const int GroupRoomTries = 4;
    private const float HiddenSpawnX = 640;
    private const float HiddenSpawnY = 400;
    private const int SpawnerChoices = 4;
    private const float ChargeSiteGap = 150;
    private const float ChargeLead = 0.1f;
    private const int ChargeSpotChoices = 1;
    private const float LateSite = 0.6f;
    private const float MinMonsterGap = 12;
    private const int BreakerFront = 3;
    private const int BreakerSide = 2;
    private const int BreakerOpenEnd = 3;
    private const float BreakerPropFloor = 5;
    private const float ClutterHeight = 5;
    private const float StandingHeight = 20;
    private const int BreakerWallRun = 4;
    private const float BreakerGateReach = 400;
    private const float BreakerGateSlack = 100;
    private const float GeneratorFenceGap = 900;
    private const int LateGeneratorCells = 4;
    private const float WeaponSpacing = 600;
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
    private const float CannonGroupGap = 600;
    private const float FarWallGap = 1.0f;
    private const float NearWallGap = 2.2f;
    private const float WallGapSlack = 0.2f;
    private const float OpeningReach = 2.5f;
    private const float MinApproach = -0.2f;
    private const float MinCannonApproach = 0.5f;
    private const float RoomReach = 0.9f;
    private const float CornerWeight = 0.5f;
    private const float FarEndWeight = 1.5f;
    private const int PileRoomTries = 4;
    private const int SpawnRoomTries = 6;
    private const float LargeSiteExtent = 40;
    private const float SmallSiteGap = 16;
    private const float LargeSiteGap = 30;
    private const float PileWallMargin = 2;
    private const float PadSpacing = 520;
    private const float PadGap = 80;
    private const float PadWallDistance = 0.85f;
    private const float PadWallMargin = 20;
    private const float LatestBossStart = 0.65f;
    private const float GroupDetour = 1.4f;
    private const float LateGeneratorGap = 0.15f;
    private static readonly KnownObject[] GorePools = [KnownObject.SmallBloodPool, KnownObject.BloodPool];
    private const float DefaultCheckpoint = 0.3f;

    private static readonly (float Dx, float Dy)[] PileOffsets = [(0, 0), (14, 6), (-12, 8), (4, -10), (-6, -4), (16, -6), (-16, 0), (8, 12)];
    private const float DoorMarkWallReach = 1;
    private const float WeaponCrateShare = 0.35f;
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
            var doors = gates.SelectMany(link => layout.LinkEdges[plan.Links.IndexOf(link)]).Select(edge => StructureBuilder.GetLabPosition(layout.Lattice, edge).Item1).ToArray();
            float Reach(EdgeKey wall) => StructureBuilder.GetLabPosition(layout.Lattice, wall).Item1 is var (x, y) ? doors.Min(door => GetDistance(new PointF(x, y), new PointF(door.X, door.Y))) : 0;
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
            ((float ax, float ay), _) = StructureBuilder.GetLabPosition(layout.Lattice, wall);
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

        ((float wx, float wy), _) = StructureBuilder.GetLabPosition(layout.Lattice, best!.Value.Wall);
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
                if (WallClearanceTable.MeasureDistance(edges, u, v) < DoorMarkWallReach)
                {
                    map.Add(mark.Id, door.Position.X + mark.Dx, door.Position.Y + mark.Dy, mark.Z, mark.Dir, unique: false);
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

                map.Add(KnownObject.BrokenKeypad, x, y, keypad.Z, facings.PickFacing(rng, KnownObject.BrokenKeypad, edges, u, v));
            }
        }
    }

    /// <summary>
    /// Places the gate keys, bosses, mounted cannon, dynamite, monsters, ambushes, stashes, pickups and the checkpoint.
    /// </summary>
    public static void Place(PlacementContext ctx, LevelBudget budget, bool hasCheckpoint, bool blackoutIsCheckpoint, LevelScript script, BreakerSpot? breaker,
        IReadOnlyList<SiteTemplate> siteTemplates, IReadOnlyDictionary<EdgeKey, EdgeKind> visibleEdges, IReadOnlyDictionary<EdgeKey, EdgeKind> builtEdges, IReadOnlyDictionary<EdgeKey, int> spawnWalls, KeypadTable keypads, FacingTable facings,
        IReadOnlyList<IReadOnlyList<PropPlacement>> cannonScenes, EncounterProfile encounters, PlacedSites? placed)
    {
        PlaceGate(ctx, budget, script, breaker, keypads, encounters.GeneratorProgress, facings);
        PlaceBosses(ctx, budget, facings, builtEdges);
        var cannon = placed is null ? PlaceMountedCannons(ctx, budget, cannonScenes) : placed.Cannon;
        var sites = placed?.Sites ?? PlaceSites(ctx, budget, siteTemplates, visibleEdges, cannon, encounters);
        PlaceCharges(ctx, sites, encounters);
        PlaceEncounters(ctx, budget, encounters, spawnWalls);
        PlaceStashes(ctx, budget);
        PlaceItems(ctx, budget, encounters);
        if (hasCheckpoint && (script.Blackout is null || !blackoutIsCheckpoint))
        {
            PlaceCheckpoint(ctx, script, encounters);
        }
    }

    /// <summary>
    /// Places the mounted cannon and the dynamite sites of a military level before its decor and returns them.
    /// </summary>
    public static PlacedSites PlaceMilitarySites(PlacementContext ctx, LevelBudget budget, IReadOnlyList<SiteTemplate> siteTemplates, IReadOnlyList<IReadOnlyList<PropPlacement>> cannonScenes,
        EncounterProfile encounters)
    {
        var cannon = PlaceMountedCannons(ctx, budget, cannonScenes);
        return new PlacedSites(cannon, PlaceSites(ctx, budget, siteTemplates, new Dictionary<EdgeKey, EdgeKind>(), cannon, encounters));
    }

    private static bool IsWallFit(Lattice lattice, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, WallClearanceTable clearance, FacingTable facings, PropPlacement p, float x, float y)
    {
        if (p.Z >= BreakerPropFloor)
        {
            return true;
        }

        (float u, float v) = lattice.ToLattice(x, y);
        return facings.Fits(p.Id, p.Dir, edges, u, v)
               && (!clearance.TryGetClearance(p.Id, out float minimum, out _) || WallClearanceTable.MeasureDistance(edges, u, v) >= minimum);
    }

    private static int CountOpenCells(FineLayout layout, Cell start, int du, int dv) =>
        FloorScanner.CountOpenCells(layout.Cells.ContainsKey, layout.Edges.ContainsKey, start, du, dv);

    private static (PlanRegion Region, RoomRole Role, GateZone Zone)[] GetRoleRooms(PlacementContext ctx, RoomRoleMap? roles = null)
    {
        roles ??= ctx.Plan.MeasureRoles();
        return ctx.Plan.Regions.Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing or RegionKind.Pen)
            .Select(region => (region, roles.GetRole(region.Id, region.IsHall), ctx.Plan.GetZone(region))).ToArray();
    }

    private static PlanRegion[] GetRooms(PlacementContext ctx, bool wing = true) =>
        ctx.Plan.Regions.Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing && r != ctx.Plan.KeyRoom && (wing || !IsInWing(ctx, r))).ToArray();

    // Returns whether a room lies on the chain of stations or one room off it, where the originals keep their fights and objectives
    private static bool IsNearBody(PlacementContext ctx, PlanRegion region) =>
        region.Kind == RegionKind.Station || (region.Host is not null && ctx.Plan.Regions[region.Host.Value].Kind == RegionKind.Station);

    private static bool IsInWing(PlacementContext ctx, PlanRegion region) =>
        region.Kind == RegionKind.Wing || (region.Host is not null && ctx.Plan.Regions[region.Host.Value].Kind == RegionKind.Wing);

    // Returns 2 for a side room off another side room and 1 for any other room
    private static int GetRoomDepth(PlacementContext ctx, PlanRegion r) =>
        r is { Kind: RegionKind.SideRoom, Host: not null } && ctx.Plan.Regions[r.Host.Value].Kind == RegionKind.SideRoom ? 2 : 1;

    private static PointF GetCenter(PlacementContext ctx, Cell c) => ctx.Layout.Lattice.GetCenter(c);

    private static float GetDistance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // Returns the dead-end room of a generator the original walks to well after its first one, or null
    private static void PlaceGate(PlacementContext ctx, LevelBudget budget, LevelScript script, BreakerSpot? breaker, KeypadTable keypads, IReadOnlyList<float> generatorProgress, FacingTable facings)
    {
        var plan = ctx.Plan;
        if (plan.Gate is GateKind.LockedDoors or GateKind.Blackout && breaker is not null)
        {
            ctx.Map.Add(KnownObject.Breaker, breaker.Position.X, breaker.Position.Y);
            ctx.Taken.Add(new Cell(breaker.Wall.A - 1, breaker.Wall.B));
            foreach ((var edge, EdgeKind kind) in ctx.Layout.Edges.Where(p => p.Value is EdgeKind.Door or EdgeKind.DoubleDoor).OrderBy(p => p.Key.Axis).ThenBy(p => p.Key.A).ThenBy(p => p.Key.B))
            {
                ((float x, float y), bool slash) = StructureBuilder.GetLabPosition(ctx.Layout.Lattice, edge);
                var door = kind == EdgeKind.Door ? KnownObject.SmallDoor : KnownObject.DoubleDoor;
                byte doorDir = (byte)(slash ? 0 : kind == EdgeKind.Door ? 128 : 64);
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

            var fences = plan.Links.Where(link => link.Kind == LinkKind.Fence).Select(link => ctx.MeasureDistances(GetDoorwayCell(ctx, link))).ToArray();
            bool IsClearOfFences(Cell c) => fences.All(walk => walk.GetValueOrDefault(c, int.MaxValue) * ctx.Step >= GeneratorFenceGap);
            var clear = back.Where(IsClearOfFences).ToList();
            if (clear.Count > 0)
            {
                back = clear;
            }

            for (int index = 0; index < Math.Max(1, budget.Generators) && back.Count > 0; index++)
            {
                var pool = back.Take(Math.Max(1, back.Count / 3)).ToArray();
                if (index > 0 && index < generatorProgress.Count && generatorProgress[index] - generatorProgress[0] > LateGeneratorGap)
                {
                    var lateRoom = FindDeadEnd(ctx, generatorProgress[index]);
                    if (lateRoom is not null)
                    {
                        var entry = GetDoorwayCell(ctx, ctx.Plan.Links.First(l => l.To == lateRoom.Id));
                        var late = ctx.GetFreeCells(lateRoom, inner: true).OrderByDescending(c => Math.Abs(c.U - entry.U) + Math.Abs(c.V - entry.V)).ToArray();
                        var lateClear = late.Where(IsClearOfFences).Take(LateGeneratorCells).ToArray();
                        pool = lateClear.Length > 0 ? lateClear : pool;
                    }
                }

                var placed = ctx.Place(pool, KnownObject.Generator, dir: 0);
                if (placed is null)
                {
                    break;
                }

                if (ctx.Level == 10 && GetDistance(new PointF(placed.Value.X, placed.Value.Y), new PointF(7541, 4501)) < 150)
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


            var walls = ctx.GetFreeCells(plan.KeyRoom)
                .Where(c => facings.IsByWall(KnownObject.Transformer, ctx.Layout.Edges, c.U + 0.5f, c.V + 0.5f) && facings.HasFacing(KnownObject.Transformer, ctx.Layout.Edges, c.U + 0.5f, c.V + 0.5f))
                .OrderByDescending(c => Math.Abs(c.U - entrance.U) + Math.Abs(c.V - entrance.V))
                .ToArray();
            foreach (var cell in walls.Take(Math.Min(2, back.Count / 3)))
            {
                ctx.Place([cell], KnownObject.Transformer, dir: facings.PickFacing(ctx.Rng, KnownObject.Transformer, ctx.Layout.Edges, cell.U + 0.5f, cell.V + 0.5f));
            }
        }
    }

    private static PlanRegion? FindDeadEnd(PlacementContext ctx, float progress) => ctx.Plan.Regions
        .Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom && r != ctx.Plan.KeyRoom && r.Id != ctx.Plan.Stations[0].Id)
        .Where(r => ctx.Plan.Links.Count(l => l.From == r.Id || l.To == r.Id) == 1 && ctx.Plan.Links.Any(l => l.To == r.Id))
        .Where(r => ctx.GetFreeCells(r, inner: true).Length > 0)
        .MinBy(r => Math.Abs(ctx.GetProgress(r) - progress));

    private static Cell GetDoorwayCell(PlacementContext ctx, PlanLink link)
    {
        int index = ctx.Plan.Links.IndexOf(link);
        var edges = ctx.Layout.LinkEdges[index];
        var middle = edges[edges.Length / 2];
        var (a, b) = middle.Cells;
        return ctx.Layout.Cells.GetValueOrDefault(a, -1) == link.To ? a : b;
    }

    // Put bosses in the largest dead-end room outside a wing well along the walk, or else the last station
    private static void PlaceBosses(PlacementContext ctx, LevelBudget budget, FacingTable facings, IReadOnlyDictionary<EdgeKey, EdgeKind> builtEdges)
    {
        var final = ctx.Plan.Regions
            .Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom && r.Id != ctx.Plan.KeyRoom?.Id && !IsInWing(ctx, r) && (ctx.Plan.Theme.Theme != LevelTheme.Lab || IsNearBody(ctx, r)))
            .Where(r => ctx.GetProgress(r) >= LatestBossStart && ctx.Plan.Links.Count(l => l.From == r.Id || l.To == r.Id) == 1)
            .Where(r => ctx.Plan.Links.Any(l => l.To == r.Id))
            .OrderByDescending(r => ctx.GetRegionCells(r).Count)
            .FirstOrDefault() ?? ctx.Plan.Stations[^1];
        
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
                    float angle = ctx.Rng.NextSingle() * 2 * MathF.PI;
                    float distance = 60 + 120 * ctx.Rng.NextSingle();
                    (float x, float y) = (boss.Value.X + MathF.Cos(angle) * distance, boss.Value.Y + MathF.Sin(angle) * distance * 0.7f);
                    (float u, float v) = ctx.Layout.Lattice.ToLattice(x, y);
                    if (ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(x, y)) && !ctx.IsInWall(x, y) && WallClearanceTable.MeasureDistance(ctx.Layout.Edges, u, v) >= GoreClearance)
                    {
                        var pool = GorePools[ctx.Rng.Next(GorePools.Length)];
                        ctx.Map.Add(pool, x, y, 0, facings.PickFacing(ctx.Rng, pool, ctx.Layout.Edges, u, v), unique: false);
                        if (thirdIndex < 2)
                        {
                            var corpse = KnownObject.SoldierCorpse + thirdIndex;
                            byte facing = facings.PickFacing(ctx.Rng, corpse, builtEdges, u, v);
                            if (facings.Fits(corpse, facing, builtEdges, u, v))
                            {
                                ctx.Map.Add(corpse, x, y, 0, facing, unique: false);
                            }
                        }
                    }
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
                    ctx.Taken.UnionWith(ring.Select(r => r.Cell));
                    if (scenes.Count > 0)
                    {
                        foreach (var stripe in scenes[ctx.Rng.Next(scenes.Count)])
                        {
                            if (ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(x + stripe.Dx, y + stripe.Dy)) && !ctx.IsInWall(x + stripe.Dx, y + stripe.Dy, 0))
                            {
                                ctx.Map.Add(stripe.Id, x + stripe.Dx, y + stripe.Dy, stripe.Z, stripe.Dir, unique: false);
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
        (PlanRegion Hall, PointF Far)? cannon, EncounterProfile encounters)
    {
        var sites = new List<(float X, float Y, Cell Cell)>();
        if (budget.DynamiteSites == 0 || templates.Count == 0)
        {
            return [];
        }

        var allowed = templates.Where(t => t.Rank <= SiteRankCap[ctx.Level]).ToArray();
        if (allowed.Length == 0)
        {
            allowed = templates.ToArray();
        }

        var own = allowed.Where(t => t.Level == ReferenceLevelSet.GetCampaignName(ctx.Level)).ToArray();
        if (own.Length > 0)
        {
            allowed = own;
        }

        int n = budget.DynamiteSites;
        bool lab = ctx.Plan.Theme.Theme == LevelTheme.Lab;
        var rooms = GetRoleRooms(ctx).Where(room => room.Region != ctx.Plan.KeyRoom && !IsInWing(ctx, room.Region)).ToArray();
        var originals = encounters.SiteRooms.SelectMany(room => Enumerable.Repeat(room, room.Count)).ToArray();
        var targets = Enumerable.Range(0, n).Select(siteIndex => originals.Length == 0 ? null : originals[GetRank(siteIndex, n, originals.Length)]).ToArray();
        var held = new Dictionary<ObjectiveRoom, PlanRegion>(ReferenceEqualityComparer.Instance);

        // Put some of the sites in the cannon's kill zone, in place of the ones due nearest to it
        var inHall = new HashSet<int>();
        if (cannon is not null && !lab)
        {
            float farProgress = ctx.GetProgress(ctx.Layout.Lattice.GetCell(cannon.Value.Far.X, cannon.Value.Far.Y));
            int count = Math.Clamp((int)MathF.Round(n * HallSiteShare), 1, MaxHallSites);
            inHall = Enumerable.Range(0, n).OrderBy(siteIndex => MathF.Abs(GetTarget(siteIndex) - farProgress)).Take(count).ToHashSet();
        }

        // Give every room one site template
        var roomTemplates = new Dictionary<int, SiteTemplate>();
        for (int siteIndex = 0; siteIndex < n; siteIndex++)
        {
            var original = targets[siteIndex];
            var candidates = OrderSiteRooms(original, GetTarget(siteIndex));
            if (candidates.Length == 0)
            {
                break;
            }

            var region = candidates[0];
            (float X, float Y, Cell Cell)? site;
            bool hallSite = inHall.Contains(siteIndex);
            var home = hallSite ? cannon!.Value.Hall : region;
            if (!lab)
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
                site = candidates.Select(room => PlaceLabSite(ctx, room, allowed, visibleEdges)).FirstOrDefault(placed => placed is not null);
            }

            site ??= PlaceCellSite(home, openAllowed: true);
            if (site is null)
            {
                continue;
            }

            sites.Add(site.Value);
            if (original is not null && ctx.Layout.Cells.TryGetValue(site.Value.Cell, out int landed))
            {
                held[original] = ctx.Plan.Regions[landed];
            }

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
                        if (ctx.IsInWall(x, y, 20) || IsCrowded(ctx, x, y) || sites.Any(other => GetDistance(new PointF(other.X, other.Y), new PointF(x, y)) < SiteSpacing))
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

                        StampSite(ctx, template, x, y, lab ? 0.3f : 1.0f);
                        ctx.Taken.Add(cell);
                        return (x, y, cell);
                    }
                }

                return null;
            }
        }

        return sites.OrderBy(s => ctx.Distance.GetValueOrDefault(s.Cell)).ToArray();

        float GetTarget(int siteIndex) => targets[siteIndex]?.Progress ?? LateSite + (1 - LateSite) * siteIndex / Math.Max(1, n - 1);

        PlanRegion[] OrderSiteRooms(ObjectiveRoom? original, float progress) => OrderObjectiveRooms(ctx, rooms, held, original, progress, [], encounters.IsGated);
    }

    // Order the rooms by an original objective room, keeping the objectives of one original room together and apart from the others
    private static PlanRegion[] OrderObjectiveRooms(PlacementContext ctx, (PlanRegion Region, RoomRole Role, GateZone Zone)[] rooms, Dictionary<ObjectiveRoom, PlanRegion> held,
        ObjectiveRoom? original, float progress, HashSet<int> onTheWay, bool isZoned) => rooms
        .OrderBy(room => original is not null && held.TryGetValue(original, out var own) && own == room.Region ? 0 : 1)
        .ThenBy(room => original is null || !isZoned || room.Zone == original.Zone ? 0 : 1)
        .ThenBy(room => onTheWay.Contains(room.Region.Id) ? 0 : 1)
        .ThenBy(room => original is null || room.Role == original.Role ? 0 : 1)
        .ThenBy(room => held.ContainsValue(room.Region) ? 1 : 0)
        .ThenBy(room => Math.Abs(ctx.GetProgress(room.Region) - progress))
        .ThenBy(room => room.Region.Id)
        .Select(room => room.Region)
        .ToArray();

    // Turn a far wall piece into a monster hole, with the 179 in front and the endless generators behind it
    private static (float X, float Y, Cell Cell)? PlaceLabSite(PlacementContext ctx, PlanRegion region, SiteTemplate[] templates, IReadOnlyDictionary<EdgeKey, EdgeKind> visibleEdges)
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
            ((float x, float y), bool isSlash) = StructureBuilder.GetLabPosition(ctx.Layout.Lattice, k);
            return !IsCrowded(ctx, x + 25 * (isSlash ? 1 : -1), y + 20);
        }).ToArray();
        
        if (walls.Length == 0)
        {
            return null;
        }

        var wall = walls[ctx.Rng.Next(walls.Length)];
        ((float hx, float hy), bool slash) = StructureBuilder.GetLabPosition(ctx.Layout.Lattice, wall);
        int index = ctx.Map.Sprites.FindIndex(s => s.Id is KnownObject.LabWallSlash or KnownObject.LabWallBack && Math.Abs(s.Position.X - hx) < 1 && Math.Abs(s.Position.Y - hy) < 1);
        if (index < 0)
        {
            return null;
        }

        var hole = ctx.Level >= 3 || ctx.Rng.Next(2) == 0 ? KnownObject.BigMonsterHole : KnownObject.SmallMonsterHole;
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

        foreach (var generator in generators)
        {
            ctx.Map.Add(generator.Id, hx - 30 * side + ctx.Rng.NextSingle() * 16 - 8, hy - 42 + ctx.Rng.NextSingle() * 12 - 6, generator.Z, generator.Dir, generator.Payload, generator.Army, unique: false);
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
    private static bool IsNearPads(PlacementContext ctx, float x, float y) => ctx.Map.Sprites.Any(s =>
        s.Id is KnownObject.TeleportPad or KnownObject.SilentTeleportPad && GetDistance(new PointF(s.Position.X, s.Position.Y), new PointF(x, y)) < PadSpacing);

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
                    if (!cells.Contains(siteCell) || !ctx.IsFree(siteCell) || ctx.IsInWall(x, y, 20) || IsCrowded(ctx, x, y) || !HasRoom(siteU, siteV)
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
                (float distance, EdgeKind kind) = CastRay(edges, ctx.Layout.FakeDoors, u, v, edgeAxis, sign);
                if (kind != EdgeKind.Wall && distance < OpeningReach || distance < GetGap(edgeAxis, sign) - WallGapSlack)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // Treat the +u and -v sides as far walls
    private static float GetGap(char edgeAxis, int sign) => (edgeAxis == 'u') == (sign > 0) ? FarWallGap : NearWallGap;

    // Count a fake door as an opening
    private static (float Distance, EdgeKind Kind) CastRay(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, IReadOnlyDictionary<EdgeKey, bool> fakeDoors, float u, float v, char edgeAxis, int sign)
    {
        for (int step = 0; step < 8; step++)
        {
            var edge = edgeAxis == 'u'
                ? new EdgeKey('u', sign > 0 ? (int)MathF.Floor(u) + 1 + step : (int)MathF.Floor(u) - step, (int)MathF.Floor(v))
                : new EdgeKey('v', (int)MathF.Floor(u), sign > 0 ? (int)MathF.Floor(v) + 1 + step : (int)MathF.Floor(v) - step);
            if (edges.TryGetValue(edge, out EdgeKind kind))
            {
                bool door = fakeDoors.ContainsKey(edge) || fakeDoors.ContainsKey(StructureBuilder.GetPreviousEdge(edge)) || fakeDoors.ContainsKey(edgeAxis == 'u' ? new EdgeKey('u', edge.A, edge.B + 1) : new EdgeKey('v', edge.A + 1, edge.B));
                return (MathF.Abs((edgeAxis == 'u' ? edge.A : edge.B) - (edgeAxis == 'u' ? u : v)), door ? EdgeKind.Door : kind);
            }
        }

        return (float.MaxValue, EdgeKind.Wall);
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
            return FacingTable.FindWall(ctx.Layout.Edges, u, v) is { Side: WallSide.UpperU or WallSide.LowerV, Distance: <= PadWallReach };
        }
    }

    // Stand a pad in the open or off a far wall, in view and clear of other pads, as the originals stand theirs
    private static bool IsPadSpot(PlacementContext ctx, float x, float y)
    {
        (float u, float v) = ctx.Layout.Lattice.ToLattice(x, y);
        var wall = FacingTable.FindWall(ctx.Layout.Edges, u, v);
        return ctx.Distance.ContainsKey(ctx.Layout.Lattice.GetCell(x, y)) && !ctx.IsInWall(x, y, PadWallMargin) && ctx.IsVisible(x, y)
            && (!wall.HasValue || wall.Value.Side is WallSide.UpperU or WallSide.LowerV && wall.Value.Distance >= PadWallDistance)
            && !ctx.Map.Sprites.Any(s => s.Id is KnownObject.TeleportPad or KnownObject.SilentTeleportPad && GetDistance(new PointF(s.Position.X, s.Position.Y), new PointF(x, y)) < PadGap);
    }

    private static bool IsCrowded(PlacementContext ctx, float x, float y) => ctx.Map.Sprites.Any(s =>
    {
        if (!ctx.Objects.TryFind(s.Id, out var obj) || s.Position.Z >= ClutterHeight || obj is { IsSolid: false, Size.Z: < StandingHeight } || !s.Id.IsDecor && s.Id != KnownObject.Crate)
        {
            return false;
        }

        float gap = MeasureSiteGap(obj, s.Position.X, s.Position.Y, x, y);
        return gap < GetSiteGap(obj);
    });

    private static void StampSite(PlacementContext ctx, SiteTemplate template, float x, float y, float shrink)
    {
        ctx.Map.Add(KnownObject.DynamiteSite, x, y, template.Z, template.Dir, unique: false);

        // Keep the pads at the original's spacing, mirrored about the site
        foreach (var member in template.Members)
        {
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

            ctx.Map.Add(member.Id, mx, my, member.Z, member.Dir, member.Payload, member.Army, unique: false);
        }

        return;

        bool Fits(float px, float py) => ctx.Layout.Cells.ContainsKey(ctx.Layout.Lattice.GetCell(px, py)) && !ctx.IsInWall(px, py, 2);
    }

    // Put one batch of charges in each room of the original's charge rooms' roles
    private static void PlaceCharges(PlacementContext ctx, (float X, float Y, Cell Cell)[] sites, EncounterProfile encounters)
    {
        var rooms = GetRoleRooms(ctx).Where(room => !IsInWing(ctx, room.Region)).ToArray();
        var originals = encounters.ChargeRooms.SelectMany(room => Enumerable.Repeat(room, room.Count)).ToArray();
        var held = new Dictionary<ObjectiveRoom, PlanRegion>(ReferenceEqualityComparer.Instance);
        var batches = Enumerable.Range(0, sites.Length).GroupBy(index => originals.Length == 0 ? null : originals[GetRank(index, sites.Length, originals.Length)], ReferenceEqualityComparer.Instance);
        var ends = sites.Select(site => site.Cell).Concat(ctx.Plan.KeyRoom is null ? [] : [GetKeyCell(ctx, ctx.Plan.KeyRoom)]);
        var onTheWay = ends.SelectMany(end => TraceWalk(ctx, end)).Where(ctx.Layout.Cells.ContainsKey).Select(c => ctx.Layout.Cells[c]).ToHashSet();
        foreach (var batch in batches)
        {
            var original = (ObjectiveRoom?)batch.Key;
            float progress = original?.Progress ?? Math.Max(0, ctx.GetProgress(sites[0].Cell) - ChargeLead);
            var ordered = OrderObjectiveRooms(ctx, rooms, held, original, progress, onTheWay, encounters.IsGated).ToArray();
            var spot = new[] { true, false }.SelectMany(inner => ordered.SelectMany(room => ctx.GetFreeCells(room, inner).Where(c => ctx.Distance.ContainsKey(c)
                    && sites.All(site => GetDistance(GetCenter(ctx, c), new PointF(site.X, site.Y)) >= ChargeSiteGap) && ctx.FindSpot(c) is not null).Take(ChargeSpotChoices)))
                .Cast<Cell?>()
                .FirstOrDefault();
            if (spot is null)
            {
                continue;
            }

            if (original is not null)
            {
                held[original] = ctx.Plan.Regions[ctx.Layout.Cells[spot.Value]];
            }

            var cluster = new List<Cell> { spot.Value };
            foreach (var (neighbor, _) in EdgeKey.GetNeighbors(spot.Value))
            {
                if (ctx.IsFree(neighbor))
                {
                    cluster.Add(neighbor);
                }
            }

            int count = batch.Count();
            for (int index = 0; index < count; index++)
            {
                _ = ctx.Place([cluster[Math.Min(index, cluster.Count - 1)]], KnownObject.Dynamite, dir: 0, take: false) ?? ctx.Place(cluster, KnownObject.Dynamite, dir: 0, take: false);
            }

            ctx.Taken.UnionWith(cluster);
        }
    }

    // Walk back from a cell to the start down the walking distances
    private static IEnumerable<Cell> TraceWalk(PlacementContext ctx, Cell from)
    {
        var cell = from;
        while (ctx.Distance.TryGetValue(cell, out int distance) && distance > 0)
        {
            yield return cell;
            Cell? next = null;
            foreach (var (neighbor, _) in EdgeKey.GetNeighbors(cell))
            {
                if (ctx.Distance.TryGetValue(neighbor, out int step) && step < distance)
                {
                    next = neighbor;
                    break;
                }
            }

            if (next is null)
            {
                yield break;
            }

            cell = next.Value;
        }
    }

    // Measure the distance from a point to the footprint of an object
    internal static float MeasureSiteGap(GameObject obj, float objectX, float objectY, float x, float y)
    {
        float dx = Math.Max(0, Math.Abs(objectX - x) - obj.Size.X / 2);
        float dy = Math.Max(0, Math.Abs(objectY - y) - obj.Size.Y / 2);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // Return the smallest gap the originals leave between an object's footprint and a site
    internal static float GetSiteGap(GameObject obj) => Math.Max(obj.Size.X, obj.Size.Y) >= LargeSiteExtent ? LargeSiteGap : SmallSiteGap;

    private static Cell GetKeyCell(PlacementContext ctx, PlanRegion keyRoom) => ctx.GetRegionCells(keyRoom).First(ctx.Distance.ContainsKey);

    private static bool IsNearMonster(PlacementContext ctx, float x, float y) =>
        ctx.Map.Sprites.Any(s => s.Army == 1 && MathF.Abs(s.Position.X - x) < MinMonsterGap && MathF.Abs(s.Position.Y - y) < MinMonsterGap);

    private static bool HasGroupRoom(PlacementContext ctx, PlanRegion region, PointF start) =>
        ctx.GetFreeCells(region).Any(c => GetDistance(GetCenter(ctx, c), start) >= GroupStartGap);

    // Anchor a group's original shape in a room, keeping the members a short walk from the anchor
    private static PlanRegion? TryPlaceGroup(PlacementContext ctx, IReadOnlyList<GroupMember> group, IReadOnlyList<PlanRegion> regions, PointF start)
    {
        // Keep groups 600 px off the cannons
        var cannons = ctx.Map.Sprites.Where(s => s.Id == KnownObject.MountedCannon).Select(s => new PointF(s.Position.X, s.Position.Y)).ToArray();
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var r = regions[ctx.Rng.Next(regions.Count)];
            var anchors = ctx.GetFreeCells(r).Where(c => GetDistance(GetCenter(ctx, c), start) >= 900 && cannons.All(cannon => GetDistance(GetCenter(ctx, c), cannon) >= CannonGroupGap)).ToArray();
            if (anchors.Length == 0)
            {
                continue;
            }

            var anchor = anchors[ctx.Rng.Next(anchors.Length)];
            (float ax, float ay) = GetCenter(ctx, anchor);
            float reach = group.Max(member => MathF.Sqrt(member.Dx * member.Dx + member.Dy * member.Dy));
            var near = MeasureNearWalk(ctx, anchor, (int)(GroupDetour * reach / ctx.Step) + 2);
            var spots = new List<(GroupMember Member, float X, float Y)>();
            foreach (var member in group)
            {
                float x = ax + member.Dx;
                float y = ay + member.Dy;
                var c = ctx.Layout.Lattice.GetCell(x, y);
                bool closet = ctx.Layout.Cells.TryGetValue(c, out int region) && ctx.Plan.Regions[region].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset;
                bool open = near.TryGetValue(c, out int steps) && steps * ctx.Step <= GroupDetour * GetDistance(new PointF(x, y), new PointF(ax, ay)) + 2 * ctx.Step;
                if (open && !closet && !ctx.IsInWall(x, y, 2) && !IsNearMonster(ctx, x, y) && GetDistance(new PointF(x, y), start) >= 900
                    && (member.Id != KnownObject.TeleportPad || IsPadSpot(ctx, x, y)))
                {
                    spots.Add((member, x, y));
                }
            }

            bool padsFit = spots.Count(spot => spot.Member.Id == KnownObject.TeleportPad) == group.Count(member => member.Id == KnownObject.TeleportPad);
            if (padsFit && spots.Count >= Math.Max(1, (int)(group.Count * 0.8f)))
            {
                foreach ((var member, float x, float y) in spots)
                {
                    ctx.Map.Add(member.Id, x, y, 0, member.Dir, MapBuilder.MonsterPayload, 1, unique: false);
                }

                return r;
            }
        }

        return null;
    }

    // Count the steps over reachable floor from a cell, up to a limit
    private static Dictionary<Cell, int> MeasureNearWalk(PlacementContext ctx, Cell from, int limit)
    {
        var steps = new Dictionary<Cell, int> { [from] = 0 };
        var queue = new Queue<Cell>([from]);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (steps[cell] >= limit)
            {
                continue;
            }

            foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (ctx.Distance.ContainsKey(next) && !steps.ContainsKey(next) && !(ctx.Layout.Edges.TryGetValue(edge, out EdgeKind kind) && kind.BlocksWalking))
                {
                    steps[next] = steps[cell] + 1;
                    queue.Enqueue(next);
                }
            }
        }

        return steps;
    }

    // Give the budget's standing groups and ambushes, by rank of hit points, the original's fights of the same rank, each in a room of that
    // fight's role and zone nearest its progress, and release a spawned fight from the entrance of the room that plays its trigger's role
    private static void PlaceEncounters(PlacementContext ctx, LevelBudget budget, EncounterProfile profile, IReadOnlyDictionary<EdgeKey, int> spawnWalls)
    {
        var roles = ctx.Plan.MeasureRoles();
        var rooms = GetRoleRooms(ctx, roles);
        var start = GetCenter(ctx, ctx.Start);
        var held = new Dictionary<Encounter, PlanRegion>(ReferenceEqualityComparer.Instance);
        var triggers = new Dictionary<(RoomRole, GateZone), PlanRegion>();
        var standing = profile.Encounters.Where(fight => !fight.IsSpawned).OrderBy(fight => fight.Share).ToArray();
        var groups = budget.PlacedGroups.OrderBy(group => group.Sum(member => GetHitPoints([member.Id]))).ToArray();
        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var fight = standing.Length == 0 ? null : standing[GetRank(groupIndex, groups.Length, standing.Length)];
            var candidates = OrderRooms(fight, groupIndex / (float)Math.Max(1, groups.Length - 1)).Where(room => HasGroupRoom(ctx, room, start)).Take(GroupRoomTries).ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }

            var placed = candidates.Select(room => TryPlaceGroup(ctx, groups[groupIndex], [room], start)).FirstOrDefault(room => room is not null);
            if (fight is not null && placed is not null)
            {
                held.TryAdd(fight, placed);
            }

            if (placed is null)
            {
                placed = candidates[0];
                foreach (var member in groups[groupIndex].Where(member => member.Id != KnownObject.TeleportPad))
                {
                    ctx.Place(ctx.GetFreeCells(placed).Where(c => GetDistance(GetCenter(ctx, c), start) >= GroupStartGap).ToArray(), member.Id, member.Dir, MapBuilder.MonsterPayload, 1);
                }
            }

            ctx.Fights.Add(new PlacedFight(ctx.GetProgress(placed), groups[groupIndex].Sum(member => GetHitPoints([member.Id])), placed.Id));
        }

        var spawned = profile.Encounters
            .Where(fight => fight.IsSpawned)
            .OrderBy(fight => fight.Share)
            .ToArray();
        
        var ambushes = budget.Ambushes
            .OrderBy(GetHitPoints)
            .ToArray();
        
        var closets = new Queue<PlanRegion>(ctx.Plan.Regions.Where(region => region.Kind == RegionKind.SpawnCloset).OrderBy(ctx.GetProgress));
        
        var waves = Enumerable.Range(0, ambushes.Length).GroupBy(index => spawned.Length == 0 ? index : GetRank(index, ambushes.Length, spawned.Length));
        foreach (var wave in waves)
        {
            var monsters = wave.SelectMany(index => ambushes[index]).ToList();
            var fight = spawned.Length == 0 ? null : spawned[wave.Key];
            if (fight is { Role: RoomRole.Closet } && closets.TryDequeue(out var closet) && PlaceClosetAmbush(ctx, closet, monsters, spawnWalls, budget))
            {
                ctx.Fights.Add(new PlacedFight(ctx.GetProgress(closet), GetHitPoints(monsters), closet.Id));
                continue;
            }

            var options = OrderRooms(fight, wave.Key / (float)Math.Max(1, ambushes.Length - 1))
                .Take(SpawnRoomTries)
                .ToArray();
            
            var (room, trigger) = options.Select(option => (Room: option, Trigger: FindTrigger(fight, option))).FirstOrDefault(pair => GetCellsAhead(pair.Room, pair.Trigger).Any());
            if (room is null)
            {
                room = options[0];
                trigger = GetTriggerCell(room);
            }

            if (fight is not null)
            {
                held.TryAdd(fight, room);
            }

            (float tx, float ty) = GetCenter(ctx, trigger);
            var cells = GetCellsAhead(room, trigger)
                .OrderByDescending(c => GetDistance(GetCenter(ctx, c), new PointF(tx, ty)))
                .ToArray();
            
            var hidden = cells.Where(c =>
            {
                var center = GetCenter(ctx, c);
                return MathF.Abs(center.X - tx) >= HiddenSpawnX || MathF.Abs(center.Y - ty) >= HiddenSpawnY;
            }).ToArray();
            
            if (cells.Length == 0)
            {
                AddToLastSpawner(ctx, monsters);
                continue;
            }

            PlaceAmbush(ctx, monsters, trigger, hidden.Length > 0 ? hidden[ctx.Rng.Next(Math.Min(hidden.Length, SpawnerChoices))] : cells[0]);
        }

        return;

        int GetHitPoints(IReadOnlyList<KnownObject> monsters) => monsters.Sum(m => ctx.Objects.TryFind(m, out var obj) ? obj.MaxHp : 0);

        // Order the rooms by the one a fight already holds, by how well they play its role and zone, rooms no other fight holds first, and by
        // how near their progress lies to the fight's
        IEnumerable<PlanRegion> OrderRooms(Encounter? fight, float progress) => rooms
            .OrderBy(room => fight is not null && held.TryGetValue(fight, out var own) && own == room.Region ? 0 : 1)
            .ThenBy(room => fight is null ? 0 : (room.Role == fight.Role ? 0 : 2) + (!profile.IsGated || room.Zone == fight.Zone ? 0 : 1))
            .ThenBy(room => held.ContainsValue(room.Region) ? 1 : 0)
            .ThenBy(room => Math.Abs(ctx.GetProgress(room.Region) - (fight?.Progress ?? progress)))
            .Select(room => room.Region);

        Cell FindTrigger(Encounter? fight, PlanRegion room) => fight is null ? GetTriggerCell(room)
            : GetTriggerCellAt(fight.IsTriggeredInRoom ? room : FindTriggerRoom(fight, room), fight.Progress);

        // Keep spawners on the floor the walk from the start reaches no sooner than their trigger
        IEnumerable<Cell> GetCellsAhead(PlanRegion room, Cell trigger) => ctx.GetFreeCells(room)
            .Where(c => ctx.Distance.GetValueOrDefault(c) >= ctx.Distance.GetValueOrDefault(trigger));

        // Trigger next door from the neighbor that plays the trigger's role best, or from afar in one room per trigger role and zone, the one
        // that plays it best nearest the first fight it releases
        PlanRegion FindTriggerRoom(Encounter fight, PlanRegion room)
        {
            if (!fight.IsTriggeredNextDoor && triggers.TryGetValue((fight.TriggerRole, fight.TriggerZone), out var shared))
            {
                return shared;
            }

            var center = GetCenter(ctx, ctx.GetRegionCells(room)[ctx.GetRegionCells(room).Count / 2]);
            
            var found = rooms.Where(other => other.Region != room && (!fight.IsTriggeredNextDoor || roles.AreJoined(room.Id, other.Region.Id)))
                .OrderBy(other => (other.Role == fight.TriggerRole ? 0 : 2) + (!profile.IsGated || other.Zone == fight.TriggerZone ? 0 : 1))
                .ThenBy(other => GetDistance(GetCenter(ctx, ctx.GetRegionCells(other.Region)[ctx.GetRegionCells(other.Region).Count / 2]), center))
                .Select(other => other.Region)
                .DefaultIfEmpty(room)
                .First();
            
            if (!fight.IsTriggeredNextDoor)
            {
                triggers[(fight.TriggerRole, fight.TriggerZone)] = found;
            }

            return found;
        }

        // Trigger at a room's entrance, or for the start room at its floor farthest from the player
        Cell GetTriggerCell(PlanRegion room)
        {
            var entrance = ctx.Plan.Links.FirstOrDefault(link => link.To == room.Id);
            return entrance is not null
                ? GetDoorwayCell(ctx, entrance)
                : ctx.GetFreeCells(room).DefaultIfEmpty(ctx.Start).MaxBy(c => GetDistance(GetCenter(ctx, c), start));
        }

        // Trigger where the walk through the room comes nearest the fight's progress, or at its entrance when no free cell does
        Cell GetTriggerCellAt(PlanRegion room, float progress) => ctx.GetFreeCells(room)
            .Where(ctx.Distance.ContainsKey)
            .OrderBy(c => Math.Abs(ctx.GetProgress(c) - progress))
            .Cast<Cell?>()
            .FirstOrDefault() ?? GetTriggerCell(room);
    }

    private static int GetRank(int index, int count, int ranks) => count <= 1 ? ranks - 1 : (int)MathF.Round(index * (ranks - 1) / (float)(count - 1));

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
        (float tx, float ty) = GetCenter(ctx, trigger);
        float radius = new[] { 180, 220, 260 }[ctx.Rng.Next(3)];
        var cells = ctx.GetRegionCells(closet);
        int triggerDistance = ctx.Distance.GetValueOrDefault(trigger);
        var onRoute = ctx.Plan.Stations.Select(r => r.Id).ToHashSet();
        
        var onward = ctx.Distance
            .Where(p => p.Value >= triggerDistance + 500 / ctx.Step && p.Value <= triggerDistance + 800 / ctx.Step && onRoute.Contains(ctx.Layout.Cells[p.Key]))
            .OrderBy(p => GetDistance(GetCenter(ctx, p.Key), new PointF(tx, ty))).ThenBy(p => p.Key.U).ThenBy(p => p.Key.V)
            .Select(p => (Cell?)p.Key).FirstOrDefault();
        
        List<PointF> way = onward is null ? [new PointF(tx, ty)] : [new PointF(tx, ty), GetCenter(ctx, onward.Value)];
        byte[] payload = SpawnerProgram.Create(monsters, (tx, ty, radius), way).ToPayload();
        (float sx, float sy) = GetCenter(ctx, inside);
        ctx.Map.Add(KnownObject.Spawner, sx, sy, 0, 0, payload, 1, unique: false);
        var wallProgram = new SpawnerProgram
        {
            Actions = [new StackAction((int)ActionCode.StopStack, 0, 0, 0), new StackAction(15, 0, 0, 0), new StackAction((int)ActionCode.FlagmanTrigger, (int)tx, (int)ty, (int)radius)],
            Trailer = new byte[4]
        };
        ctx.Map.Sprites[wallIndex] = ctx.Map.Sprites[wallIndex] with { Payload = wallProgram.ToPayload() };
        if (ctx.Rng.NextSingle() < 0.3f)
        {
            foreach (var item in ItemSchedule.CreateStash(ctx.Level, ctx.Rng, () => TakeWeapon(ctx, budget)).Take(2))
            {
                var c = cells[ctx.Rng.Next(cells.Count)];
                (float x, float y) = GetCenter(ctx, c);
                ctx.Map.Add(item, x, y, 0, 0, unique: false);
                TakeFromBudget(budget, item);
            }
        }

        return true;
    }

    // Give an ambush with nowhere ahead of its trigger to spawn to the spawner placed last
    private static void AddToLastSpawner(PlacementContext ctx, IReadOnlyList<KnownObject> monsters)
    {
        int index = ctx.Map.Sprites.FindLastIndex(sprite => sprite.Id == KnownObject.Spawner && SpawnerProgram.Parse(sprite.Payload)?.Trigger is not null);
        if (index < 0)
        {
            return;
        }

        var sprite = ctx.Map.Sprites[index];
        var program = SpawnerProgram.Parse(sprite.Payload)!;
        var trigger = program.Trigger!.Value;
        var merged = SpawnerProgram.Create(program.Monsters.Concat(monsters), (trigger.X, trigger.Y, trigger.Radius), program.Waypoints.Select(point => new PointF(point.X, point.Y)));
        ctx.Map.Sprites[index] = sprite with { Payload = merged.ToPayload() };
        var cell = ctx.Layout.Lattice.GetCell(trigger.X, trigger.Y);
        if (ctx.Layout.Cells.TryGetValue(cell, out int region))
        {
            ctx.Fights.Add(new PlacedFight(ctx.GetProgress(cell), monsters.Sum(m => ctx.Objects.TryFind(m, out var obj) ? obj.MaxHp : 0), region));
        }
    }

    private static void PlaceAmbush(PlacementContext ctx, IReadOnlyList<KnownObject> monsters, Cell trigger, Cell spawner)
    {
        (float tx, float ty) = GetCenter(ctx, trigger);
        var path = ctx.FindPath(spawner, trigger);
        int meeting = Math.Max(1, (int)(path.Length * 0.4f));
        meeting = Math.Min(meeting, path.Length - 1);
        int stride = Math.Max(1, meeting / 4);
        var way = new List<PointF>();
        for (int index = stride; index < meeting; index += stride)
        {
            way.Add(GetCenter(ctx, path[index]));
        }

        way.Add(GetCenter(ctx, path[meeting]));
        float radius = ctx.Plan.Theme.Theme == LevelTheme.Lab ? new[] { 250, 300, 350 }[ctx.Rng.Next(3)] : new[] { 300, 350, 400, 450 }[ctx.Rng.Next(4)];
        byte[] payload = SpawnerProgram.Create(monsters, (tx, ty, radius), way.TakeLast(6)).ToPayload();
        ctx.Place([spawner], KnownObject.Spawner, dir: 0, payload: payload, army: 1, take: false);
        if (ctx.Layout.Cells.TryGetValue(trigger, out int region))
        {
            ctx.Fights.Add(new PlacedFight(ctx.GetProgress(trigger), monsters.Sum(m => ctx.Objects.TryFind(m, out var obj) ? obj.MaxHp : 0), region));
        }
    }

    private static void PlaceStashes(PlacementContext ctx, LevelBudget budget)
    {
        foreach (var closet in ctx.Plan.Regions.Where(r => r.Kind is RegionKind.SecretCloset or RegionKind.SecretRoom))
        {
            var cells = ctx.GetRegionCells(closet);
            foreach (var item in ItemSchedule.CreateStash(ctx.Level, ctx.Rng, () => TakeWeapon(ctx, budget)))
            {
                var c = cells[ctx.Rng.Next(cells.Count)];
                (float x, float y) = ctx.Layout.Lattice.ToWorld(c.U + 0.3f + 0.4f * ctx.Rng.NextSingle(), c.V + 0.3f + 0.4f * ctx.Rng.NextSingle());
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

    private static List<PointF> GetWeaponSpots(PlacementContext ctx) => ctx.Map.Sprites
        .Where(s => s.Id.IsWeapon || (s.Id == KnownObject.Crate && KnownObject.GetCrateItem(s.Payload).IsWeapon))
        .Select(s => new PointF(s.Position.X, s.Position.Y))
        .ToList();

    private static int CountNear(IReadOnlyList<PointF> spots, PointF p) => spots.Count(s => GetDistance(s, p) < WeaponSpacing);

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
        int inCrates = (int)MathF.Round(weapons.Count(item => item.IsWeapon) * WeaponCrateShare);
        foreach (int crate in crates)
        {
            var s = ctx.Map.Sprites[crate];
            int index = weapons.FindIndex(item => item.IsWeapon);
            if (inCrates == 0 || index < 0)
            {
                break;
            }

            if (KnownObject.GetCrateItem(s.Payload) != 0 || CountNear(spots, new PointF(s.Position.X, s.Position.Y)) > 0)
            {
                continue;
            }

            ctx.Map.Sprites[crate] = s with { Payload = KnownObject.CreateCratePayload(weapons[index]) };
            spots.Add(new PointF(s.Position.X, s.Position.Y));
            weapons.RemoveAt(index);
            inCrates--;
        }

        var rooms = GetRooms(ctx);
        var middle = rooms.Where(r => ctx.GetProgress(r) is >= 0.25f and <= 0.75f && r.Kind == RegionKind.SideRoom)
            .OrderBy(r => Math.Abs(ctx.GetProgress(r) - 0.4f))
            .ThenByDescending(r => GetRoomDepth(ctx, r))
            .ToArray();
        
        foreach (var item in weapons)
        {
            if (!item.IsWeapon)
            {
                ctx.Place(ctx.GetFreeCells(rooms[ctx.Rng.Next(rooms.Length)]), item, dir: 0);
                continue;
            }

            (float X, float Y, Cell Cell)? placed = null;
            foreach (int allowed in new[] { 0, 1 })
            {
                foreach (var pool in new[] { middle, rooms })
                {
                    var cells = pool
                        .SelectMany(r => ctx.GetFreeCells(r))
                        .Where(c => CountNear(spots, GetCenter(ctx, c)) <= allowed)
                        .ToArray();
                    
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
                spots.Add(new PointF(placed.Value.X, placed.Value.Y));
            }
        }
    }

    private static void PlaceItems(PlacementContext ctx, LevelBudget budget, EncounterProfile encounters)
    {
        var items = budget.Items
            .OrderBy(p => p.Key)
            .SelectMany(p => Enumerable.Repeat(p.Key, p.Value))
            .ToList();
        
        int[] order = Enumerable.Range(0, items.Count).ToArray();
        ctx.Rng.Shuffle(order);
        items = order.Select(i => items[i]).ToList();

        int[] crates = Enumerable.Range(0, ctx.Map.Sprites.Count)
            .Where(i => ctx.Map.Sprites[i].Id == KnownObject.Crate)
            .ToArray();
        
        int[] crateOrder = crates.ToArray();
        ctx.Rng.Shuffle(crateOrder);
        PlaceWeapons(ctx, budget, crateOrder.ToArray());
        crateOrder = crateOrder.Where(i => KnownObject.GetCrateItem(ctx.Map.Sprites[i].Payload) == 0).ToArray();
        int nextCrate = 0;
        foreach ((var category, float share) in new[] { (ItemCategory.Ammo, 0.32f), (ItemCategory.Armor, 0.45f), (ItemCategory.Health, 0.2f) })
        {
            var ofCategory = items.Where(item => item.PickupCategory == category).ToArray();
            foreach (var item in ofCategory.Take((int)MathF.Round(ofCategory.Length * share)))
            {
                if (nextCrate >= crateOrder.Length)
                {
                    break;
                }

                int index = crateOrder[nextCrate++];
                ctx.Map.Sprites[index] = ctx.Map.Sprites[index] with { Payload = KnownObject.CreateCratePayload(item) };
                items.Remove(item);
            }
        }

        foreach ((var category, int pile) in new[]
        {
            (ItemCategory.Money, 8),
            (ItemCategory.Ammo, 3),
            (ItemCategory.Armor, 2),
            (ItemCategory.Health, 2),
            (ItemCategory.Implant, 1),
            (ItemCategory.Bonus, 2)
        })
        {
            var ofCategory = items
                .Where(item => item != KnownObject.Dynamite && item.PickupCategory == category)
                .ToArray();
            
            PlaceCategory(ctx, ofCategory, pile, category == ItemCategory.Money, encounters.PickupRooms[category], encounters.IsGated);
        }
    }

    // Put the piles of one category in rooms of the original's pickup rooms' zones and roles, the piles of one original room together
    private static void PlaceCategory(PlacementContext ctx, KnownObject[] items, int pile, bool corners, IReadOnlyList<ObjectiveRoom> pickupRooms, bool isZoned)
    {
        var rooms = GetRoleRooms(ctx);
        var originals = pickupRooms.SelectMany(room => Enumerable.Repeat(room, room.Count)).ToArray();
        var held = new Dictionary<ObjectiveRoom, PlanRegion>(ReferenceEqualityComparer.Instance);
        int piles = (items.Length + pile - 1) / pile;
        for (int pileIndex = 0; pileIndex < piles; pileIndex++)
        {
            var heap = items
                .Skip(pileIndex * pile)
                .Take(pile)
                .ToArray();
            
            var original = originals.Length == 0 ? null : originals[GetRank(pileIndex, piles, originals.Length)];
            var candidates = OrderObjectiveRooms(ctx, rooms, held, original, original?.Progress ?? (pileIndex + 0.5f) / piles, [], isZoned);
            var room = candidates.Take(PileRoomTries).FirstOrDefault(candidate => PlacePile(ctx, candidate, heap, corners, null));
            if (room is null)
            {
                room = candidates[0];
                foreach (var item in heap)
                {
                    var cells = ctx.GetFreeCells(room);
                    var seen = cells.Where(c => ctx.IsVisible(GetCenter(ctx, c).X, GetCenter(ctx, c).Y)).ToArray();
                    ctx.Place(seen.Length > 0 ? seen : cells, item, dir: 0);
                }
            }

            if (original is not null)
            {
                held[original] = room;
            }
        }
    }

    private static bool PlacePile(PlacementContext ctx, PlanRegion room, KnownObject[] heap, bool corner, float? after)
    {
        var cells = ctx.GetRegionCells(room)
            .Where(c => !ctx.Taken.Contains(c) && ctx.Distance.ContainsKey(c) && (after is null || ctx.GetProgress(c) >= after.Value))
            .Select(c => (Cell: c, Walls: CountWalls(ctx, c), Spot: ctx.FindSpot(c, PileWallMargin)))
            .Where(p => p is { Walls: > 0, Spot: not null } && ctx.IsVisible(p.Spot.Value.X, p.Spot.Value.Y))
            .ToArray();
        
        if (cells.Length == 0)
        {
            return false;
        }

        var best = corner && cells.Any(p => p.Walls >= 2) ? cells.Where(p => p.Walls >= 2).ToArray() : cells;
        var (cell, _, spot) = best[ctx.Rng.Next(best.Length)];
        (float x, float y) = (spot!.Value.X, spot.Value.Y);
        var offsets = PileOffsets
            .Where(o => !ctx.IsInWall(x + o.Dx, y + o.Dy, PileWallMargin) && ctx.IsVisible(x + o.Dx, y + o.Dy))
            .DefaultIfEmpty((0, 0))
            .ToArray();
        for (int index = 0; index < heap.Length; index++)
        {
            (float dx, float dy) = offsets[index % offsets.Length];
            ctx.Map.Add(heap[index], x + dx, y + dy, 0, 0, unique: false);
        }

        ctx.Taken.Add(cell);
        return true;
    }

    private static int CountWalls(PlacementContext ctx, Cell c)
    {
        int walls = 0;
        foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(c))
        {
            if ((ctx.Layout.Edges.TryGetValue(edge, out EdgeKind kind) && kind.IsClosed) || !ctx.Layout.Cells.ContainsKey(neighbor))
            {
                walls++;
            }
        }

        return walls;
    }

    // Put the checkpoint on the walk to the biggest fight, in a room of the original's checkpoint room's zone and role, nearest its progress
    private static void PlaceCheckpoint(PlacementContext ctx, LevelScript script, EncounterProfile encounters)
    {
        var original = encounters.CheckpointRoom;
        float progress = original?.Progress ?? DefaultCheckpoint;
        var climax = ctx.Fights.MaxBy(fight => fight.HitPoints);
        var climaxCells = climax is null ? [] : ctx.GetRegionCells(ctx.Plan.Regions[climax.Region]).Where(ctx.Distance.ContainsKey).ToArray();
        var walk = climaxCells.Length == 0 ? [] : TraceWalk(ctx, climaxCells.MinBy(c => ctx.Distance[c])).ToHashSet();
        
        var onTheWay = walk
            .Where(ctx.Layout.Cells.ContainsKey)
            .Select(c => ctx.Layout.Cells[c])
            .ToHashSet();
        
        var safe = OrderObjectiveRooms(ctx, GetRoleRooms(ctx), [], original, progress, onTheWay, encounters.IsGated)
            .Select(room => ctx.GetFreeCells(room, inner: true).Where(c => ctx.Distance.ContainsKey(c) && ctx.IsClearAround(c) && !ctx.IsInWall(GetCenter(ctx, c).X, GetCenter(ctx, c).Y, 24))
                .OrderBy(c => walk.Contains(c) ? 0 : 1).ThenBy(c => Math.Abs(ctx.GetProgress(c) - progress)).ToArray())
            .FirstOrDefault(cells => cells.Length > 0);
        
        if (safe is not null)
        {
            script.Checkpoint = GetCenter(ctx, safe[0]);
        }
    }
}
