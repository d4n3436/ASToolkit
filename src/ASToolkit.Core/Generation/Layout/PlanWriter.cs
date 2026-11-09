using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Layout;

internal sealed class PlanWriter
{
    private const float CenteredDoorShare = 0.93f;
    private const float WideDoorShare = 0.47f;
    internal const float MinDoorGap = 146;

    private readonly Random _rng;
    private readonly PlanDraft _plan;

    public PlanWriter(WallSet wallSet, Random rng)
    {
        _rng = rng;
        _plan = new PlanDraft(wallSet == WallSet.Basement ? WallSetSettings.Basement : WallSetSettings.Concrete);
    }

    public LayoutPlan Write(BuildingPlan building, BuildingMission built)
    {
        var walkable = building.Rooms
            .Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room)
            .ToArray();

        var halls = walkable.Where(room => room.Kind == BuildingRoomKind.Hall).Select(room => room.Id).ToHashSet();
        var mission = built.Mission;
        var result = new PlanInput
        {
            Cells = walkable.ToDictionary(room => room.Id, room => room.Rects.SelectMany(rect => rect.Units).ToHashSet()),
            PhaseOf = walkable.ToDictionary(room => room.Id, room => mission.Progression.GetPhase(room.Id) ?? room.Phase),
            Parent = built.Parent.ToDictionary(),
            Spine = built.Spine.ToHashSet(),
            ChokeDoors = built.ChokeDoors,
            Halls = halls,
            Types = walkable.ToDictionary(room => room.Id, room => room.Type),
            Start = building.Start,
            End = built.End,
            Realized = built.Realized,
            SideRooms = building.Rooms
                .Where(room => room.Host is not null)
                .Select(room => (room.Rects.SelectMany(rect => rect.Units).ToHashSet(), room.Kind switch
                {
                    BuildingRoomKind.SpawnCloset => RegionKind.SpawnCloset,
                    BuildingRoomKind.SecretRoom => RegionKind.SecretRoom,
                    _ => RegionKind.SecretCloset
                }, room.Type, room.Host!.Value))
                .ToArray()
        };

        var regionOf = AddRegions(mission, result);
        var gate = GetGate(mission);
        var locked = mission.Links
            .Where(link => link.IsLocked)
            .Select(link => new RoomPair(link.From, link.To))
            .ToHashSet();

        locked.UnionWith(result.Realized.Where(link => link.Kind == MissionLinkKind.Entrance).Select(link => new RoomPair(link.From, link.To)));

        var closing = mission.Progression.Seals
            .Where(seal => seal.Kind == SealKind.DoorsClose)
            .Select(seal => new RoomPair(seal.From, seal.To))
            .ToHashSet();

        // Give the quiet phase of a blackout level working doors, the doors the blackout locks behind the player
        if (gate == GateKind.Blackout)
        {
            closing.UnionWith(result.Realized
                .Where(link => result.PhaseOf[link.From] == 0 && result.PhaseOf[link.To] == 0)
                .Select(link => new RoomPair(link.From, link.To)));
        }

        var open = building.OpenWalls.ToHashSet();

        var lockedLinks = AddLinks(result, regionOf, gate, locked, closing, open);
        _plan.Gate = gate;
        MarkGated(regionOf[result.Start], lockedLinks);
        SetGateRooms(mission, regionOf, gate);
        _plan.SecondWing.UnionWith(building.SecondWing.Where(regionOf.ContainsKey).Select(room => _plan.Regions[regionOf[room]]));
        _plan.LockedSiteRooms.UnionWith(building.LockedSiteRooms.Where(regionOf.ContainsKey).Select(room => _plan.Regions[regionOf[room]]));
        AddSideRooms(result, regionOf);
        _plan.SecondWing.UnionWith(_plan.Regions.Where(region => region.Kind is RegionKind.SpawnCloset or RegionKind.SecretCloset or RegionKind.SecretRoom
            && region.Host is not null && _plan.SecondWing.Contains(_plan.Regions[region.Host.Value])).ToArray());
        AlignLinks();
        SpaceDoors();
        AddJammedDoors(building, regionOf);
        return _plan.Freeze();
    }

    private static GateKind? GetGate(MissionGraph mission)
    {
        var kinds = mission.Progression.Keys.Select(key => key.Kind).ToHashSet();
        return kinds.Contains(LandmarkKind.Generator) ? GateKind.Fences
            : kinds.Contains(LandmarkKind.BlackoutTrigger) ? GateKind.Blackout
            : kinds.Contains(LandmarkKind.Breaker) ? GateKind.LockedDoors
            : null;
    }

    // Add the stations along the critical paths in flow order, then the rooms hung off them, then the corridors
    private Dictionary<int, int> AddRegions(MissionGraph mission, PlanInput result)
    {
        var stations = mission.Progression.Phases
            .SelectMany(phase => phase.Path)
            .Prepend(result.Start)
            .Where(result.Cells.ContainsKey)
            .Distinct()
            .ToList();

        if (result.Cells.ContainsKey(result.End) && result.End != result.Start)
        {
            stations.Remove(result.End);
            stations.Add(result.End);
        }

        var order = new List<int>(stations);
        var queue = new Queue<int>(stations);
        var reached = stations.ToHashSet();
        var children = result.Parent
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).Order().ToArray());

        while (queue.Count > 0)
        {
            foreach (int child in children.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                if (result.Cells.ContainsKey(child) && reached.Add(child))
                {
                    order.Add(child);
                    queue.Enqueue(child);
                }
            }
        }

        order.AddRange(result.Cells.Keys.Where(room => !reached.Contains(room)).Order());
        var regionOf = new Dictionary<int, int>();
        var flow = mission.Progression.Flow
            .Select(KeyValuePair.Create)
            .DistinctBy(pair => pair.Key)
            .ToDictionary();

        foreach (int room in order)
        {
            int station = stations.IndexOf(room);
            var region = AddRegion(result.Cells[room], station >= 0 ? RegionKind.Station : RegionKind.SideRoom, Math.Max(0, station), result.Halls.Contains(room));
            region.Type = result.Types.TryGetValue(room, out var type) ? type : null;
            region.Phase = result.PhaseOf.GetValueOrDefault(room);
            region.Order = flow.GetValueOrDefault(room, int.MaxValue);
            region.Role = mission.Roles.TryGetValue(room, out var role) ? role : result.Spine.Contains(room) ? MissionRole.Path : MissionRole.Branch;
            regionOf[room] = region.Id;
            if (station >= 0)
            {
                _plan.Stations.Add(region);
            }
        }

        foreach (int room in order.Where(room => !stations.Contains(room)))
        {
            var region = _plan.Regions[regionOf[room]];
            int? host = result.Parent.TryGetValue(room, out int parent) && regionOf.TryGetValue(parent, out int parentRegion) ? parentRegion : null;
            region.Host = host;
            int? walker = room;
            while (walker is not null && !stations.Contains(walker.Value))
            {
                walker = result.Parent.TryGetValue(walker.Value, out int up) ? up : null;
            }

            region.Station = walker is null ? 0 : stations.IndexOf(walker.Value);
        }

        return regionOf;
    }

    private RegionDraft AddRegion(HashSet<Cell> cells, RegionKind kind, int station, bool isHall)
    {
        var rects = DecomposeRects(cells);
        var region = _plan.AddRegion(kind, rects[0], station, null, isHall);
        foreach (var rect in rects.Skip(1))
        {
            _plan.ExtendRegion(region, rect);
        }

        return region;
    }

    private HashSet<LinkDraft> AddLinks(PlanInput result, Dictionary<int, int> regionOf, GateKind? gate, HashSet<RoomPair> locked, HashSet<RoomPair> closing,
        HashSet<RoomPair> open)
    {
        var lockedLinks = new HashSet<LinkDraft>();
        var boundaries = FindBoundaries();
        foreach ((int from, int to, _) in result.Realized)
        {
            var pair = new RoomPair(from, to);
            bool isLocked = locked.Contains(pair);
            var linkKind = isLocked ? gate == GateKind.Fences ? LinkKind.Fence : LinkKind.Door
                : closing.Contains(pair) ? LinkKind.Door
                : open.Contains(pair) ? LinkKind.Open
                : LinkKind.Arch;
            bool allowDoor = gate is null || isLocked || closing.Contains(pair);
            var link = AddLink(boundaries, regionOf[from], regionOf[to], linkKind, allowDoor);
            link?.IsChoke = result.ChokeDoors.Contains(new RoomPair(from, to));

            if (isLocked && link is not null)
            {
                lockedLinks.Add(link);
            }
        }

        return lockedLinks;
    }

    private LinkDraft? AddLink(Dictionary<RoomPair, List<EdgeKey>> boundaries, int from, int to, LinkKind kind, bool allowDoor)
    {
        if (!boundaries.TryGetValue(new RoomPair(from, to), out var edges))
        {
            return null;
        }

        var boundary = GetLongestRun(edges);
        if (boundary.Length == 0)
        {
            return null;
        }

        var link = new LinkDraft { From = from, To = to, Kind = kind, Edges = [], Boundary = boundary };
        _plan.Links.Add(link);
        SetLinkKind(link, kind, allowDoor);
        return link;
    }

    // Gate every region the start reaches only through a lock
    private void MarkGated(int start, HashSet<LinkDraft> lockedLinks)
    {
        var reached = new HashSet<int> { start };
        var queue = new Queue<int>([start]);
        while (queue.Count > 0)
        {
            int region = queue.Dequeue();
            foreach (var link in _plan.Links.Where(link => link.From == region || link.To == region))
            {
                int other = link.From == region ? link.To : link.From;
                if (!lockedLinks.Contains(link) && reached.Add(other))
                {
                    queue.Enqueue(other);
                }
            }
        }

        foreach (var region in _plan.Regions)
        {
            region.IsGated = !reached.Contains(region.Id);
        }
    }

    private void SetGateRooms(MissionGraph mission, Dictionary<int, int> regionOf, GateKind? gate)
    {
        if (gate is null)
        {
            return;
        }

        var keyKind = gate == GateKind.Fences ? LandmarkKind.Generator : LandmarkKind.Breaker;
        var keys = mission.Progression.Keys
            .Where(key => key.Kind == keyKind && regionOf.ContainsKey(key.Place))
            .Select(key => _plan.Regions[regionOf[key.Place]])
            .Distinct()
            .ToArray();

        _plan.KeyRoom = keys.FirstOrDefault();
        if (gate == GateKind.Fences)
        {
            _plan.GeneratorRooms = keys;
        }

        if (gate == GateKind.Blackout)
        {
            var seal = mission.Progression.Seals
                .Where(seal => seal.Kind == SealKind.DoorsClose && regionOf.ContainsKey(seal.From) && regionOf.ContainsKey(seal.To))
                .Select(seal => (LevelSeal?)seal)
                .FirstOrDefault();

            int? trigger = mission.Progression.Keys
                .Where(key => key.Kind == LandmarkKind.BlackoutTrigger && regionOf.ContainsKey(key.Place))
                .Select(key => (int?)key.Place)
                .FirstOrDefault();

            if (seal is not null)
            {
                var pair = new RoomPair(regionOf[seal.Value.From], regionOf[seal.Value.To]);
                _plan.BlackoutLink = _plan.Links.FirstOrDefault(link => new RoomPair(link.From, link.To) == pair);
            }

            if (trigger is not null)
            {
                _plan.TriggerRoom = _plan.Regions[regionOf[trigger.Value]];
            }

            if (_plan.BlackoutLink is null && trigger is not null)
            {
                int region = regionOf[trigger.Value];
                _plan.BlackoutLink = _plan.Links
                    .Where(link => link.To == region || link.From == region)
                    .OrderBy(link => link.To == region ? 0 : 1)
                    .FirstOrDefault();
            }
        }
    }

    private void AddSideRooms(PlanInput result, Dictionary<int, int> regionOf)
    {
        foreach ((var cells, var kind, var type, int room) in result.SideRooms)
        {
            if (!regionOf.TryGetValue(room, out int hostId))
            {
                continue;
            }

            var host = _plan.Regions[hostId];
            var region = AddRegion(cells, kind, host.Station, false);
            region.Host = host.Id;
            region.IsGated = host.IsGated;
            region.Phase = host.Phase;
            if (kind == RegionKind.SecretRoom)
            {
                region.Type = type;
            }

            var shared = host.Rects
                .SelectMany(rect => region.Rects.SelectMany(rect.GetSharedEdges))
                .ToArray();

            if (shared.Length == 0)
            {
                continue;
            }

            var linkKind = kind == RegionKind.SpawnCloset ? LinkKind.SpawnWall : LinkKind.SecretWall;
            _plan.Links.Add(new LinkDraft { From = host.Id, To = region.Id, Kind = linkKind, Edges = [shared[shared.Length / 2]], Boundary = shared });
        }
    }

    private void SetLinkKind(LinkDraft link, LinkKind kind, bool allowDoor = true)
    {
        var all = link.Boundary;
        var inner = GetInnerBoundary(all);
        bool basement = _plan.Settings.WallSet == WallSet.Basement;
        if (kind == LinkKind.Arch && inner.Length < 1)
        {
            kind = basement && allowDoor ? LinkKind.Door : LinkKind.Open;
        }

        // Leave a wall stub beside concrete arches to keep each room's own walls
        int width = kind switch
        {
            LinkKind.Arch => Math.Min(_rng.NextInclusive(_plan.Settings.ArchWidth), basement ? inner.Length : Math.Max(1, inner.Length - 1)),
            LinkKind.Door => inner.Length >= 2 && _rng.NextSingle() < WideDoorShare ? 2 : 1,
            _ => 1
        };
        link.Kind = kind;
        if (kind == LinkKind.Open)
        {
            link.Edges = all.ToList();
        }
        else if (kind == LinkKind.Fence)
        {
            link.Edges = [all[all.Length / 2]];
        }
        else
        {
            int start = _rng.Next(0, inner.Length - width + 1);
            if (basement && _rng.NextSingle() < CenteredDoorShare)
            {
                start = (inner.Length - width) / 2;
            }

            link.Edges = inner.Skip(start).Take(width).ToList();
        }
    }

    // Move each doorway to the nearest span of its boundary where no other wall meets it, keeping a wall piece at both ends when it can
    private void AlignLinks()
    {
        foreach (var link in _plan.Links.Where(link => link.Kind != LinkKind.Open && link.Edges.Count > 0))
        {
            var inner = GetInnerBoundary(link.Boundary);
            if (inner.Contains(link.Edges[0]) && inner.Contains(link.Edges[^1]) && IsClear(CollectionsMarshal.AsSpan(link.Edges)))
            {
                continue;
            }

            int start = Array.IndexOf(link.Boundary, link.Edges[0]);
            foreach (var span in new[] { inner, link.Boundary })
            {
                if (TryAlignLink(link, span, start))
                {
                    break;
                }
            }
        }
    }

    private bool TryAlignLink(LinkDraft link, EdgeKey[] boundary, int start)
    {
        int offset = Array.IndexOf(link.Boundary, boundary[0]);
        for (int width = link.Edges.Count; width >= 1; width--)
        {
            int[] spans = Enumerable.Range(0, boundary.Length - width + 1)
                .Where(i => IsClear(boundary.AsSpan(i, width)))
                .OrderBy(i => Math.Abs(i + offset - start))
                .ToArray();

            if (spans.Length > 0)
            {
                link.Edges = boundary.Skip(spans[0]).Take(width).ToList();
                return true;
            }
        }

        return false;
    }

    private bool IsClear(ReadOnlySpan<EdgeKey> span)
    {
        foreach (var edge in span)
        {
            foreach ((int a, int b) in edge.Axis == 'u' ? new[] { (edge.A, edge.B), (edge.A, edge.B + 1) } : new[] { (edge.A, edge.B), (edge.A + 1, edge.B) })
            {
                bool clear = edge.Axis == 'u'
                    ? Region(a - 1, b - 1) == Region(a - 1, b) && Region(a, b - 1) == Region(a, b)
                    : Region(a - 1, b - 1) == Region(a, b - 1) && Region(a - 1, b) == Region(a, b);
                if (!clear)
                {
                    return false;
                }
            }
        }

        return true;
        int Region(int u, int v) => _plan.Occupancy.GetValueOrDefault(new Cell(u, v), -1);
    }

    // Moves each basement door or arch closer than the least door gap to another one to the farthest clear span of its boundary
    private void SpaceDoors()
    {
        if (_plan.Settings.WallSet != WallSet.Basement)
        {
            return;
        }

        var doorLinks = _plan.Links.Where(link => link is { Kind: LinkKind.Door or LinkKind.Arch, Edges.Count: > 0 }).ToList();
        foreach (var link in doorLinks)
        {
            float current = MeasureDoorGap(doorLinks, link, link.Edges);
            if (current >= MinDoorGap)
            {
                continue;
            }

            var inner = GetInnerBoundary(link.Boundary);
            int width = link.Edges.Count;
            var spans = Enumerable.Range(0, inner.Length - width + 1)
                .Where(i => IsClear(inner.AsSpan(i, width)))
                .Select(i => (Index: i, Gap: MeasureDoorGap(doorLinks, link, inner.AsSpan(i, width).ToArray())))
                .OrderByDescending(span => span.Gap)
                .ToArray();

            if (spans.Length > 0 && spans[0].Gap > current)
            {
                link.Edges = inner.Skip(spans[0].Index).Take(width).ToList();
            }
        }
    }

    private static float MeasureDoorGap(List<LinkDraft> doorLinks, LinkDraft link, IReadOnlyList<EdgeKey> edges)
    {
        float gap = float.MaxValue;
        foreach (var other in doorLinks.Where(other => other != link))
        {
            foreach (var edge in edges)
            {
                var position = GetBasementDoorPosition(edge);
                foreach (var otherEdge in other.Edges)
                {
                    var otherPosition = GetBasementDoorPosition(otherEdge);
                    float dx = position.X - otherPosition.X;
                    float dy = position.Y - otherPosition.Y;
                    gap = MathF.Min(gap, MathF.Sqrt(dx * dx + dy * dy));
                }
            }
        }

        return gap;
    }

    private static PointF GetBasementDoorPosition(EdgeKey edge) =>
        edge.Axis == 'v' ? ConvertToBasementPixels(edge.A + 0.25f, edge.B + 0.25f) : ConvertToBasementPixels(edge.A - 0.25f, edge.B + 0.75f);

    private static PointF ConvertToBasementPixels(float a, float b) => new(Lattice.BasementStep.X * (a + b), Lattice.BasementStep.Y * (b - a));

    private static EdgeKey[] GetInnerBoundary(EdgeKey[] boundary) => boundary.Length > 2 ? boundary[1..^1] : boundary;

    private void AddJammedDoors(BuildingPlan building, Dictionary<int, int> regionOf)
    {
        if (_plan.Settings.WallSet != WallSet.Basement)
        {
            return;
        }

        var boundaries = FindBoundaries();
        var taken = _plan.Links
            .SelectMany(link => link.Edges)
            .ToHashSet();

        foreach ((int a, int b) in building.JammedWalls.Where(pair => regionOf.ContainsKey(pair.A) && regionOf.ContainsKey(pair.B)))
        {
            if (!boundaries.TryGetValue(new RoomPair(regionOf[a], regionOf[b]), out var edges))
            {
                continue;
            }

            var inner = GetInnerBoundary(GetLongestRun(edges));
            if (inner.Length == 0 || inner.Any(taken.Contains))
            {
                continue;
            }

            _plan.JammedDoors.Add(inner[inner.Length / 2]);
            taken.UnionWith(inner);
        }
    }

    private Dictionary<RoomPair, List<EdgeKey>> FindBoundaries()
    {
        var boundaries = new Dictionary<RoomPair, List<EdgeKey>>();
        foreach ((var unit, int area) in _plan.Occupancy.OrderBy(pair => pair.Key.U).ThenBy(pair => pair.Key.V))
        {
            foreach (var (next, edge) in EdgeKey.GetNeighbors(unit))
            {
                if (!_plan.Occupancy.TryGetValue(next, out int other) || other <= area)
                {
                    continue;
                }

                if (!boundaries.TryGetValue(new RoomPair(area, other), out var list))
                {
                    list = [];
                    boundaries[new RoomPair(area, other)] = list;
                }

                list.Add(edge);
            }
        }

        return boundaries;
    }

    private static EdgeKey[] GetLongestRun(List<EdgeKey> edges)
    {
        EdgeKey[] best = [];
        foreach (var line in edges.GroupBy(edge => (edge.Axis, Line: edge.Axis == 'v' ? edge.B : edge.A)))
        {
            var sorted = line
                .OrderBy(edge => edge.Axis)
                .ThenBy(edge => edge.A)
                .ThenBy(edge => edge.B)
                .ToArray();

            int first = 0;
            for (int index = 1; index <= sorted.Length; index++)
            {
                if (index == sorted.Length || Along(sorted[index]) != Along(sorted[index - 1]) + 1)
                {
                    if (index - first > best.Length)
                    {
                        best = sorted[first..index];
                    }

                    first = index;
                }
            }
        }

        return best;

        static int Along(EdgeKey edge) => edge.Axis == 'u' ? edge.B : edge.A;
    }

    // Cover units with each row's runs, merged with the runs of identical extent in the rows below
    private static List<UnitRect> DecomposeRects(HashSet<Cell> units)
    {
        var rects = new List<UnitRect>();
        var open = new Dictionary<(int U, int Width), (int V, int Height)>();
        for (int v = units.Min(unit => unit.V); v <= units.Max(unit => unit.V); v++)
        {
            var runs = new List<(int U, int Width)>();
            foreach (int u in units.Where(unit => unit.V == v).Select(unit => unit.U).Order())
            {
                if (runs.Count > 0 && runs[^1].U + runs[^1].Width == u)
                {
                    runs[^1] = (runs[^1].U, runs[^1].Width + 1);
                }
                else
                {
                    runs.Add((u, 1));
                }
            }

            foreach (var closed in open.Where(pair => !runs.Contains(pair.Key)).ToArray())
            {
                rects.Add(new UnitRect(closed.Key.U, closed.Value.V, closed.Key.Width, closed.Value.Height));
                open.Remove(closed.Key);
            }

            foreach (var run in runs)
            {
                open[run] = open.TryGetValue(run, out var started) ? (started.V, started.Height + 1) : (v, 1);
            }
        }

        rects.AddRange(open.Select(pair => new UnitRect(pair.Key.U, pair.Value.V, pair.Key.Width, pair.Value.Height)));
        return rects;
    }

    private sealed class PlanDraft
    {
        public PlanDraft(WallSetSettings settings) => Settings = settings;

        public WallSetSettings Settings { get; }

        public List<RegionDraft> Regions { get; } = [];

        public List<LinkDraft> Links { get; } = [];

        public List<EdgeKey> JammedDoors { get; } = [];

        public Dictionary<Cell, int> Occupancy { get; } = [];

        public List<RegionDraft> Stations { get; } = [];

        public GateKind? Gate { get; set; }

        public RegionDraft? KeyRoom { get; set; }

        public IReadOnlyList<RegionDraft> GeneratorRooms { get; set; } = [];

        public LinkDraft? BlackoutLink { get; set; }

        public RegionDraft? TriggerRoom { get; set; }

        public HashSet<RegionDraft> SecondWing { get; } = [];

        public HashSet<RegionDraft> LockedSiteRooms { get; } = [];

        public RegionDraft AddRegion(RegionKind kind, UnitRect rect, int station, int? host, bool isHall)
        {
            var region = new RegionDraft { Id = Regions.Count, Kind = kind, Station = station, Host = host, IsHall = isHall };
            Regions.Add(region);
            ExtendRegion(region, rect);
            return region;
        }

        public void ExtendRegion(RegionDraft region, UnitRect rect)
        {
            region.Rects.Add(rect);
            foreach (var unit in rect.Units)
            {
                Occupancy[unit] = region.Id;
                region.Units.Add(unit);
            }
        }

        public LayoutPlan Freeze()
        {
            var regions = Regions
                .Select(region => new PlanRegion
                {
                    Id = region.Id,
                    Kind = region.Kind,
                    Rects = region.Rects.ToArray(),
                    Units = region.Units,
                    Station = region.Station,
                    Host = region.Host,
                    IsHall = region.IsHall,
                    IsGated = region.IsGated,
                    Phase = region.Phase,
                    Order = region is { Order: int.MaxValue, Host: not null } ? Regions[region.Host.Value].Order : region.Order,
                    Role = region.Role,
                    Type = region.Type
                })
                .ToArray();

            var links = Links
                .Select((link, index) => new PlanLink { Id = index, From = link.From, To = link.To, Kind = link.Kind, Edges = link.Edges.ToArray(), Boundary = link.Boundary, IsChoke = link.IsChoke })
                .ToArray();

            return new LayoutPlan
            {
                Settings = Settings,
                Regions = regions,
                Links = links,
                JammedDoors = JammedDoors.ToArray(),
                Occupancy = Occupancy,
                Stations = Stations.Select(region => regions[region.Id]).ToArray(),
                Gate = Gate,
                KeyRoom = KeyRoom is null ? null : regions[KeyRoom.Id],
                GeneratorRooms = GeneratorRooms.Select(room => regions[room.Id]).ToArray(),
                BlackoutLink = BlackoutLink is null ? null : links[Links.IndexOf(BlackoutLink)],
                TriggerRoom = TriggerRoom is null ? null : regions[TriggerRoom.Id],
                SecondWing = SecondWing.Select(region => regions[region.Id]).ToHashSet(),
                LockedSiteRooms = LockedSiteRooms.Select(region => regions[region.Id]).ToHashSet()
            };
        }
    }

    private sealed class RegionDraft
    {
        public required int Id { get; init; }

        public required RegionKind Kind { get; set; }

        public List<UnitRect> Rects { get; } = [];

        public HashSet<Cell> Units { get; } = [];

        public required int Station { get; set; }

        public int? Host { get; set; }

        public bool IsHall { get; init; }

        public bool IsGated { get; set; }

        public int Phase { get; set; }

        public int Order { get; set; } = int.MaxValue;

        public MissionRole? Role { get; set; }

        public RoomType? Type { get; set; }
    }

    private sealed class LinkDraft
    {
        public required int From { get; init; }

        public required int To { get; init; }

        public required LinkKind Kind { get; set; }

        public required List<EdgeKey> Edges { get; set; }

        public required EdgeKey[] Boundary { get; init; }

        public bool IsChoke { get; set; }
    }
}
