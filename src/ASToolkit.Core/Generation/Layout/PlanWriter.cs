using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Layout;

// Write a built layout into a layout plan with regions in station order, links on the realized mission links, the gate from the mission's
// keys and locks, pens and closets
internal sealed class PlanWriter
{
    private const float CenteredDoorShare = 0.95f;
    private const float WideDoorShare = 0.45f;
    private const float MinDoorGap = 160;
    private const float WalkShare = 0.8f;
    private const int ClosetTries = 80;
    private const int FarClosetTries = 60;

    private static readonly (int U, int V)[] Headings = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private static readonly (int U, int V)[] FarHeadings = [(1, 0), (0, -1)];
    private static readonly (int W, int H)[] ClosetSizes = [(2, 3), (3, 2), (3, 3), (2, 2)];

    private readonly Random _rng;
    private readonly LayoutPlan _plan;

    public PlanWriter(LevelTheme theme, Random rng)
    {
        _rng = rng;
        _plan = new LayoutPlan(theme == LevelTheme.Lab ? ThemeSettings.Lab : ThemeSettings.Military);
    }

    public LayoutPlan Write(MissionGraph mission, BlockResult result, AreaRequest request)
    {
        _plan.Courts.UnionWith(result.Courts);
        var regionOf = AddRegions(mission, result);
        var gate = GetGate(mission);
        var locked = mission.Links
            .Where(link => link.IsLocked)
            .Select(link => Order(link.From, link.To))
            .ToHashSet();

        locked.UnionWith(result.Realized.Where(link => link.Kind == MissionLinkKind.Entrance).Select(link => Order(link.From, link.To)));

        var closing = mission.Progression.Seals
            .Where(seal => seal.Kind == SealKind.DoorsClose)
            .Select(seal => Order(seal.From, seal.To))
            .ToHashSet();

        var roles = mission.Links
            .GroupBy(link => Order(link.From, link.To))
            .ToDictionary(group => group.Key, group => group.First().Kind);

        var lockedLinks = AddLinks(result, regionOf, gate, locked, closing, roles);
        _plan.Gate = gate;
        MarkGated(regionOf[result.Start], lockedLinks);
        SetGateRooms(mission, regionOf, gate, lockedLinks);
        AddPens(request.Pens);
        foreach (var branch in mission.Progression.Phases.SelectMany(phase => phase.Branches))
        {
            var regions = branch.Places
                .Where(regionOf.ContainsKey)
                .Select(room => _plan.Regions[regionOf[room]])
                .ToList();

            if (regions.Count > 0)
            {
                _plan.Branches.Add(regions);
            }
        }

        var hosts = _plan.Regions.Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom).ToArray();
        AddClosets(request.SecretClosets, RegionKind.SecretCloset, LinkKind.SecretWall, hosts);
        AddClosets(request.SpawnClosets, RegionKind.SpawnCloset, LinkKind.SpawnWall, _plan.Stations.Skip(2).ToArray());
        AlignLinks();
        SpaceDoors();
        return _plan;
    }

    private static (int, int) Order(int a, int b) => a < b ? (a, b) : (b, a);

    private static GateKind? GetGate(MissionGraph mission)
    {
        var kinds = mission.Progression.Keys.Select(key => key.Kind).ToHashSet();
        return kinds.Contains(LandmarkKind.Generator) ? GateKind.Fences
            : kinds.Contains(LandmarkKind.BlackoutTrigger) ? GateKind.Blackout
            : kinds.Contains(LandmarkKind.Breaker) ? GateKind.LockedDoors
            : null;
    }

    // Add the stations along the critical paths in flow order, then the rooms hung off them, then the corridors
    private Dictionary<int, int> AddRegions(MissionGraph mission, BlockResult result)
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
        var flow = mission.Progression.Flow
            .Select((room, index) => (Room: room, Index: index))
            .GroupBy(entry => entry.Room)
            .ToDictionary(group => group.Key, group => group.First().Index);

        var regionOf = new Dictionary<int, int>();
        foreach (int room in order)
        {
            int station = stations.IndexOf(room);
            var region = AddRegion(result.Cells[room], station >= 0 ? RegionKind.Station : RegionKind.SideRoom, Math.Max(0, station), result.Halls.Contains(room));
            region.Room = room;
            region.Phase = result.PhaseOf.GetValueOrDefault(room);
            region.Flow = flow.TryGetValue(room, out int index) ? index : null;
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

        for (int index = 0; index < result.Corridors.Count; index++)
        {
            (var cells, int a, int b) = result.Corridors[index];
            var region = AddRegion(cells, RegionKind.SideRoom, _plan.Regions[regionOf[a]].Station, isHall: true);
            region.Host = regionOf[a];
            region.Phase = result.PhaseOf.GetValueOrDefault(b);
            regionOf[RoomTexturer.GetCorridorId(index)] = region.Id;
        }

        return regionOf;
    }

    private PlanRegion AddRegion(HashSet<Cell> cells, RegionKind kind, int station, bool isHall)
    {
        var rects = DecomposeRects(cells);
        var region = _plan.AddRegion(kind, rects[0], station, null, isHall);
        foreach (var rect in rects.Skip(1))
        {
            _plan.ExtendRegion(region, rect);
        }

        return region;
    }

    private HashSet<PlanLink> AddLinks(BlockResult result, Dictionary<int, int> regionOf, GateKind? gate, HashSet<(int, int)> locked, HashSet<(int, int)> closing,
        Dictionary<(int, int), MissionLinkKind> roles)
    {
        var lockedLinks = new HashSet<PlanLink>();
        var boundaries = FindBoundaries();
        foreach ((int from, int to, var kind) in result.Realized)
        {
            var pair = Order(from, to);
            bool isLocked = locked.Contains(pair);
            var linkKind = isLocked ? gate == GateKind.Fences ? LinkKind.Fence : LinkKind.Door : closing.Contains(pair) ? LinkKind.Door : LinkKind.Arch;
            bool allowDoor = gate is null || isLocked || closing.Contains(pair);
            var role = roles.TryGetValue(pair, out var found) ? found : kind;
            int corridor = result.Corridors.FindIndex(entry => Order(entry.A, entry.B) == pair);
            if (corridor >= 0)
            {
                int middle = regionOf[RoomTexturer.GetCorridorId(corridor)];
                var entry = AddLink(boundaries, regionOf[from], middle, linkKind, allowDoor, role);
                AddLink(boundaries, middle, regionOf[to], LinkKind.Arch, gate is null, role);
                if (isLocked && entry is not null)
                {
                    lockedLinks.Add(entry);
                }
            }
            else
            {
                var link = AddLink(boundaries, regionOf[from], regionOf[to], linkKind, allowDoor, role);
                if (isLocked && link is not null)
                {
                    lockedLinks.Add(link);
                }
            }
        }

        return lockedLinks;
    }

    private PlanLink? AddLink(Dictionary<(int, int), List<EdgeKey>> boundaries, int from, int to, LinkKind kind, bool allowDoor, MissionLinkKind role)
    {
        if (!boundaries.TryGetValue(Order(from, to), out var edges))
        {
            return null;
        }

        var boundary = GetLongestRun(edges);
        if (boundary.Length == 0)
        {
            return null;
        }

        var link = new PlanLink { From = from, To = to, Kind = kind, Edges = [], Boundary = boundary, Phase = _plan.Regions[to].Phase, Role = role };
        _plan.Links.Add(link);
        SetLinkKind(link, kind, allowDoor);
        return link;
    }

    // Gate every region the start reaches only through a lock
    private void MarkGated(int start, HashSet<PlanLink> lockedLinks)
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

    private void SetGateRooms(MissionGraph mission, Dictionary<int, int> regionOf, GateKind? gate, HashSet<PlanLink> lockedLinks)
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
        _plan.GateLink = _plan.Links
            .Where(link => lockedLinks.Contains(link) && _plan.Regions[link.From].IsGated != _plan.Regions[link.To].IsGated)
            .OrderBy(link => _plan.Regions[link.From].Kind == RegionKind.Station && _plan.Regions[link.To].Kind == RegionKind.Station ? 0 : 1)
            .FirstOrDefault();

        if (gate == GateKind.Fences)
        {
            var start = _plan.Stations[0].Rects[0];
            var walk = MeasureWalk([new Cell(start.U + start.W / 2, start.V + start.H / 2)]);
            float perUnit = WalkShare * _plan.Theme.UnitPixels;
            _plan.GeneratorRooms = keys
                .Select(room => (Room: room, WalkPixels: room.Units.Where(walk.ContainsKey).Select(unit => walk[unit]).DefaultIfEmpty(0).Max() * perUnit))
                .ToArray();
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
                var pair = Order(regionOf[seal.Value.From], regionOf[seal.Value.To]);
                _plan.BlackoutLink = _plan.Links.FirstOrDefault(link => Order(link.From, link.To) == pair);
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

    // Fence leaves of the gated part in as pens
    private void AddPens(int count)
    {
        var leaves = _plan.Regions
            .Where(region => region is { IsGated: true, Kind: RegionKind.SideRoom, IsHall: false } && _plan.Links.Count(link => link.From == region.Id || link.To == region.Id) == 1)
            .OrderBy(_ => _rng.Next())
            .Take(count)
            .ToArray();

        foreach (var pen in leaves)
        {
            pen.Kind = RegionKind.Pen;
            var entrance = _plan.Links.FirstOrDefault(link => link.To == pen.Id) ?? _plan.Links.First(link => link.From == pen.Id);
            SetLinkKind(entrance, LinkKind.Fence);
        }
    }

    private void AddClosets(int count, RegionKind kind, LinkKind linkKind, IReadOnlyList<PlanRegion> hosts)
    {
        if (hosts.Count == 0)
        {
            return;
        }

        int made = 0;
        for (int tries = 0; made < count && tries < count * ClosetTries; tries++)
        {
            var host = hosts[_rng.Next(hosts.Count)];
            var hr = host.Rects[_rng.Next(host.Rects.Count)];
            (int w, int h) = ClosetSizes[_rng.Next(ClosetSizes.Length)];
            // Open secret closets in the host's far walls first, where the camera sees the breakable wall
            var side = kind == RegionKind.SecretCloset && tries < count * FarClosetTries ? FarHeadings[_rng.Next(FarHeadings.Length)] : Headings[_rng.Next(4)];
            UnitRect? r = side switch
            {
                (1, 0) when hr.H - h - 1 >= 1 => new UnitRect(hr.U + hr.W, _rng.Next(hr.V + 1, hr.V + hr.H - h), w, h),
                (-1, 0) when hr.H - h - 1 >= 1 => new UnitRect(hr.U - w, _rng.Next(hr.V + 1, hr.V + hr.H - h), w, h),
                (0, 1) when hr.W - w - 1 >= 1 => new UnitRect(_rng.Next(hr.U + 1, hr.U + hr.W - w), hr.V + hr.H, w, h),
                (0, -1) when hr.W - w - 1 >= 1 => new UnitRect(_rng.Next(hr.U + 1, hr.U + hr.W - w), hr.V - h, w, h),
                _ => null
            };
            if (r is null || !_plan.IsFree(r.Value) || _plan.TouchesOtherAtCorner(r.Value, host.Id) || r.Value.Units.Any(_plan.Courts.Contains))
            {
                continue;
            }

            var shared = hr.GetSharedEdges(r.Value);
            bool hostSide = shared.All(k => new[] { k.Cells.First, k.Cells.Second }.Where(c => !r.Value.Contains(c)).All(c => _plan.Occupancy.GetValueOrDefault(c, -1) == host.Id));
            if (shared.Length < 2 || !hostSide)
            {
                continue;
            }

            var closet = _plan.AddRegion(kind, r.Value, host.Station, host.Id);
            closet.IsGated = host.IsGated;
            closet.Phase = host.Phase;
            _plan.Links.Add(new PlanLink { From = host.Id, To = closet.Id, Kind = linkKind, Edges = [shared[shared.Length / 2]], Boundary = shared, Phase = host.Phase });
            made++;
        }
    }

    private void SetLinkKind(PlanLink link, LinkKind kind, bool allowDoor = true)
    {
        var all = link.Boundary;
        var inner = GetInnerBoundary(all);
        bool lab = _plan.Theme.Theme == LevelTheme.Lab;
        if (kind == LinkKind.Arch && inner.Length < 1)
        {
            kind = lab && allowDoor ? LinkKind.Door : LinkKind.Open;
        }

        // Leave a wall stub beside military arches to keep each room's own walls
        int width = kind switch
        {
            LinkKind.Arch => Math.Min(_rng.Next(_plan.Theme.ArchWidth.Min, _plan.Theme.ArchWidth.Max + 1), lab ? inner.Length : Math.Max(1, inner.Length - 1)),
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
            if (lab && _rng.NextSingle() < CenteredDoorShare)
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

    private bool TryAlignLink(PlanLink link, EdgeKey[] boundary, int start)
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

    // Move each lab door or arch that stands closer than the least door gap to another one to the farthest clear span of its boundary
    private void SpaceDoors()
    {
        if (_plan.Theme.Theme != LevelTheme.Lab)
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

    private static float MeasureDoorGap(List<PlanLink> doorLinks, PlanLink link, IReadOnlyList<EdgeKey> edges)
    {
        float gap = float.MaxValue;
        foreach (var other in doorLinks.Where(other => other != link))
        {
            foreach (var edge in edges)
            {
                var position = GetLabDoorPosition(edge);
                foreach (var otherEdge in other.Edges)
                {
                    var otherPosition = GetLabDoorPosition(otherEdge);
                    float dx = position.X - otherPosition.X;
                    float dy = position.Y - otherPosition.Y;
                    gap = MathF.Min(gap, MathF.Sqrt(dx * dx + dy * dy));
                }
            }
        }

        return gap;
    }

    private static PointF GetLabDoorPosition(EdgeKey edge) =>
        edge.Axis == 'v' ? ConvertToLabPixels(edge.A + 0.25f, edge.B + 0.25f) : ConvertToLabPixels(edge.A - 0.25f, edge.B + 0.75f);

    private static PointF ConvertToLabPixels(float a, float b) => new(Lattice.LabStep.X * (a + b), Lattice.LabStep.Y * (b - a));

    private static EdgeKey[] GetInnerBoundary(EdgeKey[] boundary) => boundary.Length > 2 ? boundary[1..^1] : boundary;

    private Dictionary<Cell, int> MeasureWalk(IEnumerable<Cell> sources)
    {
        var passable = _plan.Links.Where(link => link.Kind is not (LinkKind.SecretWall or LinkKind.SpawnWall)).SelectMany(link => link.Edges).ToHashSet();
        var walk = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var source in sources.Where(_plan.Occupancy.ContainsKey))
        {
            walk[source] = 0;
            queue.Enqueue(source);
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            int region = _plan.Occupancy[cell];
            foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (walk.ContainsKey(next) || !_plan.Occupancy.TryGetValue(next, out int other) || (other != region && !passable.Contains(edge)))
                {
                    continue;
                }

                walk[next] = walk[cell] + 1;
                queue.Enqueue(next);
            }
        }

        return walk;
    }

    private Dictionary<(int, int), List<EdgeKey>> FindBoundaries()
    {
        var boundaries = new Dictionary<(int, int), List<EdgeKey>>();
        foreach ((var unit, int area) in _plan.Occupancy.OrderBy(pair => pair.Key.U).ThenBy(pair => pair.Key.V))
        {
            foreach (var (next, edge) in EdgeKey.GetNeighbors(unit))
            {
                if (!_plan.Occupancy.TryGetValue(next, out int other) || other <= area)
                {
                    continue;
                }

                if (!boundaries.TryGetValue((area, other), out var list))
                {
                    list = [];
                    boundaries[(area, other)] = list;
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
}
