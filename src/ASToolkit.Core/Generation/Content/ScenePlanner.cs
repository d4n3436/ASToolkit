using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Content;

internal sealed class ScenePlanner
{
    private const float CannonWalk = 3600;
    private const float CannonClearance = 180;
    private const float CannonLatest = 0.75f;
    private const float CannonNearEnd = 0.3f;
    private const float CannonEnd = 0.6f;
    private const float FarEnd = 0.85f;
    private const float KillZoneDistance = 1000;
    private const float BlastReach = 100;
    private const int SiteCells = 96;
    private const int CellsPerSite = 120;
    private const int HoleGap = 3;
    private static readonly float[] RingSteps = [-1, -0.5f, 0, 0.5f, 1];

    private readonly Random _rng;
    private readonly LevelBrief _brief;
    private readonly LayoutPlan _plan;
    private readonly FineLayout _layout;
    private readonly SceneGrammar _grammar;
    private Dictionary<Cell, int> _walk = [];
    private int _walkLength = 1;

    public ScenePlanner(Random rng, LevelBrief brief, LayoutPlan plan, FineLayout layout, SceneGrammar grammar)
    {
        _rng = rng;
        _brief = brief;
        _plan = plan;
        _layout = layout;
        _grammar = grammar;
    }

    public SitePlan PlanSites(int count, EconomyModel economy, Cell start, IReadOnlyDictionary<EdgeKey, EdgeKind> visibleEdges)
    {
        _walk = _layout.MeasureWalk(start, new HashSet<Cell>());
        _walkLength = Math.Max(1, _walk.Values.DefaultIfEmpty(0).Max());
        bool basement = _plan.Settings.WallSet == WallSet.Basement;
        var rooms = _plan.Regions
            .Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Pen && region != _plan.KeyRoom && region != _plan.TriggerRoom
                && _plan.HoldsFights(region) && (basement || _layout.GetRegionCells(region.Id).Count >= SiteCells))
            .ToArray();

        var lockedRooms = rooms.Where(_plan.LockedSiteRooms.Contains).ToArray();
        int lockedSites = lockedRooms.Length == 0 ? 0 : Math.Max(1, (int)MathF.Round(count / 3f));
        var inLocked = Enumerable.Range(0, lockedSites).Select(index => (index + 1) * count / (lockedSites + 1)).ToHashSet();
        float[] targets = Enumerable.Range(0, count).Select(_ => economy.SitePositions.Value[_rng.Next(economy.SitePositions.Value.Count)]).Order().ToArray();
        var plan = new SitePlan([], []);
        for (int index = 0; index < count; index++)
        {
            var candidates = (inLocked.Contains(index) ? lockedRooms : rooms)
                .OrderBy(room => Math.Abs(GetProgress(room) - targets[index]))
                .ThenBy(room => room.Id)
                .ToArray();

            var hole = basement ? candidates.Select(room => new { Room = room, Wall = FindHoleWall(room) }).FirstOrDefault(pair => pair.Wall is not null) : null;
            if (hole is not null)
            {
                plan.Holes[hole.Wall!.Value] = hole.Room;
            }
            else
            {
                var open = candidates.Where(room => plan.Sites.Count(other => other.Room == room) < Math.Max(1, _layout.GetRegionCells(room.Id).Count / CellsPerSite)).ToArray();
                if (open.Length > 0)
                {
                    plan.Sites.Add(new PlannedSite(open[0], open.Skip(1).Select(room => room.Id).ToArray()));
                }
            }
        }

        return plan;

        EdgeKey? FindHoleWall(PlanRegion room)
        {
            var cells = _layout.GetRegionCells(room.Id).ToHashSet();
            var walls = visibleEdges.Keys
                .Where(edge => IsHoleWall(edge) && IsHoleWall(StructureBuilder.GetPreviousEdge(edge)) && IsHoleWall(edge.Axis == 'v' ? new EdgeKey('v', edge.A + 1, edge.B) : new EdgeKey('u', edge.A, edge.B + 1)))
                .Where(edge => plan.Holes.Keys.All(other => other.Axis != edge.Axis || Math.Abs(other.A - edge.A) + Math.Abs(other.B - edge.B) > HoleGap))
                .OrderBy(edge => edge.Axis)
                .ThenBy(edge => edge.A)
                .ThenBy(edge => edge.B)
                .ToArray();

            return walls.Length == 0 ? null : walls[_rng.Next(walls.Length)];

            bool IsHoleWall(EdgeKey edge)
            {
                var (first, second) = edge.Cells;
                var (floor, outside) = edge.Axis == 'v' ? (second, first) : (first, second);
                return visibleEdges.GetValueOrDefault(edge) == EdgeKind.Wall && cells.Contains(floor) && _walk.ContainsKey(floor) && !_layout.Cells.ContainsKey(outside);
            }
        }
    }

    public PlacedCannon? PlaceCannon(PlacementContext ctx, LevelBudget budget)
    {
        if (budget.MountedCannons == 0)
        {
            return null;
        }

        var halls = _plan.Stations
            .Where(station => ctx.GetProgress(station) is >= 0.15f and <= 0.8f)
            .OrderByDescending(station => ctx.GetRegionCells(station).Any(cell => ctx.Distance.GetValueOrDefault(cell) * ctx.Step >= CannonWalk))
            .ThenByDescending(station => ctx.GetRegionCells(station).Count)
            .ToArray();

        foreach (bool strict in new[] { true, false })
        {
            float clear = strict ? CannonClearance : CannonClearance / 2;
            foreach (var hall in halls)
            {
                var axis = HallAxis.Measure(ctx, hall);
                var far = axis.GetFarPoint(FarEnd);
                var cells = ctx.GetFreeCells(hall, inner: true)
                    .Where(cell => ctx.Distance.GetValueOrDefault(cell) * ctx.Step >= CannonWalk && ctx.GetProgress(cell) <= CannonLatest && (!strict || axis.GetEntryShare(cell) >= CannonNearEnd))
                    .ToArray();

                _rng.Shuffle(cells);
                foreach (var cell in cells.OrderBy(cell => MathF.Abs(axis.GetEntryShare(cell) - CannonEnd)))
                {
                    var point = _layout.Lattice.GetCenter(cell);
                    var ring = RingSteps.SelectMany(dx => RingSteps.Select(dy => new PointF(point.X + dx * clear, point.Y + dy * clear * 0.7f))).ToArray();
                    if (strict && ContentPlacer.GetDistance(point, far) < KillZoneDistance
                        || !ring.All(spot => _layout.Cells.ContainsKey(GetCell(spot)) && ctx.IsFree(GetCell(spot)) && !ctx.IsInWall(spot.X, spot.Y, 8)))
                    {
                        continue;
                    }

                    byte dir = (byte)(_rng.Next(2) == 0 ? 0 : 128);
                    ctx.Map.Add(KnownObject.MountedCannon, point.X, point.Y, 0, dir, unique: false);
                    ctx.Occupancy.Add(KnownObject.MountedCannon, point.X, point.Y, 0);
                    ctx.Taken.UnionWith(ring.Select(GetCell));
                    return new PlacedCannon(hall, point, dir, far);
                }
            }
        }

        return null;
    }

    public SceneRequest[] GetRequests(SitePlan sites, BreakerSpot? breaker, PlacedCannon? cannon, float generatorsPerRoom)
    {
        var requests = new List<SceneRequest>();
        if (_plan is { Gate: GateKind.Fences, KeyRoom: not null })
        {
            var rooms = _plan.GeneratorRooms.Count > 0 ? _plan.GeneratorRooms.Select(entry => entry.Room).ToArray() : [_plan.KeyRoom];
            int count = Math.Max(rooms.Length, (int)MathF.Round(rooms.Length * generatorsPerRoom));
            requests.AddRange(Enumerable.Range(0, count).Select(index => new SceneRequest(rooms[index % rooms.Length].Id, KnownObject.Generator, null, 0, [])));
        }

        if (_plan.Settings.WallSet == WallSet.Concrete)
        {
            var big = _plan.Regions
                .Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom && _layout.GetRegionCells(region.Id).Count >= _grammar.RackCells)
                .ToArray();

            _rng.Shuffle(big);
            requests.AddRange(big.Take(_grammar.Racks[_rng.Next(_grammar.Racks.Length)]).Select(room => new SceneRequest(room.Id, KnownObject.Missile, null, 0, [])));
            requests.AddRange(sites.Sites.Select(site => new SceneRequest(site.Room.Id, KnownObject.DynamiteSite, null, 0, site.Fallbacks)));
        }

        int breakerRoom = breaker is null ? -1 : _layout.Cells.GetValueOrDefault(new Cell(breaker.Wall.A - 1, breaker.Wall.B), -1);
        if (breakerRoom >= 0)
        {
            requests.Add(new SceneRequest(breakerRoom, KnownObject.Breaker, breaker!.Position, 0, []));
        }

        if (cannon is not null)
        {
            requests.Add(new SceneRequest(cannon.Hall.Id, KnownObject.MountedCannon, cannon.Point, cannon.Dir, []));
        }

        return requests.ToArray();
    }

    public void StandBasementSites(MapBuilder map, SitePlan sites, int generators)
    {
        if (_plan.Settings.WallSet == WallSet.Basement)
        {
            foreach (var site in sites.Sites)
            {
                var cells = _layout.GetRegionCells(site.Room.Id);
                var middle = _layout.Lattice.GetCenter(cells[cells.Count / 2]);
                map.Add(KnownObject.DynamiteSite, middle.X, middle.Y, 0, 0, unique: false);
            }
        }

        int left = sites.Holes.Count;
        foreach (var (edge, room) in sites.Holes)
        {
            ((float hx, float hy), bool slash) = StructureBuilder.GetBasementPosition(_layout.Lattice, edge);
            int side = slash ? 1 : -1;
            map.Add(KnownObject.DynamiteSite, hx + _grammar.Site.X * side, hy + _grammar.Site.Y, 0, 0, unique: false);
            int builds = (generators + left - 1) / left--;
            generators -= builds;
            if (builds <= 0)
                continue;
            
            var trigger = GetSiteTrigger(room);
            var program = SpawnerProgram.Create(Enumerable.Range(0, builds).Select(_ => _grammar.DrawEndlessGenerator(_rng, _brief.Level)), (trigger.X, trigger.Y, _grammar.TriggerRadius), []);
            map.Add(KnownObject.Spawner, hx + _grammar.Spawner.X * side, hy + _grammar.Spawner.Y, _grammar.SpawnerZ, 0, program.ToPayload(), 1, unique: false);
        }
    }

    public void StackEndlessGenerators(MapBuilder map, int generators)
    {
        var pads = map.Sprites.Where(sprite => sprite.Id == KnownObject.TeleportPad).Select(sprite => new PointF(sprite.Position.X, sprite.Position.Y)).ToArray();
        foreach (var site in map.Sprites.Where(sprite => sprite.Id == KnownObject.DynamiteSite).Select(sprite => new PointF(sprite.Position.X, sprite.Position.Y)).ToArray())
        {
            var near = pads.Where(pad => ContentPlacer.GetDistance(pad, site) < BlastReach - _grammar.GeneratorRise[1]).ToArray();
            if (generators <= 0 || near.Length == 0)
            {
                continue;
            }

            var pad = near.MinBy(pad => ContentPlacer.GetDistance(pad, site));
            for (int count = Math.Min(generators, _rng.Next(_grammar.GeneratorBatch[0], _grammar.GeneratorBatch[1] + 1)); count > 0; count--, generators--)
            {
                float rise = _grammar.GeneratorRise[0] + _rng.NextSingle() * (_grammar.GeneratorRise[1] - _grammar.GeneratorRise[0]);
                map.Add(_grammar.DrawEndlessGenerator(_rng, _brief.Level), pad.X + _grammar.GeneratorJitter * (2 * _rng.NextSingle() - 1), pad.Y - rise, 0, 0, new byte[8], 1, unique: false);
            }
        }
    }

    private PointF GetSiteTrigger(PlanRegion room)
    {
        var key = _plan.GeneratorRooms
            .Select(entry => entry.Room)
            .Append(_plan.KeyRoom)
            .OfType<PlanRegion>()
            .Where(other => other.Phase == room.Phase - 1)
            .MaxBy(GetProgress);

        var target = key ?? room;
        return _layout.Lattice.GetCenter(_layout.GetDoorwayCell(_plan.Links.FirstOrDefault(link => link.To == target.Id)?.Id ?? -1, target.Id));
    }

    private float GetProgress(PlanRegion room) =>
        _layout.GetRegionCells(room.Id).Where(_walk.ContainsKey).Select(cell => _walk[cell] / (float)_walkLength).DefaultIfEmpty(0).Min();

    private Cell GetCell(PointF point) => _layout.Lattice.GetCell(point.X, point.Y);
}

internal sealed record SitePlan(List<PlannedSite> Sites, Dictionary<EdgeKey, PlanRegion> Holes);

internal readonly record struct PlannedSite(PlanRegion Room, int[] Fallbacks);

internal sealed record PlacedCannon(PlanRegion Hall, PointF Point, byte Dir, PointF Far);
