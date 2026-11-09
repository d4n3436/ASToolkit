using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Content;

internal sealed class EncounterPlacer
{
    private const float StartGap = 900;
    private const float CannonGap = 600;
    private const float HalfScreenWidth = 640;
    private const float HalfScreenHeight = 360;
    private const int AnchorTries = 12;
    private const int SpawnerTries = 8;
    private const float GoldenAngle = 2.39996f;
    private const float MonsterGap = 12;
    private const int MostClustered = 40;

    private readonly PlacementContext _ctx;
    private readonly EncounterModel _model;
    private readonly List<PointF> _spawners = [];
    private readonly IReadOnlyDictionary<EdgeKey, int> _spawnWalls;
    private readonly PointF _start;
    private readonly PointF[] _cannons;
    private readonly Dictionary<int, Cell[]> _floors = [];

    public EncounterPlacer(PlacementContext ctx, EncounterModel model, IReadOnlyDictionary<EdgeKey, int> spawnWalls)
    {
        _ctx = ctx;
        _model = model;
        _spawnWalls = spawnWalls;
        _start = ctx.Layout.Lattice.GetCenter(ctx.Start);
        _cannons = ctx.Map.Sprites
            .Where(sprite => sprite.Id == KnownObject.MountedCannon)
            .Select(sprite => new PointF(sprite.Position.X, sprite.Position.Y))
            .ToArray();
    }

    public void Place(IReadOnlyList<PlannedFight> fights)
    {
        foreach (var fight in fights.Where(fight => fight.Trigger is null || !Release(fight)))
        {
            Stand(fight.Monsters, fight.Rooms);
        }
    }

    private void Stand(IReadOnlyList<KnownObject> group, IReadOnlyList<int> rooms)
    {
        var left = group.ToList();
        for (int index = 0; index < rooms.Count && left.Count > 0; index++)
        {
            var free = GetFloor(rooms[index]);
            var placed = free.Length == 0 ? [] : PlaceCluster(left, rooms[index], free);
            while (placed.Count > 0)
            {
                left = left.Where((_, member) => !placed.Contains(member)).ToList();
                placed = left.Count == 0 ? [] : PlaceCluster(left, rooms[index], free);
            }
        }

        int full = 0;
        var floor = rooms.Count == 0 ? [] : _ctx.GetFreeCells(_ctx.Plan.Regions[rooms[0]]);
        foreach (var id in left)
        {
            while (full < rooms.Count && _ctx.Place(floor, id, dir: null, MapBuilder.MonsterPayload, army: 1, take: false) is null)
            {
                full++;
                floor = full < rooms.Count ? _ctx.GetFreeCells(_ctx.Plan.Regions[rooms[full]]) : [];
            }
        }
    }

    private Cell[] GetFloor(int room)
    {
        if (_floors.TryGetValue(room, out var floor))
            return floor;
        
        var cells = _ctx.GetFreeCells(_ctx.Plan.Regions[room]);
        var clear = cells
            .Where(cell => ContentPlacer.GetDistance(_ctx.Layout.Lattice.GetCenter(cell), _start) >= StartGap
                           && _cannons.All(cannon => ContentPlacer.GetDistance(_ctx.Layout.Lattice.GetCenter(cell), cannon) >= CannonGap))
            .ToArray();

        floor = _floors[room] = clear.Length > 0 ? clear : cells;

        return floor;
    }

    private HashSet<int> PlaceCluster(List<KnownObject> group, int room, Cell[] free)
    {
        var best = new List<(int Index, PointF Point)>();
        for (int attempt = 0; attempt < AnchorTries && best.Count < Math.Min(group.Count, MostClustered); attempt++)
        {
            var anchor = _ctx.Layout.Lattice.GetCenter(free[_ctx.Rng.Next(free.Length)]);
            var placed = new List<(int Index, PointF Point)>(MostClustered);
            for (int index = 0; index < Math.Min(group.Count, MostClustered); index++)
            {
                float radius = _model.Spacing.Value * 0.55f * MathF.Sqrt(index), angle = index * GoldenAngle;
                var point = new PointF(anchor.X + radius * MathF.Cos(angle), anchor.Y + radius * MathF.Sin(angle));
                var cell = _ctx.Layout.Lattice.GetCell(point.X, point.Y);
                if (_ctx.Layout.Cells.GetValueOrDefault(cell, -1) == room && _ctx.Distance.ContainsKey(cell) && IsApart(placed, point) && _ctx.IsVisible(point.X, point.Y)
                    && !_ctx.IsInWall(point.X, point.Y, 2) && _ctx.Occupancy.Fits(group[index], point.X, point.Y, 0))
                {
                    placed.Add((index, point));
                }
            }

            best = placed.Count > best.Count ? placed : best;
        }

        foreach ((int index, var point) in best)
        {
            _ctx.Map.Add(group[index], point.X, point.Y, 0, _ctx.Rng.Next(256), MapBuilder.MonsterPayload, 1, unique: false, source: SpriteSource.Model);
            _ctx.Occupancy.Add(group[index], point.X, point.Y, 0);
        }

        return best.Select(member => member.Index).ToHashSet();
    }

    private static bool IsApart(List<(int Index, PointF Point)> placed, PointF point)
    {
        foreach (var (_, other) in placed)
        {
            float x = other.X - point.X, y = other.Y - point.Y;
            if (x * x + y * y < MonsterGap * MonsterGap)
            {
                return false;
            }
        }

        return true;
    }

    private bool Release(PlannedFight fight)
    {
        var trigger = fight.Trigger!.Value;
        var center = _ctx.Layout.Lattice.GetCenter(_ctx.Layout.GetDoorwayCell(trigger.Link, trigger.Region));
        int radius = (int)MathF.Ceiling(_ctx.Layout.LinkEdges[trigger.Link].Max(edge => ContentPlacer.GetDistance(GetMiddle(edge), center)) + _ctx.Step);
        if (fight.Closet is not null)
        {
            return ReleaseFromCloset(fight, center, radius);
        }

        var spots = fight.Rooms
            .SelectMany(room => _ctx.GetFreeCells(_ctx.Plan.Regions[room], inner: true))
            .Select(free => (Cell: free, Point: _ctx.Layout.Lattice.GetCenter(free)))
            .Where(spot => MathF.Abs(spot.Point.X - center.X) >= HalfScreenWidth + radius + _ctx.Step || MathF.Abs(spot.Point.Y - center.Y) >= HalfScreenHeight + radius + _ctx.Step)
            .OrderBy(spot => ContentPlacer.GetDistance(spot.Point, center))
            .ToArray();

        if (spots.Length == 0)
        {
            return false;
        }

        var near = spots.Where(spot => ContentPlacer.GetDistance(spot.Point, center) <= _model.SpawnerReach.Value).DefaultIfEmpty(spots[0]).ToArray();
        foreach (var group in fight.Groups)
        {
            byte[] payload = SpawnerProgram.Create(group, (center.X, center.Y, radius), [center]).ToPayload();
            PointF? point = null;
            var clear = near
                .Where(spot => _spawners.All(other => ContentPlacer.GetDistance(other, spot.Point) >= _model.SpawnerSpacing.Value) && _ctx.Occupancy.HasRoomAround(spot.Point.X, spot.Point.Y))
                .Take(SpawnerTries)
                .ToArray();

            foreach (var spot in clear)
            {
                var placed = _ctx.Place([spot.Cell], KnownObject.Spawner, dir: 0, payload: payload, army: 1, take: false);
                if (placed is null) 
                    continue;
                
                point = new PointF(placed.Value.X, placed.Value.Y);
                break;
            }

            if (point is null)
            {
                var ordered = near.OrderByDescending(spot => _ctx.Occupancy.HasRoomAround(spot.Point.X, spot.Point.Y)).ToArray();
                var placed = _ctx.Place(ordered.Select(spot => spot.Cell).ToArray(), KnownObject.Spawner, dir: 0, payload: payload, army: 1, take: false);
                point = placed is null ? ordered[0].Point : new PointF(placed.Value.X, placed.Value.Y);
                if (placed is null)
                {
                    _ctx.Map.Add(KnownObject.Spawner, point.Value.X, point.Value.Y, 0, 0, payload, 1, unique: false, source: SpriteSource.Model);
                }
            }

            _spawners.Add(point.Value);
        }

        return true;
    }

    private bool ReleaseFromCloset(PlannedFight fight, PointF center, int radius)
    {
        int closet = fight.Closet!.Value;
        var link = _ctx.Plan.Links.FirstOrDefault(link => link.To == closet);
        if (link is null || !_spawnWalls.TryGetValue(_ctx.Layout.LinkEdges[link.Id][0], out int wallIndex))
        {
            return false;
        }

        var cells = _ctx.GetRegionCells(_ctx.Plan.Regions[closet]);
        for (int index = 0; index < fight.Groups.Count; index++)
        {
            var point = _ctx.Layout.Lattice.GetCenter(cells[index % cells.Count]);
            _spawners.Add(point);
            _ctx.Map.Add(KnownObject.Spawner, point.X, point.Y, 0, 0, SpawnerProgram.Create(fight.Groups[index], (center.X, center.Y, radius), [center]).ToPayload(), 1, unique: false,
                source: SpriteSource.Model);
        }

        // Break the wall when the player enters the trigger
        var wall = new SpawnerProgram
        {
            Actions = [new StackAction((int)ActionCode.StopStack, 0, 0, 0), new StackAction(15, 0, 0, 0), new StackAction((int)ActionCode.FlagmanTrigger, (int)center.X, (int)center.Y, radius)],
            Trailer = new byte[4]
        };
        _ctx.Map.Sprites[wallIndex] = _ctx.Map.Sprites[wallIndex] with { Payload = wall.ToPayload() };
        return true;
    }

    private PointF GetMiddle(EdgeKey edge)
    {
        var (a, b) = edge.Cells;
        var (first, second) = (_ctx.Layout.Lattice.GetCenter(a), _ctx.Layout.Lattice.GetCenter(b));
        return new PointF((first.X + second.X) / 2, (first.Y + second.Y) / 2);
    }
}
