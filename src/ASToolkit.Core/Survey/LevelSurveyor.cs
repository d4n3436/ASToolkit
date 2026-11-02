using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the survey of a level, its places, connections and measures.
/// </summary>
public sealed class LevelSurveyor
{
    private const float MaxOpeningPixels = 240;
    private const float MaxStubPixels = 300;
    private const float MinPlacePixels = 4 * 40 * 56;
    private const float PocketPixels = 50000;
    private const float MaxClosetPixels = 60 * 40 * 56;
    private const float MinRoomPixels = 260;
    private const float MinCorridorPixels = 400;
    private const float RoofReach = 0.8f;
    private const float NookPixels = 10 * 40 * 56;
    private const float CorridorWidthPixels = 150;
    private const float LongRatio = 2.5f;
    private const int GroupReach = 130;
    private const int NearReach = 400;
    private const int OffRoutePixels = 150;

    private static readonly HashSet<KnownObject> Skipped =
    [
        KnownObject.SmallShadow, KnownObject.EmbeddedShadow, KnownObject.ZDummy, KnownObject.FlyingGibs, KnownObject.RockDebris, KnownObject.BigExplosion,
        KnownObject.ExplosionSpark, KnownObject.BlastMark, KnownObject.Crate, KnownObject.WallInscriptions, KnownObject.WallObjects, KnownObject.BrokenKeypad
    ];

    private readonly MapResourceBuilder _map;
    private readonly LevelScriptSummary _script;
    private readonly RouteAnalysis _analysis;
    private readonly ObjectTable _objects;
    private readonly RouteGrid _grid;
    private readonly Lattice _lattice;
    private readonly float _step;
    private readonly int _tour;
    private readonly List<(int A, int B)> _walkable = [];
    private PlaceSegmentation _segments = null!;
    private int[] _number = [];
    private PlaceDraft[] _places = [];
    private List<(PlaceConnection Connection, List<EdgeKey> Edges)> _connections = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelSurveyor"/> class.
    /// </summary>
    /// <param name="map">The map data.</param>
    /// <param name="script">The level script.</param>
    /// <param name="analysis">The route info.</param>
    /// <param name="objects">The game objects.</param>
    public LevelSurveyor(MapResourceBuilder map, LevelScriptSummary script, RouteAnalysis analysis, ObjectTable objects)
    {
        _map = map;
        _script = script;
        _analysis = analysis;
        _objects = objects;
        _grid = analysis.Grid;
        _lattice = _grid.Lattice;
        _step = analysis.Step;
        _tour = Math.Max(1, analysis.RoutePixels);
    }

    private float CellArea => 2 * _lattice.StepX * _lattice.StepY;

    /// <summary>
    /// Surveys a level.
    /// </summary>
    public static LevelSurvey Survey(MapResourceBuilder map, LevelScriptSummary script, RouteAnalysis analysis, ObjectTable objects) =>
        new LevelSurveyor(map, script, analysis, objects).Survey();

    /// <summary>
    /// Surveys a level with the given measurements.
    /// </summary>
    public static LevelSurvey Survey(MapResourceBuilder map, LevelScriptSummary script, RouteAnalysis analysis, ObjectTable objects, LevelMeasurement measurement) =>
        new LevelSurveyor(map, script, analysis, objects).Survey(measurement);

    /// <summary>
    /// Surveys the level.
    /// </summary>
    public LevelSurvey Survey() => Survey(LevelMeasurer.Measure(_map, _script, _analysis, _objects));

    /// <summary>
    /// Surveys the level with the given measurements.
    /// </summary>
    public LevelSurvey Survey(LevelMeasurement measurement)
    {
        var setting = FindSetting();
        bool openPlan = setting != LevelTheme.Lab || !_grid.Edges.Values.Any(kind => kind is EdgeKind.Door or EdgeKind.Doorway or EdgeKind.ShutDoor);
        var segmenter = new PlaceSegmenter((int)(MaxOpeningPixels / _step), (int)(MaxStubPixels / _step), (int)MathF.Ceiling(MinPlacePixels / CellArea),
            (int)(MaxClosetPixels / CellArea), (int)MathF.Round(MinRoomPixels / _step), (int)MathF.Round(MinCorridorPixels / _step), (int)(PocketPixels / CellArea));
        _segments = segmenter.Segment(_grid, _analysis.Tour.ToHashSet(), FindRoofedCells(), openPlan, FindContentCells(withDoors: true), FindContentCells(withDoors: false));

        BuildPlaces(FindZones(measurement.Style));
        BuildConnections(measurement.Style);
        RelabelDeadEnds();
        int loops = DescribeGraph();
        var landmarks = DescribeLandmarks(measurement.Style);
        var encounters = DescribeEncounters();
        var items = DescribeItems(encounters);
        AssignRoles(landmarks, encounters);
        var gates = DescribeGates(measurement.Style, landmarks);
        return new LevelSurvey
        {
            Name = _analysis.Name,
            Theme = _analysis.Theme,
            Setting = setting,
            Style = measurement.Style,
            Lattice = _lattice,
            Places = _places.Select(place => place.ToPlace()).ToArray(),
            Connections = _connections.Select(entry => entry.Connection).ToArray(),
            Signature = MeasureSignature(measurement, loops, encounters, items, gates),
            Route = BuildRoute(),
            Gates = gates,
            Landmarks = landmarks,
            Encounters = encounters,
            Items = items,
            Branches = DescribeBranches(),
            Builds = DescribeBuilds()
        };
    }

    private LevelBuild[] DescribeBuilds()
    {
        var builds = new List<LevelBuild>();
        foreach (var sprite in _map.Sprites)
        {
            if (sprite.Id == KnownObject.Spawner || sprite.Payload.Length == 0 || !SpriteProgram.TryParse(sprite.Payload, _objects.IsAlienShooter2, out var program))
            {
                continue;
            }

            for (int index = 0; index < program.StackCount; index++)
            {
                AddBuilds(program.GetStack(index), builds);
            }
        }

        return builds.ToArray();
    }

    private void AddBuilds(ReadOnlySpan<StackAction> stack, List<LevelBuild> builds)
    {
        int? trigger = null;
        foreach (var action in stack)
        {
            if (action.Act == (int)ActionCode.FlagmanTrigger)
            {
                trigger = GetPlaceNear(action.Var1, action.Var2);
                break;
            }
        }

        if (trigger is null)
        {
            return;
        }

        foreach (var action in stack)
        {
            if (action.Act == (int)ActionCode.BuildUnit && action.Var2 != 0)
            {
                builds.Add(new LevelBuild(trigger.Value, (KnownObject)action.Var1, GetPlaceNear(action.Var2, action.Var3)));
            }
        }
    }

    private HashSet<Cell> FindContentCells(bool withDoors)
    {
        var content = new HashSet<Cell>(_analysis.Tour);
        foreach (var sprite in _map.Sprites)
        {
            if (!KnownObject.Floors.Contains(sprite.Id) && !KnownObject.LabWalls.Contains(sprite.Id) && !KnownObject.MilitaryWalls.Contains(sprite.Id) && !Skipped.Contains(sprite.Id) && (withDoors || !sprite.Id.IsDoor))
            {
                content.Add(_lattice.GetCell(sprite.Position.X, sprite.Position.Y));
            }
        }

        return content;
    }

    private HashSet<Cell> FindRoofedCells()
    {
        var roofed = new HashSet<Cell>();
        if (_analysis.Theme == LevelTheme.Military)
        {
            return roofed;
        }

        var tiles = _map.Sprites
            .Where(sprite => sprite is { Id: KnownObject.Earth, Position.Z: > 40 })
            .ToLookup(sprite => ((int)MathF.Floor(sprite.Position.X / (2 * Lattice.LabStep.X)), (int)MathF.Floor((sprite.Position.Y - sprite.Position.Z) / (2 * Lattice.LabStep.Y))));

        foreach (var cell in _grid.Cells)
        {
            (float x, float y) = _lattice.GetCenter(cell);
            int column = (int)MathF.Floor(x / (2 * Lattice.LabStep.X)), row = (int)MathF.Floor(y / (2 * Lattice.LabStep.Y));
            bool covered = false;
            for (int dx = -1; dx <= 1 && !covered; dx++)
            {
                for (int dy = -1; dy <= 1 && !covered; dy++)
                {
                    covered = tiles[(column + dx, row + dy)].Any(tile => MathF.Abs(tile.Position.X - x) / Lattice.LabStep.X + MathF.Abs(tile.Position.Y - tile.Position.Z - y) / Lattice.LabStep.Y <= RoofReach);
                }
            }

            if (covered)
            {
                roofed.Add(cell);
            }
        }

        return roofed;
    }

    private Dictionary<Cell, GateZone> FindZones(LevelStyle style)
    {
        var floor = _segments.Floor;
        var start = _analysis.Tour[0];
        var zones = new Dictionary<Cell, GateZone>();
        var open = floor.Flood([start], edge => IsWall(edge) || Is(edge, EdgeKind.SecretWall));
        foreach (var cell in floor)
        {
            zones[cell] = open.Contains(cell) ? GateZone.Open : GateZone.Secret;
        }

        if (style == LevelStyle.LockedDoors)
        {
            Mark(floor.Flood([start], edge => IsWall(edge) || Is(edge, EdgeKind.SecretWall) || Is(edge, EdgeKind.Door)), GateZone.Locked);
        }

        var blackout = _script.Blackout;
        if (style == LevelStyle.Blackout && blackout is not null)
        {
            var from = FindNearest(blackout.Value.Center.X, blackout.Value.Center.Y, floor);
            if (from is not null)
            {
                Mark(floor.Flood([from.Value], edge => IsWall(edge) || Is(edge, EdgeKind.SecretWall) || Is(edge, EdgeKind.Door)), GateZone.Blackout);
            }
        }

        if (_grid.FenceEdges.Count > 0)
        {
            Mark(floor.Flood([start], edge => IsWall(edge) || Is(edge, EdgeKind.SecretWall) || _grid.FenceEdges.Contains(edge)), GateZone.Fenced);
        }

        return zones;

        void Mark(HashSet<Cell> reached, GateZone gated)
        {
            foreach (var cell in open.Where(cell => !reached.Contains(cell)))
            {
                zones[cell] = gated;
            }
        }
    }

    private bool Is(EdgeKey edge, EdgeKind kind) => _grid.Edges.TryGetValue(edge, out var found) && found == kind;

    private bool IsWall(EdgeKey edge) => _grid.Edges.TryGetValue(edge, out var kind) && kind.BlocksWalking;

    private void BuildPlaces(Dictionary<Cell, GateZone> zones)
    {
        var regions = _segments.Places;
        var tour = _analysis.Tour;
        int[] first = Enumerable.Repeat(int.MaxValue, regions.Count).ToArray();
        int[] visits = new int[regions.Count];
        int last = -1;
        for (int tourIndex = 0; tourIndex < tour.Count; tourIndex++)
        {
            if (!_segments.PlaceOf.TryGetValue(tour[tourIndex], out int region))
            {
                continue;
            }

            first[region] = Math.Min(first[region], tourIndex);
            if (region != last)
            {
                visits[region]++;
                last = region;
            }
        }

        Func<EdgeKey, bool> closed = edge => IsWall(edge) || Is(edge, EdgeKind.SecretWall);
        var fromStart = MeasureWalks(_grid.Cells, [tour[0]], closed, out _);
        var fromTour = MeasureWalks(_grid.Cells, tour.Distinct(), closed, out var previous);

        int[] order = Enumerable.Range(0, regions.Count)
            .OrderBy(region => first[region])
            .ThenBy(region => regions[region].Min(cell => fromTour.GetValueOrDefault(cell, int.MaxValue)))
            .ThenBy(region => regions[region].Min(cell => fromStart.GetValueOrDefault(cell, int.MaxValue)))
            .ToArray();

        _number = new int[regions.Count];
        for (int orderIndex = 0; orderIndex < order.Length; orderIndex++)
        {
            _number[order[orderIndex]] = orderIndex + 1;
        }

        _places = order.Select(region =>
        {
            var cells = regions[region];
            float extentU = (cells.Max(cell => cell.U) - cells.Min(cell => cell.U) + 1) * _step;
            float extentV = (cells.Max(cell => cell.V) - cells.Min(cell => cell.V) + 1) * _step;
            float area = cells.Count * CellArea;
            var zone = cells.GroupBy(cell => zones[cell]).MaxBy(group => group.Count())!.Key;
            int? walk = cells.Where(fromStart.ContainsKey).Select(cell => (int?)fromStart[cell]).Min();
            bool entered = first[region] != int.MaxValue;
            int? detour = null;
            int? enteredFrom = null;
            var nearest = cells.Where(fromTour.ContainsKey).OrderBy(cell => fromTour[cell]).Select(cell => (Cell?)cell).FirstOrDefault();
            if (!entered && nearest is not null)
            {
                detour = (int)MathF.Round(2 * fromTour[nearest.Value] * _step);
                enteredFrom = FindEntrance(nearest.Value, region, previous);
            }

            var center = GetCenter(cells);
            return new PlaceDraft
            {
                Id = _number[region],
                Cells = cells,
                Kind = GetKind(cells, extentU, extentV, area, zone),
                Zone = zone,
                Center = new Point((int)center.X, (int)center.Y),
                AreaPixels = (int)area,
                LengthPixels = (int)MathF.Max(extentU, extentV),
                WidthPixels = (int)MathF.Min(extentU, extentV),
                WalkFromStart = walk is null ? null : (int)MathF.Round(walk.Value * _step),
                FirstVisit = entered ? ToPixels(first[region]) : null,
                Visits = visits[region],
                Detour = detour,
                EnteredFrom = enteredFrom,
                BlockedShare = MathF.Round(cells.Count(_grid.BlockedCells.Contains) / (float)cells.Count, 2)
            };
        }).ToArray();

        Tag(FindPlace(tour[0]), PlaceTags.Start);
    }

    private int? FindEntrance(Cell target, int region, Dictionary<Cell, Cell> previous)
    {
        var cell = target;
        while (previous.TryGetValue(cell, out var before))
        {
            if (_segments.PlaceOf.TryGetValue(before, out int other) && other != region)
            {
                return _number[other];
            }

            cell = before;
        }

        return null;
    }

    private PlaceKind GetKind(List<Cell> cells, float extentU, float extentV, float area, GateZone zone)
    {
        if (zone == GateZone.Secret && area < 4 * NookPixels)
        {
            bool spawns = _analysis.Elements.Any(element => (element.Kind is ElementKind.Spawner or ElementKind.EndlessGenerator && cells.Contains(element.Cell))
                                                            || (element.Kind is ElementKind.SpawnWall or ElementKind.MonsterHole && IsBeside(element, cells)));
            return spawns ? PlaceKind.SpawnCloset : PlaceKind.SecretCloset;
        }

        if (area < NookPixels)
        {
            return PlaceKind.Nook;
        }

        float width = MathF.Min(extentU, extentV);
        float length = MathF.Max(extentU, extentV);
        float fill = area / (extentU * extentV * CellArea / (_step * _step));
        if (length / width >= LongRatio)
        {
            return width <= CorridorWidthPixels ? PlaceKind.Corridor : PlaceKind.Hall;
        }

        if (fill < 0.45f && MeasureThickness(cells) * _step <= CorridorWidthPixels)
        {
            return PlaceKind.Corridor;
        }

        return fill < 0.5f && area > 20 * NookPixels ? PlaceKind.Yard : PlaceKind.Room;
    }

    private static int MeasureThickness(List<Cell> cells)
    {
        var set = cells.ToHashSet();
        var depth = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var cell in cells)
        {
            foreach (var (neighbor, _) in EdgeKey.GetNeighbors(cell))
            {
                if (!set.Contains(neighbor))
                {
                    depth[cell] = 1;
                    queue.Enqueue(cell);
                    break;
                }
            }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var (neighbor, _) in EdgeKey.GetNeighbors(cell))
            {
                if (set.Contains(neighbor) && !depth.ContainsKey(neighbor))
                {
                    depth[neighbor] = depth[cell] + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return 2 * depth.Values.DefaultIfEmpty(0).Max();
    }

    private bool IsBeside(RouteElement element, List<Cell> cells)
    {
        var at = _lattice.GetCell(element.X, element.Y);
        return cells.Any(cell => Math.Abs(cell.U - at.U) <= 2 && Math.Abs(cell.V - at.V) <= 2);
    }

    private void BuildConnections(LevelStyle style)
    {
        bool locking = style is LevelStyle.LockedDoors or LevelStyle.Blackout;
        var breakables = _map.Sprites
            .Where(sprite => sprite.Id is KnownObject.BreakableWall or KnownObject.BreakableWall2 or KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack
                or KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole)
            .ToArray();

        var found = new Dictionary<(int A, int B, ConnectionKind Kind), List<EdgeKey>>();
        foreach (var cell in _segments.Floor)
        {
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (edge.Cells.First != cell || _segments.Flanks.Contains(edge) || !_segments.PlaceOf.TryGetValue(neighbor, out int other) || other == _segments.PlaceOf[cell])
                {
                    continue;
                }

                ConnectionKind? kind = _grid.Edges.TryGetValue(edge, out var edgeKind)
                    ? edgeKind switch
                    {
                        EdgeKind.Door => locking ? ConnectionKind.LockedDoor : ConnectionKind.Door,
                        EdgeKind.Doorway => ConnectionKind.Doorway,
                        EdgeKind.ShutDoor => ConnectionKind.JammedDoor,
                        EdgeKind.SecretWall => GetBreakableKind(edge, breakables),
                        EdgeKind.BrokenWall => ConnectionKind.BrokenWall,
                        EdgeKind.FenceFrame => _grid.FenceEdges.Contains(edge) ? ConnectionKind.Fence : ConnectionKind.FenceFrame,
                        _ => null
                    }
                    : ConnectionKind.Opening;

                if (kind is null)
                {
                    continue;
                }

                int first = _number[_segments.PlaceOf[cell]], second = _number[other];
                var key = (Math.Min(first, second), Math.Max(first, second), kind.Value);
                if (!found.TryGetValue(key, out var edges))
                {
                    found[key] = edges = [];
                }

                edges.Add(edge);
            }
        }

        var runs = new List<(int A, int B, ConnectionKind Kind, List<EdgeKey> Edges)>();
        foreach (((int first, int second, var kind), var edges) in found)
        {
            foreach (var run in SplitRuns(edges))
            {
                runs.Add((first, second, kind, run));
                if (kind is not (ConnectionKind.JammedDoor or ConnectionKind.SecretWall or ConnectionKind.SpawnWall or ConnectionKind.MonsterHole))
                {
                    _walkable.Add((first, second));
                }
            }
        }

        _connections = runs
            .OrderBy(run => run.A)
            .ThenBy(run => run.B)
            .ThenBy(run => run.Kind)
            .ThenBy(run => run.Edges.Min(edge => (edge.A, edge.B)))
            .Select((run, index) => (new PlaceConnection { Id = index + 1, A = run.A, B = run.B, Kind = run.Kind, Edges = EdgeRun.Create(run.Edges) }, run.Edges))
            .ToList();

        foreach (var (edge, _) in _grid.Edges.Where(pair => pair.Value == EdgeKind.ShutDoor))
        {
            var (one, two) = edge.Cells;
            if (_segments.Floor.Contains(one) != _segments.Floor.Contains(two))
            {
                Tag(FindPlace(_segments.Floor.Contains(one) ? one : two), PlaceTags.FakeDoor);
            }
        }
    }

    private void RelabelDeadEnds()
    {
        foreach (var place in _places.Where(place => place.Kind == PlaceKind.Corridor))
        {
            if (_walkable.Distinct().Count(pair => pair.A == place.Id || pair.B == place.Id) < 2)
            {
                place.Kind = PlaceKind.Room;
            }
        }
    }

    private ConnectionKind GetBreakableKind(EdgeKey edge, MergedMapSprite[] breakables)
    {
        var middle = GetMiddle(edge);
        var piece = breakables.MinBy(sprite => GetDistance(sprite.Position.X, sprite.Position.Y, middle.X, middle.Y));
        return piece?.Id switch
        {
            KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack => ConnectionKind.SpawnWall,
            KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole => ConnectionKind.MonsterHole,
            _ => ConnectionKind.SecretWall
        };
    }

    private static List<List<EdgeKey>> SplitRuns(List<EdgeKey> edges)
    {
        var runs = new List<List<EdgeKey>>();
        var left = new HashSet<EdgeKey>(edges);
        while (left.Count > 0)
        {
            var seed = left.OrderBy(edge => (edge.Axis, edge.A, edge.B)).First();
            left.Remove(seed);
            var run = new List<EdgeKey> { seed };
            for (int runIndex = 0; runIndex < run.Count; runIndex++)
            {
                var edge = run[runIndex];
                foreach (var other in left.Where(other => Math.Abs(other.A - edge.A) + Math.Abs(other.B - edge.B) <= 2).ToArray())
                {
                    left.Remove(other);
                    run.Add(other);
                }
            }

            runs.Add(run);
        }

        return runs;
    }

    private PointF GetMiddle(EdgeKey edge) => edge.Axis == 'v' ? _lattice.ToWorld(edge.A + 0.5f, edge.B) : _lattice.ToWorld(edge.A, edge.B + 0.5f);

    private RouteVisit[] BuildRoute()
    {
        var tour = _analysis.Tour;
        var stays = new List<(int Place, int Enter, int Leave)>();
        for (int tourIndex = 0; tourIndex < tour.Count; tourIndex++)
        {
            int place = _segments.PlaceOf.TryGetValue(tour[tourIndex], out int region) ? _number[region] : 0;
            if (stays.Count > 0 && stays[^1].Place == place)
            {
                stays[^1] = stays[^1] with { Leave = tourIndex };
            }
            else
            {
                stays.Add((place, tourIndex, tourIndex));
            }
        }

        for (int stayIndex = stays.Count - 2; stayIndex >= 1; stayIndex--)
        {
            if (stays[stayIndex].Leave - stays[stayIndex].Enter < 2 && stays[stayIndex - 1].Place == stays[stayIndex + 1].Place)
            {
                stays[stayIndex - 1] = stays[stayIndex - 1] with { Leave = stays[stayIndex + 1].Leave };
                stays.RemoveRange(stayIndex, 2);
                stayIndex = Math.Min(stayIndex, stays.Count - 1);
            }
        }

        return stays.Select(stay => new RouteVisit(stay.Place, ToPixels(stay.Enter), ToPixels(stay.Leave), GetPhase(stay.Enter))).ToArray();
    }

    private RoutePhase GetPhase(int tourIndex)
    {
        float at = tourIndex * _step;
        float walked = 0;
        var phase = RoutePhase.Free;
        foreach (var leg in _analysis.Legs)
        {
            phase = leg.Phase;
            walked += leg.Pixels;
            if (at < walked)
            {
                break;
            }
        }

        return phase;
    }

    private int DescribeGraph()
    {
        int count = _places.Length;
        var adjacency = GetAdjacency();
        int components = 0;
        bool[] seen = new bool[count + 1];
        for (int place = 1; place <= count; place++)
        {
            if (seen[place])
            {
                continue;
            }

            components++;
            var stack = new Stack<int>([place]);
            seen[place] = true;
            while (stack.Count > 0)
            {
                foreach (int neighbor in adjacency[stack.Pop()].Where(neighbor => !seen[neighbor]))
                {
                    seen[neighbor] = true;
                    stack.Push(neighbor);
                }
            }
        }

        var cuts = FindCutPlaces(adjacency, count);
        var onLoops = FindLoopPlaces(adjacency);
        foreach (var place in _places)
        {
            int degree = adjacency[place.Id].Count;
            if (degree == 1)
            {
                Tag(place, PlaceTags.DeadEnd);
            }

            if (degree >= 4)
            {
                Tag(place, PlaceTags.Hub);
            }

            if (cuts.Contains(place.Id))
            {
                Tag(place, PlaceTags.ChokePoint);
            }

            if (onLoops.Contains(place.Id))
            {
                Tag(place, PlaceTags.OnLoop);
            }

            if (place.FirstVisit is null)
            {
                Tag(place, PlaceTags.OffRoute);
            }
        }

        return _walkable.Distinct().Count() - count + components;
    }

    private HashSet<int>[] GetAdjacency()
    {
        var adjacency = Enumerable.Range(0, _places.Length + 1).Select(_ => new HashSet<int>()).ToArray();
        foreach ((int first, int second) in _walkable)
        {
            adjacency[first].Add(second);
            adjacency[second].Add(first);
        }

        return adjacency;
    }

    private LevelBranch[] DescribeBranches()
    {
        var adjacency = GetAdjacency();
        var branches = new List<LevelBranch>();
        var done = new HashSet<int>();
        int floor = Math.Max(1, _places.Sum(place => place.AreaPixels));
        foreach (var place in _places.Where(place => place.FirstVisit is null && place.Zone != GateZone.Secret))
        {
            if (!done.Add(place.Id))
            {
                continue;
            }

            var members = new List<int> { place.Id };
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                members.AddRange(adjacency[members[memberIndex]].Where(neighbor => GetPlace(neighbor).FirstVisit is null && done.Add(neighbor)).Order().ToArray());
            }

            int[] entrances = members.SelectMany(member => adjacency[member]).Where(neighbor => GetPlace(neighbor).FirstVisit is not null).Distinct().Order().ToArray();
            int area = members.Sum(member => GetPlace(member).AreaPixels);
            int detour = members.Max(member => GetPlace(member).Detour ?? 0);
            branches.Add(new LevelBranch(members.Order().ToArray(), entrances, MathF.Round(area / (float)floor, 3), detour));
        }

        return branches.ToArray();
    }

    private static HashSet<int> FindCutPlaces(HashSet<int>[] adjacency, int count)
    {
        var cuts = new HashSet<int>();
        int[] order = new int[count + 1];
        int[] low = new int[count + 1];
        int time = 0;
        for (int place = 1; place <= count; place++)
        {
            if (order[place] == 0)
            {
                Visit(place, 0);
            }
        }

        return cuts;

        void Visit(int place, int parent)
        {
            order[place] = low[place] = ++time;
            int children = 0;
            foreach (int neighbor in adjacency[place])
            {
                if (order[neighbor] == 0)
                {
                    children++;
                    Visit(neighbor, place);
                    low[place] = Math.Min(low[place], low[neighbor]);
                    if (parent != 0 && low[neighbor] >= order[place])
                    {
                        cuts.Add(place);
                    }
                }
                else if (neighbor != parent)
                {
                    low[place] = Math.Min(low[place], order[neighbor]);
                }
            }

            if (parent == 0 && children > 1)
            {
                cuts.Add(place);
            }
        }
    }

    private HashSet<int> FindLoopPlaces(HashSet<int>[] adjacency)
    {
        var onLoops = new HashSet<int>();
        foreach (var link in _walkable.GroupBy(link => link))
        {
            (int first, int second) = link.Key;
            if (link.Count() > 1 || IsConnectedWithout(first, second))
            {
                onLoops.Add(first);
                onLoops.Add(second);
            }
        }

        return onLoops;

        bool IsConnectedWithout(int first, int second)
        {
            var seen = new HashSet<int> { first };
            var stack = new Stack<int>([first]);
            while (stack.Count > 0)
            {
                int place = stack.Pop();
                foreach (int neighbor in adjacency[place])
                {
                    if ((place == first && neighbor == second) || (place == second && neighbor == first))
                    {
                        continue;
                    }

                    if (neighbor == second)
                    {
                        return true;
                    }

                    if (seen.Add(neighbor))
                    {
                        stack.Push(neighbor);
                    }
                }
            }

            return false;
        }
    }

    private LevelLandmark[] DescribeLandmarks(LevelStyle style)
    {
        var landmarks = new List<LevelLandmark>();
        var elements = _analysis.Elements;
        var sites = elements.Where(element => element.Kind == ElementKind.DynamiteSite).ToArray();
        var charges = elements.Where(element => element is { Kind: ElementKind.Pickup, Category: ItemCategory.Dynamite }).ToArray();
        var triggers = elements.Where(element => element is { Kind: ElementKind.Spawner, Program.Trigger: not null }).Select(element => element.Program!.Trigger!.Value).ToArray();
        var gating = _analysis.Gating;

        var start = _map.Sprites.FirstOrDefault(sprite => sprite.Id is KnownObject.PlayerStart or KnownObject.PlayerStartFemaleLegs);
        if (start is not null)
        {
            landmarks.Add(CreateLandmark(LandmarkKind.PlayerStart, start.Position.X, start.Position.Y, 0,
                ("facing", start.Facing), ("femaleLegs", start.Id == KnownObject.PlayerStartFemaleLegs ? 1 : null)));
        }

        foreach (var breaker in elements.Where(element => element.Kind == ElementKind.Breaker))
        {
            bool opens = style is LevelStyle.LockedDoors or LevelStyle.Blackout;
            int transformers = _map.Sprites.Count(sprite => sprite.Id == KnownObject.Transformer && GetDistance(sprite.Position.X, sprite.Position.Y, breaker.X, breaker.Y) <= NearReach);
            int traps = triggers.Count(trigger => GetDistance(trigger.X, trigger.Y, breaker.X, breaker.Y) <= NearReach);
            int beside = charges.Count(charge => GetPlaceId(charge.Cell) == GetPlaceId(breaker.Cell));
            landmarks.Add(CreateLandmark(LandmarkKind.Breaker, breaker.X, breaker.Y, GetRoute(ObjectiveKind.Breaker, breaker),
                ("key-to-gate", opens ? gating.KeyToGatePixels : null), ("opens-nothing", opens ? null : 1), ("transformers", transformers),
                ("traps", traps == 0 ? null : traps), ("charges", beside == 0 ? null : beside)));
        }

        var blackout = _script.Blackout;
        if (blackout is not null)
        {
            var center = blackout.Value.Center;
            int? search = gating.BreakerFromBlackoutPixels.Count == 0 ? null : gating.BreakerFromBlackoutPixels.Min();
            landmarks.Add(CreateLandmark(LandmarkKind.BlackoutTrigger, center.X, center.Y, GetRouteAt(center.X, center.Y, ObjectiveKind.Blackout),
                ("radius", blackout.Value.Radius), ("search", search)));
        }

        landmarks.AddRange(_analysis.Checkpoints.Select(checkpoint => CreateLandmark(LandmarkKind.Checkpoint, checkpoint.X, checkpoint.Y, (int)(checkpoint.Progress * _tour))));

        foreach (var generator in elements.Where(element => element.Kind == ElementKind.Generator))
        {
            int power = _map.Sprites.Count(sprite => sprite.Id is KnownObject.Transformer or KnownObject.Container or KnownObject.Container2 or KnownObject.Container2Alt
                                                     && GetDistance(sprite.Position.X, sprite.Position.Y, generator.X, generator.Y) <= 250);
            landmarks.Add(CreateLandmark(LandmarkKind.Generator, generator.X, generator.Y, GetRoute(ObjectiveKind.Generator, generator),
                ("key-to-gate", gating.KeyToGatePixels), ("fences", elements.Count(element => element.Kind == ElementKind.Fence)), ("power", power)));
        }

        foreach (var batch in Cluster(charges, charge => new PointF(charge.X, charge.Y), 300))
        {
            var first = batch.MinBy(charge => charge.Progress)!;
            var nearest = sites.MinBy(site => GetDistance(site.X, site.Y, first.X, first.Y));
            landmarks.Add(CreateLandmark(LandmarkKind.DynamiteCharges, (float)batch.Average(charge => charge.X), (float)batch.Average(charge => charge.Y),
                (int)(batch.Min(charge => charge.Progress) * _tour), ("charges", batch.Count),
                ("site-distance", nearest is null ? null : (int)GetDistance(nearest.X, nearest.Y, first.X, first.Y)), ("site", nearest is null ? null : GetPlaceId(nearest.Cell))));
        }

        foreach (var site in sites)
        {
            var hole = elements.Where(element => element.Kind is ElementKind.MonsterHole or ElementKind.SpawnWall).MinBy(element => GetDistance(element.X, element.Y, site.X, site.Y));
            var pad = elements.Where(element => element.Kind == ElementKind.TeleportPad).MinBy(element => GetDistance(element.X, element.Y, site.X, site.Y));
            int? holeDistance = hole is not null && GetDistance(hole.X, hole.Y, site.X, site.Y) <= 150 ? (int)GetDistance(hole.X, hole.Y, site.X, site.Y) : null;
            int? padDistance = pad is not null && GetDistance(pad.X, pad.Y, site.X, site.Y) <= 260 ? (int)GetDistance(pad.X, pad.Y, site.X, site.Y) : null;
            int generators = elements.Count(element => element.Kind == ElementKind.EndlessGenerator && GetDistance(element.X, element.Y, site.X, site.Y) <= 260);
            landmarks.Add(CreateLandmark(LandmarkKind.DynamiteSite, site.X, site.Y, GetRoute(ObjectiveKind.DynamiteSite, site),
                ("hole", holeDistance), ("hole-id", holeDistance is null ? null : (int)hole!.Id), ("pad", padDistance), ("generators", generators == 0 ? null : generators)));
        }

        foreach (var boss in elements.Where(element => element.Kind == ElementKind.Boss))
        {
            int escort = elements.Count(element => element.Kind == ElementKind.Monster && GetDistance(element.X, element.Y, boss.X, boss.Y) <= 600);
            landmarks.Add(CreateLandmark(LandmarkKind.Boss, boss.X, boss.Y, GetRoute(ObjectiveKind.Boss, boss), ("id", (int)boss.Id), ("escort", escort)));
        }

        landmarks.AddRange(elements.Where(element => element.Kind == ElementKind.MountedCannon)
            .Select(cannon => CreateLandmark(LandmarkKind.MountedCannon, cannon.X, cannon.Y, (int)(cannon.Progress * _tour))));

        var exit = _script.Exit;
        if (exit is not null)
        {
            var center = exit.Value.Center;
            landmarks.Add(CreateLandmark(LandmarkKind.ExitTrigger, center.X, center.Y, GetRouteAt(center.X, center.Y, null), ("radius", exit.Value.Radius)));
        }

        var final = _analysis.Objectives.MaxBy(objective => objective.Progress);
        if (final is not null)
        {
            var end = CreateLandmark(LandmarkKind.RouteEnd, final.X, final.Y, (int)(final.Progress * _tour));
            landmarks.Add(end);
            Tag(GetPlace(end.Place), PlaceTags.RouteEnd);
        }

        return landmarks.OrderBy(landmark => landmark.Route).ToArray();
    }

    private LevelLandmark CreateLandmark(LandmarkKind kind, float x, float y, int route, params (string Key, int? Value)[] facts) => new()
    {
        Kind = kind,
        Place = GetPlaceNear(x, y),
        Route = route,
        Position = new Point((int)x, (int)y),
        Facts = facts.Where(fact => fact.Value is not null).ToDictionary(fact => fact.Key, fact => fact.Value!.Value)
    };

    private int GetRoute(ObjectiveKind kind, RouteElement element)
    {
        var done = _analysis.Objectives.Where(objective => objective.Kind == kind).MinBy(objective => GetDistance(objective.X, objective.Y, element.X, element.Y));
        return done is not null && GetDistance(done.X, done.Y, element.X, element.Y) < 5 ? (int)(done.Progress * _tour) : (int)(element.Progress * _tour);
    }

    private int GetRouteAt(float x, float y, ObjectiveKind? kind)
    {
        var done = kind is null ? null : _analysis.Objectives.FirstOrDefault(objective => objective.Kind == kind);
        if (done is not null)
        {
            return (int)(done.Progress * _tour);
        }

        var cell = _lattice.GetCell(x, y);
        var tour = _analysis.Tour;
        int best = 0;
        int bestGap = int.MaxValue;
        for (int tourIndex = 0; tourIndex < tour.Count; tourIndex++)
        {
            int gap = Math.Abs(tour[tourIndex].U - cell.U) + Math.Abs(tour[tourIndex].V - cell.V);
            if (gap < bestGap)
            {
                bestGap = gap;
                best = tourIndex;
            }
        }

        return ToPixels(best);
    }

    private LevelEncounter[] DescribeEncounters()
    {
        var encounters = new List<LevelEncounter>();
        var elements = _analysis.Elements;
        var holes = elements.Where(element => element.Kind is ElementKind.SpawnWall or ElementKind.MonsterHole).ToArray();
        var pads = elements.Where(element => element.Kind == ElementKind.TeleportPad).ToArray();
        var sites = elements.Where(element => element.Kind == ElementKind.DynamiteSite).ToArray();

        foreach (var group in Cluster(elements.Where(element => element.Kind == ElementKind.Monster).ToArray(), monster => new PointF(monster.X, monster.Y), GroupReach))
        {
            var place = GetPlace(group.GroupBy(monster => GetPlaceId(monster.Cell)).MaxBy(members => members.Count())!.Key);
            float x = (float)group.Average(monster => monster.X), y = (float)group.Average(monster => monster.Y);
            encounters.Add(new LevelEncounter
            {
                Id = 0,
                Kind = EncounterKind.PlacedGroup,
                Place = place.Id,
                Route = (int)(group.Min(monster => monster.Progress) * _tour),
                Count = group.Count,
                HitPoints = group.Sum(monster => GetHitPoints(monster.Id)),
                Monsters = Compose(group.Select(monster => monster.Id)),
                Formation = GetFormation(group),
                Spot = GetSpot(place, x, y),
                Position = new Point((int)x, (int)y)
            });
        }

        foreach (var boss in elements.Where(element => element.Kind == ElementKind.Boss))
        {
            var place = GetPlace(GetPlaceId(boss.Cell));
            encounters.Add(new LevelEncounter
            {
                Id = 0,
                Kind = EncounterKind.Boss,
                Place = place.Id,
                Route = GetRoute(ObjectiveKind.Boss, boss),
                Count = 1,
                HitPoints = GetHitPoints(boss.Id),
                Monsters = [new MonsterCount(boss.Id, 1)],
                Formation = EncounterFormation.Single,
                Spot = GetSpot(place, boss.X, boss.Y),
                Position = new Point(boss.X, boss.Y)
            });
        }

        var spawners = elements.Where(element => element.Kind == ElementKind.Spawner).ToArray();
        foreach (var group in spawners.GroupBy(spawner => spawner.Program!.Trigger ?? (spawner.X, spawner.Y, -1)))
        {
            var members = group.ToArray();
            var anchor = members[0];
            var trigger = anchor.Program!.Trigger;
            var monsters = members.SelectMany(member => member.Program!.Monsters).ToArray();
            bool builds = monsters.Length > 0 && monsters.All(monster => monster.IsEndlessGenerator);
            int route;
            int? triggerPlace = null;
            SpawnSource? source = null;
            LandmarkKind? nearKey = null;
            if (trigger is not null)
            {
                (int triggerX, int triggerY, _) = trigger.Value;
                triggerPlace = GetPlaceNear(triggerX, triggerY);
                route = members.Min(member => member.TriggerProgress is null ? GetRouteAt(triggerX, triggerY, null) : (int)(member.TriggerProgress.Value * _tour));
                source = GetSource(anchor, holes, pads);
                var key = elements.FirstOrDefault(element => element.Kind is ElementKind.Breaker or ElementKind.Generator or ElementKind.DynamiteSite
                                                             && GetDistance(element.X, element.Y, triggerX, triggerY) <= NearReach);
                nearKey = key?.Kind switch
                {
                    ElementKind.Breaker => LandmarkKind.Breaker,
                    ElementKind.Generator => LandmarkKind.Generator,
                    ElementKind.DynamiteSite => LandmarkKind.DynamiteSite,
                    _ => null
                };
            }
            else
            {
                route = (int)(anchor.Progress * _tour);
            }

            int?[] distances = members.Select(member => member.TriggerDistance).Where(distance => distance is not null).ToArray();
            encounters.Add(new LevelEncounter
            {
                Id = 0,
                Kind = builds ? EncounterKind.EndlessGenerators : trigger is null ? EncounterKind.Spawner : EncounterKind.Ambush,
                Place = members.GroupBy(member => GetPlaceId(member.Cell)).MaxBy(places => places.Count())!.Key,
                Route = route,
                Count = monsters.Length,
                HitPoints = monsters.Sum(GetHitPoints),
                Monsters = Compose(monsters),
                Formation = EncounterFormation.Stream,
                Source = source,
                Spawners = members.Length,
                TriggerPlace = triggerPlace,
                TriggerRadius = trigger?.Radius,
                TriggerDistance = distances.Length == 0 ? null : distances.Min(),
                NearKey = nearKey,
                Position = new Point((int)members.Average(member => member.X), (int)members.Average(member => member.Y))
            });
        }

        foreach (var group in Cluster(elements.Where(element => element.Kind == ElementKind.EndlessGenerator).ToArray(), generator => new PointF(generator.X, generator.Y), 200))
        {
            var anchor = group[0];
            var site = sites.MinBy(site => GetDistance(site.X, site.Y, anchor.X, anchor.Y));
            var pad = pads.MinBy(pad => GetDistance(pad.X, pad.Y, anchor.X, anchor.Y));
            encounters.Add(new LevelEncounter
            {
                Id = 0,
                Kind = EncounterKind.EndlessGenerators,
                Place = GetPlaceId(anchor.Cell),
                Route = (int)(group.Min(generator => generator.Progress) * _tour),
                Count = group.Count,
                HitPoints = 0,
                Monsters = Compose(group.Select(generator => generator.Id)),
                Formation = EncounterFormation.Source,
                Source = pad is not null && GetDistance(pad.X, pad.Y, anchor.X, anchor.Y) <= 120 ? SpawnSource.TeleportPad : null,
                ShutBy = site is not null && GetDistance(site.X, site.Y, anchor.X, anchor.Y) <= 260 ? GetPlaceId(site.Cell) : null,
                Position = new Point((int)group.Average(generator => generator.X), (int)group.Average(generator => generator.Y))
            });
        }

        var ordered = encounters
            .OrderBy(encounter => encounter.Route)
            .ThenBy(encounter => encounter.Kind)
            .ThenBy(encounter => encounter.Position.Y)
            .ThenBy(encounter => encounter.Position.X)
            .Select((encounter, index) => WithId(encounter, index + 1))
            .ToArray();

        foreach (var encounter in ordered)
        {
            GetPlace(encounter.TriggerPlace ?? encounter.Place).ThreatHitPoints += encounter.HitPoints;
        }

        return ordered;
    }

    private static LevelEncounter WithId(LevelEncounter encounter, int id) => new()
    {
        Id = id,
        Kind = encounter.Kind,
        Place = encounter.Place,
        Route = encounter.Route,
        Count = encounter.Count,
        HitPoints = encounter.HitPoints,
        Monsters = encounter.Monsters,
        Formation = encounter.Formation,
        Spot = encounter.Spot,
        Source = encounter.Source,
        Spawners = encounter.Spawners,
        TriggerPlace = encounter.TriggerPlace,
        TriggerRadius = encounter.TriggerRadius,
        TriggerDistance = encounter.TriggerDistance,
        NearKey = encounter.NearKey,
        ShutBy = encounter.ShutBy,
        Position = encounter.Position
    };

    private static SpawnSource GetSource(RouteElement spawner, RouteElement[] holes, RouteElement[] pads)
    {
        if (holes.Any(hole => Math.Abs(hole.X - spawner.X) < 100 && Math.Abs(hole.Y - spawner.Y) < 100))
        {
            return SpawnSource.SpawnCloset;
        }

        if (pads.Any(pad => Math.Abs(pad.X - spawner.X) < 260 && Math.Abs(pad.Y - spawner.Y) < 260))
        {
            return SpawnSource.TeleportPad;
        }

        float triggerProgress = spawner.TriggerProgress ?? 0;
        return spawner.BranchDepth > 300 && spawner.BranchDepth - (spawner.TriggerBranchDepth ?? 0) > 200 ? SpawnSource.SideRoom
            : spawner.Progress > triggerProgress + 0.03f ? SpawnSource.Ahead
            : spawner.Progress < triggerProgress - 0.03f ? SpawnSource.Behind
            : SpawnSource.AtTrigger;
    }

    private static EncounterFormation GetFormation(List<RouteElement> group)
    {
        if (group.Count == 1)
        {
            return EncounterFormation.Single;
        }

        float meanX = (float)group.Average(monster => monster.X), meanY = (float)group.Average(monster => monster.Y);
        float xx = 0, yy = 0, xy = 0;
        foreach (var monster in group)
        {
            xx += (monster.X - meanX) * (monster.X - meanX);
            yy += (monster.Y - meanY) * (monster.Y - meanY);
            xy += (monster.X - meanX) * (monster.Y - meanY);
        }

        float half = (xx + yy) / 2;
        float root = MathF.Sqrt(MathF.Max(0, half * half - (xx * yy - xy * xy)));
        if ((half + root) / MathF.Max(1e-3f, half - root) > 12)
        {
            return EncounterFormation.Line;
        }

        float[] gaps = group.Select(monster => group.Where(other => other != monster).Min(other => GetDistance(other.X, other.Y, monster.X, monster.Y))).ToArray();
        float mean = gaps.Average();
        float spread = MathF.Sqrt(gaps.Average(gap => (gap - mean) * (gap - mean)));
        return group.Count >= 6 && spread < 0.2f * mean ? EncounterFormation.Grid : EncounterFormation.Cluster;
    }

    private PlaceSpot GetSpot(PlaceDraft place, float x, float y)
    {
        if (place.FirstVisit is null)
        {
            return PlaceSpot.OffRoute;
        }

        if (place.LengthPixels < 250)
        {
            return PlaceSpot.SmallPlace;
        }

        var entry = _lattice.GetCenter(_analysis.Tour.First(cell => _segments.PlaceOf.TryGetValue(cell, out int region) && _number[region] == place.Id));
        float far = place.Cells.Select(cell => _lattice.GetCenter(cell)).Max(center => GetDistance(center.X, center.Y, entry.X, entry.Y));
        float share = GetDistance(x, y, entry.X, entry.Y) / MathF.Max(1, far);
        return share < 0.33f ? PlaceSpot.NearEntrance : share < 0.67f ? PlaceSpot.Middle : PlaceSpot.FarEnd;
    }

    private LevelItem[] DescribeItems(LevelEncounter[] encounters)
    {
        var items = new List<LevelItem>();
        foreach (var element in _analysis.Elements.Where(element => element.Kind == ElementKind.Pickup || (element.Kind == ElementKind.Crate && element.CrateItem != 0)))
        {
            var place = GetPlace(GetPlaceId(element.Cell));
            int route = (int)(element.Progress * _tour);
            var before = encounters.Where(fight => fight.HitPoints > 0 && fight.Route <= route && route - fight.Route <= 1200 && IsNear(fight, place.Id)).MaxBy(fight => fight.Route);
            var after = encounters.Where(fight => fight.HitPoints > 0 && fight.Route > route && fight.Route - route <= 800 && IsNear(fight, place.Id)).MinBy(fight => fight.Route);
            items.Add(new LevelItem
            {
                Id = element.Kind == ElementKind.Crate ? element.CrateItem : element.Id,
                Category = element.Category,
                Place = place.Id,
                Route = route,
                OffRoute = element.BranchDepth,
                InCrate = element.Kind == ElementKind.Crate,
                After = before?.Id,
                Before = after?.Id,
                Position = new Point(element.X, element.Y)
            });
        }

        return items.OrderBy(item => item.Route).ThenBy(item => item.Position.Y).ThenBy(item => item.Position.X).ToArray();

        bool IsNear(LevelEncounter fight, int place) =>
            fight.Place == place || fight.TriggerPlace == place
            || _walkable.Any(link => (link.A == place && (link.B == fight.Place || link.B == fight.TriggerPlace)) || (link.B == place && (link.A == fight.Place || link.A == fight.TriggerPlace)));
    }

    private void AssignRoles(LevelLandmark[] landmarks, LevelEncounter[] encounters)
    {
        int placedTotal = Math.Max(1, encounters.Where(encounter => encounter.Kind == EncounterKind.PlacedGroup).Sum(encounter => encounter.HitPoints));
        
        var furniture = _map.Sprites
            .Where(sprite => sprite.Id.Family is not null || sprite.Id == KnownObject.ScatteredPaper)
            .Select(sprite => GetPlaceNear(sprite.Position.X, sprite.Position.Y, anywhere: false))
            .Where(place => place != 0)
            .CountBy(place => place)
            .ToDictionary();
        
        foreach (var place in _places)
        {
            var here = landmarks.Where(landmark => landmark.Place == place.Id).Select(landmark => landmark.Kind).ToHashSet();
            var fights = encounters.Where(encounter => encounter.Place == place.Id).ToArray();
            var groups = fights.Where(encounter => encounter.Kind == EncounterKind.PlacedGroup).ToArray();
            place.FurniturePieces = furniture.GetValueOrDefault(place.Id);
            place.Use = place.Kind == PlaceKind.SecretCloset ? PlaceUse.Stash
                : here.Contains(LandmarkKind.Boss) ? PlaceUse.BossDen
                : here.Contains(LandmarkKind.Breaker) && !place.Tags.HasFlag(PlaceTags.Hub) ? PlaceUse.BreakerRoom
                : here.Contains(LandmarkKind.Generator) ? PlaceUse.GeneratorRoom
                : here.Contains(LandmarkKind.DynamiteSite) ? PlaceUse.DynamiteSite
                : fights.Any(encounter => encounter.Kind is EncounterKind.Spawner or EncounterKind.EndlessGenerators
                                          || (encounter.Kind == EncounterKind.Ambush && place.Kind == PlaceKind.SpawnCloset)) ? PlaceUse.MonsterSource
                : groups.Sum(encounter => encounter.HitPoints) >= 0.2f * placedTotal && groups.Sum(encounter => encounter.Count) >= 10 ? PlaceUse.Lair
                : PlaceUse.None;
        }
    }

    private LevelTheme FindSetting()
    {
        if (_analysis.Theme != LevelTheme.Lab)
        {
            return _analysis.Theme;
        }

        int military = _map.Sprites.Count(sprite => sprite.Id == KnownObject.MilitaryFloor);
        int lab = _map.Sprites.Count(sprite => sprite.Id is KnownObject.BasementFloor or KnownObject.BasementFloorAlt);
        return military > lab ? LevelTheme.Military : LevelTheme.Lab;
    }

    private LevelGate[] DescribeGates(LevelStyle style, LevelLandmark[] landmarks)
    {
        var gates = new List<LevelGate>();
        var gating = _analysis.Gating;
        var connections = _connections.Select(entry => entry.Connection).ToArray();
        if (style is LevelStyle.LockedDoors or LevelStyle.Blackout)
        {
            var breaker = landmarks.FirstOrDefault(landmark => landmark.Kind == LandmarkKind.Breaker);
            var doors = connections.Where(connection => connection.Kind == ConnectionKind.LockedDoor).ToArray();
            int[] near = doors
                .Where(door => GetPlace(door.A).Zone != GetPlace(door.B).Zone)
                .SelectMany(door => new[] { door.A, door.B })
                .Where(place => GetPlace(place).Zone is GateZone.Open)
                .Distinct()
                .Order()
                .ToArray();

            var trigger = style == LevelStyle.Blackout ? landmarks.FirstOrDefault(landmark => landmark.Kind == LandmarkKind.BlackoutTrigger) : null;
            float share = style == LevelStyle.Blackout
                ? _places.Where(place => place.Zone == GateZone.Blackout).Sum(place => place.AreaPixels) / (float)Math.Max(1, _places.Sum(place => place.AreaPixels))
                : gating.LockedShare ?? 0;

            gates.Add(new LevelGate
            {
                Kind = style,
                GatedShare = MathF.Round(share, 3),
                KeyPlace = breaker?.Place,
                KeyRoute = breaker?.Route,
                KeyToGate = gating.KeyToGatePixels,
                GatePlaces = near,
                Connections = doors.Length,
                TriggerPlace = trigger?.Place,
                TriggerRoute = trigger?.Route
            });
        }

        if (gating.FencedOffShare is not null)
        {
            var generator = landmarks.FirstOrDefault(landmark => landmark.Kind == LandmarkKind.Generator);
            var fences = connections.Where(connection => connection.Kind == ConnectionKind.Fence).ToArray();
            gates.Add(new LevelGate
            {
                Kind = LevelStyle.Fenced,
                GatedShare = MathF.Round(gating.FencedOffShare.Value, 3),
                KeyPlace = generator?.Place,
                KeyRoute = generator?.Route,
                KeyToGate = gating.KeyToGatePixels,
                GatePlaces = fences.SelectMany(fence => new[] { fence.A, fence.B }).Where(place => GetPlace(place).Zone == GateZone.Open).Distinct().Order().ToArray(),
                Connections = fences.Length
            });
        }

        return gates.ToArray();
    }

    private LevelSignature MeasureSignature(LevelMeasurement measurement, int loops, LevelEncounter[] encounters, LevelItem[] items, LevelGate[] gates)
    {
        int floorPixels = _places.Sum(place => place.AreaPixels);
        int offRoutePixels = _places.Where(place => place.FirstVisit is null).Sum(place => place.AreaPixels);
        var fights = encounters.Where(encounter => encounter.HitPoints > 0).ToArray();
        int[] fifths = new int[5];
        foreach (var fight in fights)
        {
            fifths[Math.Clamp(fight.Route * 5 / _tour, 0, 4)] += fight.HitPoints;
        }

        var start = _places.FirstOrDefault(place => place.Tags.HasFlag(PlaceTags.Start));
        return new LevelSignature
        {
            TourPixels = _analysis.RoutePixels,
            SpinePixels = _analysis.SpinePixels,
            RevisitShare = MathF.Round(_analysis.RevisitShare, 3),
            FloorPixels = floorPixels,
            BoxFill = MeasureBoxFill(),
            Places = _places.Length,
            RoutePlaces = _places.Count(place => place.FirstVisit is not null),
            Loops = loops,
            OffRouteShare = MathF.Round(offRoutePixels / (float)Math.Max(1, floorPixels), 3),
            StartPixels = start?.AreaPixels ?? 0,
            HitPoints = measurement.HitPoints,
            SpawnedShare = MathF.Round(measurement.SpawnedShare, 3),
            HitPointsByFifth = fifths,
            FirstFight = fights.Length == 0 ? null : MathF.Round(fights.Min(fight => fight.Route) / (float)_tour, 3),
            BlockedShare = MathF.Round(measurement.BlockedShare, 3),
            Breakables = measurement.Breakables,
            GatedShare = gates.Length == 0 ? 0 : gates[0].GatedShare,
            KeyToGate = _analysis.Gating.KeyToGatePixels,
            KeyWalk = _analysis.Legs.Where(leg => leg.Phase == RoutePhase.DoorsLocked).Select(leg => (int?)leg.Pixels).FirstOrDefault(),
            FirstGenerator = _analysis.Objectives.Where(objective => objective.Kind == ObjectiveKind.Generator).Select(objective => (float?)MathF.Round(objective.Progress, 3)).FirstOrDefault(),
            OffRouteItemShare = MathF.Round(items.Count(item => item.OffRoute > OffRoutePixels) / (float)Math.Max(1, items.Length), 3)
        };
    }

    private PlaceDraft GetPlace(int id) => _places[id - 1];

    private PlaceDraft? FindPlace(Cell cell) => _segments.PlaceOf.TryGetValue(cell, out int region) ? GetPlace(_number[region]) : null;

    private int GetPlaceId(Cell cell)
    {
        if (_segments.PlaceOf.TryGetValue(cell, out int region))
        {
            return _number[region];
        }

        (float x, float y) = _lattice.GetCenter(cell);
        return GetPlaceNear(x, y);
    }

    private int GetPlaceNear(float x, float y, bool anywhere = true)
    {
        var cell = FindNearest(x, y, _segments.Floor, anywhere);
        return cell is null ? 0 : _number[_segments.PlaceOf[cell.Value]];
    }

    private Cell? FindNearest(float x, float y, HashSet<Cell> pool, bool anywhere = true)
    {
        var at = _lattice.GetCell(x, y);
        if (pool.Contains(at))
        {
            return at;
        }

        for (int radius = 1; radius < 10; radius++)
        {
            Cell? best = null;
            float bestDistance = float.MaxValue;
            for (int du = -radius; du <= radius; du++)
            {
                for (int dv = -radius; dv <= radius; dv++)
                {
                    var cell = new Cell(at.U + du, at.V + dv);
                    if (!pool.Contains(cell))
                    {
                        continue;
                    }

                    var center = _lattice.GetCenter(cell);
                    float distance = GetDistance(center.X, center.Y, x, y);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = cell;
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return anywhere && pool.Count > 0 ? pool.MinBy(cell => GetDistance(_lattice.GetCenter(cell).X, _lattice.GetCenter(cell).Y, x, y)) : null;
    }

    private static Dictionary<Cell, int> MeasureWalks(HashSet<Cell> cells, IEnumerable<Cell> sources, Func<EdgeKey, bool> isClosed, out Dictionary<Cell, Cell> previous)
    {
        var walks = new Dictionary<Cell, int>();
        previous = new Dictionary<Cell, Cell>();
        var queue = new Queue<Cell>();
        foreach (var source in sources)
        {
            if (cells.Contains(source) && walks.TryAdd(source, 0))
            {
                queue.Enqueue(source);
            }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (cells.Contains(neighbor) && !walks.ContainsKey(neighbor) && !isClosed(edge))
                {
                    walks[neighbor] = walks[cell] + 1;
                    previous[neighbor] = cell;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return walks;
    }

    private PointF GetCenter(List<Cell> cells)
    {
        float x = 0, y = 0;
        foreach (var cell in cells)
        {
            var center = _lattice.GetCenter(cell);
            x += center.X;
            y += center.Y;
        }

        return new PointF(x / cells.Count, y / cells.Count);
    }

    private float MeasureBoxFill()
    {
        var floor = _segments.Floor;
        int width = floor.Max(cell => cell.U) - floor.Min(cell => cell.U) + 1;
        int height = floor.Max(cell => cell.V) - floor.Min(cell => cell.V) + 1;
        return MathF.Round(floor.Count / (float)(width * height), 3);
    }

    private static void Tag(PlaceDraft? place, PlaceTags tag)
    {
        place?.Tags |= tag;
    }

    private int ToPixels(int tourIndex) => (int)MathF.Round(tourIndex * _step);

    private int GetHitPoints(KnownObject id) => _objects.TryFind(id, out var found) ? found.MaxHp : 0;

    private static MonsterCount[] Compose(IEnumerable<KnownObject> ids) =>
        ids.GroupBy(id => id).OrderByDescending(group => group.Count()).ThenBy(group => (int)group.Key).Select(group => new MonsterCount(group.Key, group.Count())).ToArray();

    private static float GetDistance(float x0, float y0, float x1, float y1) => MathF.Sqrt((x0 - x1) * (x0 - x1) + (y0 - y1) * (y0 - y1));

    private static List<List<T>> Cluster<T>(T[] items, Func<T, PointF> getPosition, float reach)
    {
        var groups = new List<List<T>>();
        var left = new SortedSet<int>(Enumerable.Range(0, items.Length));
        var points = items.Select(getPosition).ToArray();
        while (left.Count > 0)
        {
            int seed = left.Min;
            left.Remove(seed);
            var group = new List<int> { seed };
            for (int groupIndex = 0; groupIndex < group.Count; groupIndex++)
            {
                var point = points[group[groupIndex]];
                foreach (int other in left.Where(other => Math.Abs(points[other].X - point.X) <= reach && Math.Abs(points[other].Y - point.Y) <= reach
                                                          && GetDistance(points[other].X, points[other].Y, point.X, point.Y) <= reach).ToArray())
                {
                    left.Remove(other);
                    group.Add(other);
                }
            }

            groups.Add(group.Select(itemIndex => items[itemIndex]).ToList());
        }

        return groups;
    }

    private sealed class PlaceDraft
    {
        public required int Id { get; init; }

        public required List<Cell> Cells { get; init; }

        public required PlaceKind Kind { get; set; }

        public required GateZone Zone { get; init; }

        public required Point Center { get; init; }

        public required int AreaPixels { get; init; }

        public required int LengthPixels { get; init; }

        public required int WidthPixels { get; init; }

        public int? WalkFromStart { get; init; }

        public int? FirstVisit { get; init; }

        public required int Visits { get; init; }

        public int? Detour { get; init; }

        public int? EnteredFrom { get; init; }

        public required float BlockedShare { get; init; }

        public PlaceTags Tags { get; set; }

        public PlaceUse Use { get; set; }

        public int FurniturePieces { get; set; }

        public int ThreatHitPoints { get; set; }

        public LevelPlace ToPlace() => new()
        {
            Id = Id,
            Kind = Kind,
            Zone = Zone,
            Cells = Cells,
            Center = Center,
            Use = Use,
            FurniturePieces = FurniturePieces,
            Tags = Tags,
            AreaPixels = AreaPixels,
            LengthPixels = LengthPixels,
            WidthPixels = WidthPixels,
            WalkFromStart = WalkFromStart,
            FirstVisit = FirstVisit,
            Visits = Visits,
            Detour = Detour,
            EnteredFrom = EnteredFrom,
            ThreatHitPoints = ThreatHitPoints,
            BlockedShare = BlockedShare
        };
    }
}
