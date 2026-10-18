using System.Runtime.InteropServices;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Provides the planning of a level layout around its main route.
/// </summary>
public static class LayoutPlanner
{
    private const double LabChainPerSpine = 0.65;
    private const double MilitaryChainPerSpine = 0.9;
    private const double MaxStationShare = 0.58;
    private const double WalkPerUnitStep = 0.8;
    private const double BranchShare = 0.6;
    private const double ForkChance = 0.35;
    private const double MaxAspectStretch = 0.6;
    private const int MaxPocketUnits = 2;
    private const int MaxBranchTries = 160;
    private const double BlackoutReach = 1700;
    private static readonly (int U, int V)[] Headings = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private static readonly (int W, int H)[] ClosetSizes = [(2, 3), (3, 2), (3, 3), (2, 2)];

    /// <summary>
    /// Plans a layout of about the target area inside a box that it fills to about the given share, with a main route
    /// as long as the given elongation times the square root of the area and no room walked farther than the maximum
    /// elongation allows, or returns null when the attempt boxed itself in.
    /// </summary>
    public static LayoutPlan? Plan(Random rng, ThemeSettings theme, int targetUnits, double fill, double elongation, double maxElongation, GateRequest? gate, int secretClosets, int spawnClosets, int shortcuts)
    {
        var plan = new LayoutPlan(theme);
        double spine = elongation * Math.Sqrt(targetUnits);
        GrowStations(rng, plan, (int)(targetUnits * MaxStationShare), spine * (theme.Theme == LevelTheme.Lab ? LabChainPerSpine : MilitaryChainPerSpine), targetUnits / fill);
        if (plan.Stations.Count < 5)
        {
            return null;
        }

        if (gate is not null && gate.IsWing && !GrowWing(rng, plan, (int)(targetUnits * gate.Share)))
        {
            return null;
        }

        // Walks cut corners and cover less than a unit per step. Leave room for a blackout's search walk past the trigger.
        double maxWalk = maxElongation * Math.Sqrt(targetUnits) / WalkPerUnitStep;
        if (gate is { Kind: GateKind.Blackout, IsWing: false })
        {
            maxWalk = Math.Max(maxWalk, (BlackoutReach + gate.SearchPixels) / (theme.UnitPixels * WalkPerUnitStep));
        }

        var walk = MeasureWalk(plan, [GetStartUnit(plan)], openOnly: false);
        var branches = GrowBranches(rng, plan, (int)((targetUnits - plan.Area) * BranchShare), walk, maxWalk);
        AddSideRooms(rng, plan, targetUnits, branches, walk, maxWalk);
        if (gate is not null && !(gate.IsWing ? PlaceWingGate(rng, plan, gate) : PlaceGate(rng, plan, gate)))
        {
            return null;
        }

        AddClosets(rng, plan, secretClosets, RegionKind.SecretCloset, LinkKind.SecretWall, plan.Regions.Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom).ToArray());
        AddClosets(rng, plan, spawnClosets, RegionKind.SpawnCloset, LinkKind.SpawnWall, plan.Stations.Skip(1).Take(Math.Max(1, plan.Stations.Count / 2)).ToArray());
        AddShortcuts(rng, plan, shortcuts);
        FillPockets(plan);
        AlignLinks(plan);
        return plan;
    }

    // Merge small enclosed holes, which can't take a roof tile, into the room beside them
    private static void FillPockets(LayoutPlan plan)
    {
        int u0 = plan.Occupancy.Keys.Min(c => c.U) - 1;
        int u1 = plan.Occupancy.Keys.Max(c => c.U) + 1;
        int v0 = plan.Occupancy.Keys.Min(c => c.V) - 1;
        int v1 = plan.Occupancy.Keys.Max(c => c.V) + 1;
        var outside = new HashSet<Cell> { new(u0, v0) };
        var queue = new Queue<Cell>(outside);
        while (queue.Count > 0)
        {
            foreach (var (n, _) in EdgeKey.GetNeighbors(queue.Dequeue()))
            {
                if (IsInside(n) && !plan.Occupancy.ContainsKey(n) && outside.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        var seen = new HashSet<Cell>();
        for (int u = u0; u <= u1; u++)
        {
            for (int v = v0; v <= v1; v++)
            {
                var start = new Cell(u, v);
                if (plan.Occupancy.ContainsKey(start) || outside.Contains(start) || !seen.Add(start))
                {
                    continue;
                }

                var pocket = new List<Cell> { start };
                for (int cellIndex = 0; cellIndex < pocket.Count; cellIndex++)
                {
                    foreach (var (n, _) in EdgeKey.GetNeighbors(pocket[cellIndex]))
                    {
                        if (!plan.Occupancy.ContainsKey(n) && seen.Add(n))
                        {
                            pocket.Add(n);
                        }
                    }
                }

                if (pocket.Count <= MaxPocketUnits)
                {
                    var host = GetPocketHost(plan, pocket);
                    foreach (var cell in pocket)
                    {
                        plan.Occupancy[cell] = host.Id;
                        host.Units.Add(cell);
                    }
                }
            }
        }

        return;

        bool IsInside(Cell c) => c.U >= u0 && c.U <= u1 && c.V >= v0 && c.V <= v1;
    }

    private static PlanRegion GetPocketHost(LayoutPlan plan, List<Cell> pocket)
    {
        var shared = new Dictionary<int, int>();
        foreach (var cell in pocket)
        {
            foreach (var (n, _) in EdgeKey.GetNeighbors(cell))
            {
                if (plan.Occupancy.TryGetValue(n, out int region))
                {
                    shared[region] = shared.GetValueOrDefault(region) + 1;
                }
            }
        }

        return shared.OrderBy(p => plan.Regions[p.Key].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset ? 1 : 0)
            .ThenByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => plan.Regions[p.Key]).First();
    }

    /// <summary>
    /// Returns the walking length along the main route between two links, in pixels.
    /// </summary>
    public static double MeasureChain(LayoutPlan plan, PlanLink from, PlanLink to)
    {
        int a = plan.Stations.FindIndex(s => s.Id == from.To);
        int b = plan.Stations.FindIndex(s => s.Id == to.From);
        if (a < 0 || b < a)
        {
            return 0;
        }

        double units = 0;
        var previous = GetMidpoint(from.Edges);
        for (int index = a; index <= b; index++)
        {
            var exit = index == b ? to : GetChainLink(plan, index);
            var next = GetMidpoint(exit.Edges);
            units += Math.Abs(next.U - previous.U) + Math.Abs(next.V - previous.V);
            previous = next;
        }

        return units * plan.Theme.UnitPixels;
    }

    private static PlanLink GetChainLink(LayoutPlan plan, int station) =>
        plan.Links.First(l => l.From == plan.Stations[station].Id && l.To == plan.Stations[station + 1].Id);

    private static (double U, double V) GetMidpoint(IReadOnlyList<EdgeKey> edges)
    {
        double u = 0;
        double v = 0;
        foreach (var edge in edges)
        {
            (double eu, double ev) = edge.Axis == 'u' ? (edge.A, edge.B + .5) : (edge.A + .5, (double)edge.B);
            u += eu;
            v += ev;
        }

        return (u / edges.Count, v / edges.Count);
    }

    private static int Next(Random rng, (int Min, int Max) range) => rng.Next(range.Min, range.Max + 1);

    private static void GrowStations(Random rng, LayoutPlan plan, int stationUnits, double chainUnits, double boxUnits)
    {
        var t = plan.Theme;
        var first = new UnitRect(0, 0, Next(rng, (t.RoomSize.Min, (t.RoomSize.Min + t.RoomSize.Max) / 2)), Next(rng, (t.RoomSize.Min, (t.RoomSize.Min + t.RoomSize.Max) / 2)));
        double aspect = 1 + MaxAspectStretch * rng.NextDouble();
        int boxW = Math.Max(first.W + 2, (int)Math.Round(Math.Sqrt(boxUnits * aspect)));
        int boxH = Math.Max(first.H + 2, (int)Math.Round(boxUnits / boxW));
        if (rng.Next(2) == 0)
        {
            (boxW, boxH) = (boxH, boxW);
        }

        plan.Bounds = new UnitRect(-rng.Next(boxW - first.W + 1), -rng.Next(boxH - first.H + 1), boxW, boxH);
        plan.Stations.Add(plan.AddRegion(RegionKind.Station, first, 0, null));
        int heading = rng.Next(4);
        int area = first.Area;
        double chain = 0;
        var entry = (U: first.U + first.W / 2.0, V: first.V + first.H / 2.0);
        for (int tries = 0; area < stationUnits && chain < chainUnits && tries < 400; tries++)
        {
            var previous = plan.Stations[^1];
            bool isHall = !previous.IsHall || rng.NextDouble() < 0.3;
            int length = isHall ? Next(rng, t.HallLength) : Next(rng, t.RoomSize);
            int width = isHall ? Next(rng, t.HallWidth) : Next(rng, t.RoomSize);
            int turn = rng.NextDouble() < 0.35 ? rng.Next(2) == 0 ? 1 : 3 : 0;
            UnitRect? placed = null;
            foreach (int candidateHeading in new[] { (heading + turn) % 4, heading, (heading + 1) % 4, (heading + 3) % 4 })
            {
                placed = FindStationRect(rng, plan, previous, Headings[candidateHeading], length, width);
                if (placed is not null)
                {
                    heading = candidateHeading;
                    break;
                }
            }

            if (placed is null)
            {
                break;
            }

            var station = plan.AddRegion(RegionKind.Station, placed.Value, plan.Stations.Count, null, isHall);
            plan.Stations.Add(station);
            area += placed.Value.Area;
            var kind = plan.Theme.Theme == LevelTheme.Lab
                ? Pick(rng, (LinkKind.Door, 0.45), (LinkKind.Arch, 0.45), (LinkKind.Open, 0.10))
                : Pick(rng, (LinkKind.Arch, 0.8), (LinkKind.Open, 0.2));
            AddLink(rng, plan, previous, station, previous.Rects[0].GetSharedEdges(placed.Value), kind);
            var exit = GetMidpoint(plan.Links[^1].Boundary);
            chain += Math.Abs(exit.U - entry.U) + Math.Abs(exit.V - entry.V);
            entry = exit;
        }
    }

    private static UnitRect? FindStationRect(Random rng, LayoutPlan plan, PlanRegion previous, (int U, int V) heading, int length, int width)
    {
        var p = previous.Rects[0];
        int ms = plan.Theme.MinShared;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            UnitRect r;
            if (heading.U != 0)
            {
                int v = rng.Next(p.V - width + ms, p.V + p.H - ms + 1);
                r = new UnitRect(heading.U > 0 ? p.U + p.W : p.U - length, v, length, width);
            }
            else
            {
                int u = rng.Next(p.U - width + ms, p.U + p.W - ms + 1);
                r = new UnitRect(u, heading.V > 0 ? p.V + p.H : p.V - length, width, length);
            }

            if (p.GetSharedEdges(r).Length >= ms && plan.IsFree(r))
            {
                return r;
            }
        }

        return null;
    }

    private static PlanRegion[] GrowBranches(Random rng, LayoutPlan plan, int branchUnits, Dictionary<Cell, int> walk, double maxWalk)
    {
        var t = plan.Theme;
        var all = new List<PlanRegion>();
        int count = Math.Clamp(branchUnits / (4 * t.RoomSize.Max * t.RoomSize.Max), 1, 3);
        int[] hosts = Enumerable.Range(1, Math.Max(1, plan.Stations.Count - 2)).OrderBy(_ => rng.Next()).ToArray();
        int grown = 0;
        foreach (int host in hosts)
        {
            if (grown >= count)
            {
                break;
            }

            var branch = new List<PlanRegion>();
            int area = 0;
            int budget = (branchUnits - all.Sum(r => r.Units.Count)) / (count - grown);
            for (int tries = 0; area < budget && tries < MaxBranchTries; tries++)
            {
                var previous = branch.Count == 0 ? plan.Stations[host] : rng.NextDouble() < ForkChance ? branch[rng.Next(branch.Count)] : branch[^1];
                bool isHall = branch.Count > 0 && !previous.IsHall && rng.NextDouble() < 0.4;
                int length = isHall ? Next(rng, t.HallLength) : Next(rng, t.RoomSize);
                int width = isHall ? Next(rng, t.HallWidth) : Next(rng, t.RoomSize);
                UnitRect? placed = null;
                foreach (int candidateHeading in Enumerable.Range(0, 4).OrderBy(_ => rng.Next()))
                {
                    placed = FindStationRect(rng, plan, previous, Headings[candidateHeading], length, width);
                    if (placed is not null)
                    {
                        break;
                    }
                }

                if (placed is null)
                {
                    continue;
                }

                var region = plan.AddRegion(RegionKind.SideRoom, placed.Value, host, previous.Id, isHall);
                var kind = t.Theme == LevelTheme.Lab
                    ? Pick(rng, (LinkKind.Door, branch.Count == 0 ? 0.6 : 0.4), (LinkKind.Arch, 0.5), (LinkKind.Open, branch.Count == 0 ? 0 : 0.1))
                    : Pick(rng, (LinkKind.Arch, 0.8), (LinkKind.Open, 0.2));
                AddLink(rng, plan, previous, region, previous.Rects[0].GetSharedEdges(placed.Value), kind);
                if (!TryExtendWalk(plan, walk, region, maxWalk))
                {
                    continue;
                }

                branch.Add(region);
                area += placed.Value.Area;
            }

            if (branch.Count > 0)
            {
                plan.Branches.Add(branch);
                all.AddRange(branch);
                grown++;
            }
        }

        return all.ToArray();
    }

    private static void AddSideRooms(Random rng, LayoutPlan plan, int targetUnits, IReadOnlyList<PlanRegion> branches, Dictionary<Cell, int> walk, double maxWalk)
    {
        var t = plan.Theme;
        for (int tries = 0; plan.Area < targetUnits && tries < 6000; tries++)
        {
            var hosts = plan.Stations
                .Concat(plan.Stations)
                .Concat(plan.Stations)
                .Concat(branches)
                .Concat(plan.Regions.Where(r => r.Kind == RegionKind.SideRoom && plan.Regions[r.Host!.Value].Kind == RegionKind.Station))
                .ToArray();
            
            var host = hosts[rng.Next(hosts.Length)];
            var room = TryAttachRoom(rng, plan, host, Next(rng, t.RoomSize), Next(rng, t.RoomSize), RegionKind.SideRoom);
            if (room is null || !TryExtendWalk(plan, walk, room, maxWalk))
            {
                continue;
            }

            int shared = plan.Links[^1].Boundary.Length;
            var kind = t.Theme == LevelTheme.Lab
                ? Pick(rng, (LinkKind.Door, 0.62), (LinkKind.Arch, 0.3), (LinkKind.Open, host.Kind == RegionKind.SideRoom ? 0.08 : 0))
                : Pick(rng, (LinkKind.Arch, 0.8), (LinkKind.Open, shared <= 6 ? 0.2 : 0));
            SetLinkKind(rng, plan, plan.Links[^1], kind);
        }
    }

    private static PlanRegion? TryAttachRoom(Random rng, LayoutPlan plan, PlanRegion host, int w, int h, RegionKind kind)
    {
        var hr = host.Rects[rng.Next(host.Rects.Count)];
        var side = Headings[rng.Next(4)];
        var r = side switch
        {
            (1, 0) => new UnitRect(hr.U + hr.W, rng.Next(hr.V - h + 3, Math.Max(hr.V - h + 3, hr.V + hr.H - 3) + 1), w, h),
            (-1, 0) => new UnitRect(hr.U - w, rng.Next(hr.V - h + 3, Math.Max(hr.V - h + 3, hr.V + hr.H - 3) + 1), w, h),
            (0, 1) => new UnitRect(rng.Next(hr.U - w + 3, Math.Max(hr.U - w + 3, hr.U + hr.W - 3) + 1), hr.V + hr.H, w, h),
            _ => new UnitRect(rng.Next(hr.U - w + 3, Math.Max(hr.U - w + 3, hr.U + hr.W - 3) + 1), hr.V - h, w, h)
        };
        var shared = hr.GetSharedEdges(r);
        if (!plan.IsFree(r) || shared.Length < 3)
        {
            return null;
        }

        var room = plan.AddRegion(kind, r, host.Station, host.Id);
        AddLink(rng, plan, host, room, shared, LinkKind.Arch, allowDoor: false);
        return room;
    }

    private static void AddLink(Random rng, LayoutPlan plan, PlanRegion from, PlanRegion to, EdgeKey[] boundary, LinkKind kind, bool allowDoor = true)
    {
        var link = new PlanLink { From = from.Id, To = to.Id, Kind = kind, Edges = [], Boundary = boundary };
        plan.Links.Add(link);
        SetLinkKind(rng, plan, link, kind, allowDoor);
    }

    private static void SetLinkKind(Random rng, LayoutPlan plan, PlanLink link, LinkKind kind, bool allowDoor = true)
    {
        var all = link.Boundary;
        var inner = all.Length > 2 ? all[1..^1] : all;
        if (kind == LinkKind.Arch && inner.Length < 2)
        {
            kind = plan.Theme.Theme == LevelTheme.Lab && allowDoor ? LinkKind.Door : LinkKind.Open;
        }

        int width = kind switch
        {
            LinkKind.Arch => Math.Max(2, Math.Min(Next(rng, plan.Theme.ArchWidth), inner.Length)),
            LinkKind.Door => inner.Length >= 2 && rng.NextDouble() < 0.45 ? 2 : 1,
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
            int start = rng.Next(0, inner.Length - width + 1);
            link.Edges = inner.Skip(start).Take(width).ToList();
        }
    }

    // Move each doorway to the nearest span of its boundary where no other wall meets it
    private static void AlignLinks(LayoutPlan plan)
    {
        foreach (var link in plan.Links.Where(l => l.Kind != LinkKind.Open && l.Edges.Count > 0))
        {
            if (IsClear(plan, CollectionsMarshal.AsSpan(link.Edges)))
            {
                continue;
            }

            int start = Array.IndexOf(link.Boundary, link.Edges[0]);
            for (int width = link.Edges.Count; width >= 1; width--)
            {
                int[] spans = Enumerable.Range(0, link.Boundary.Length - width + 1)
                    .Where(i => IsClear(plan, link.Boundary.AsSpan(i, width)))
                    .OrderBy(i => Math.Abs(i - start))
                    .ToArray();
                if (spans.Length > 0)
                {
                    link.Edges = link.Boundary.Skip(spans[0]).Take(width).ToList();
                    break;
                }
            }
        }
    }

    private static bool IsClear(LayoutPlan plan, ReadOnlySpan<EdgeKey> span)
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
        int Region(int u, int v) => plan.Occupancy.GetValueOrDefault(new Cell(u, v), -1);
    }

    private static LinkKind Pick(Random rng, params (LinkKind Kind, double Weight)[] choices)
    {
        double roll = rng.NextDouble() * choices.Sum(c => c.Weight);
        foreach ((var kind, double weight) in choices)
        {
            roll -= weight;
            if (roll < 0)
            {
                return kind;
            }
        }

        return choices[^1].Kind;
    }

    private static bool PlaceGate(Random rng, LayoutPlan plan, GateRequest gate)
    {
        int count = plan.Stations.Count;
        int cut;
        int blackout = 0;
        if (gate.Kind == GateKind.Blackout)
        {
            // Put the trigger on the last chain link within reach of the start
            var first = GetChainLink(plan, 0);
            double startOffset = first.Edges.Count > 0 ? plan.Theme.UnitPixels * (plan.Stations[0].Rects[0].W + plan.Stations[0].Rects[0].H) / 4 : 0;
            blackout = Enumerable.Range(0, count - 3).Where(b => startOffset + MeasureChain(plan, first, GetChainLink(plan, b)) <= BlackoutReach).DefaultIfEmpty(0).Max();
        }

        if (gate is { Kind: GateKind.Blackout, SearchPixels: > 0 })
        {
            var trigger = GetChainLink(plan, blackout);
            cut = Enumerable.Range(blackout + 1, count - 2 - blackout).OrderBy(k => Math.Abs(MeasureChain(plan, trigger, GetChainLink(plan, k)) - gate.SearchPixels)).First();
        }
        else
        {
            int[] cuts = Enumerable.Range(Math.Max(1, blackout + 1), count - 1 - Math.Max(1, blackout + 1)).ToArray();
            cut = cuts.OrderBy(k => Math.Abs(BehindShare(k) - gate.Share)).First();
            if (gate.Kind == GateKind.Fences)
            {
                cut = cuts.Where(k => Math.Abs(BehindShare(k) - gate.Share) <= 0.08).DefaultIfEmpty(cut).Max();
            }
        }

        foreach (var region in plan.Regions)
        {
            region.IsGated = region.Station > cut;
        }

        var gateLink = GetChainLink(plan, cut);
        plan.Gate = gate.Kind;
        plan.GateLink = gateLink;
        if (gate.Kind == GateKind.Fences)
        {
            SetLinkKind(rng, plan, gateLink, LinkKind.Fence);
            return PlaceGeneratorRoom(rng, plan, cut);
        }

        // Turn the open part's doors into arches because every door locks
        foreach (var link in plan.Links)
        {
            if (link != gateLink && link.Kind == LinkKind.Door && !plan.Regions[link.To].IsGated)
            {
                SetLinkKind(rng, plan, link, LinkKind.Arch, allowDoor: false);
            }
        }

        SetLinkKind(rng, plan, gateLink, LinkKind.Door);
        if (gate.Kind == GateKind.Blackout)
        {
            plan.BlackoutLink = GetChainLink(plan, blackout);
        }

        var hosts = new[] { cut, cut - 1, cut - 2 }.Where(k => k >= 1).Select(k => plan.Stations[k]).ToArray();
        if (gate is { Kind: GateKind.Blackout, SearchPixels: > 0 })
        {
            // Hang the breaker room off the open room that lies the search walk from the trigger
            var trigger = plan.BlackoutLink!.Edges.SelectMany(e => new[] { e.Cells.First, e.Cells.Second }).Where(plan.Occupancy.ContainsKey).Distinct();
            var walk = MeasureWalk(plan, trigger, openOnly: true);
            double Farthest(PlanRegion r) => r.Units.Where(walk.ContainsKey).Select(c => walk[c]).DefaultIfEmpty(0).Max() * plan.Theme.UnitPixels * WalkPerUnitStep;
            hosts = plan.Regions.Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom && !r.IsGated && r.Station > blackout)
                .OrderBy(r => Math.Abs(Farthest(r) - gate.SearchPixels)).Take(6).ToArray();
        }

        foreach (var host in hosts)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var room = TryAttachRoom(rng, plan, host, Next(rng, (4, 6)), Next(rng, (4, 6)), RegionKind.SideRoom);
                if (room is not null)
                {
                    plan.KeyRoom = room;
                    return true;
                }
            }
        }

        return false;
        
        double BehindShare(int k) => plan.Regions.Where(r => r.Station > k).Sum(r => r.Units.Count) / (double)plan.Area;
    }

    private static Dictionary<Cell, int> MeasureWalk(LayoutPlan plan, IEnumerable<Cell> sources, bool openOnly)
    {
        var passable = plan.Links.Where(l => l.Kind is not (LinkKind.SecretWall or LinkKind.SpawnWall)).SelectMany(l => l.Edges).ToHashSet();
        var walk = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var source in sources)
        {
            walk[source] = 0;
            queue.Enqueue(source);
        }

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            int region = plan.Occupancy[c];
            foreach (var (n, edge) in EdgeKey.GetNeighbors(c))
            {
                if (walk.ContainsKey(n) || !plan.Occupancy.TryGetValue(n, out int other) || (openOnly && plan.Regions[other].IsGated) || other != region && !passable.Contains(edge))
                {
                    continue;
                }

                walk[n] = walk[c] + 1;
                queue.Enqueue(n);
            }
        }

        return walk;
    }

    private static Cell GetStartUnit(LayoutPlan plan)
    {
        var r = plan.Stations[0].Rects[0];
        return new Cell(r.U + r.W / 2, r.V + r.H / 2);
    }

    // Undo the last region and link if any of its units lies past the walk cap
    private static bool TryExtendWalk(LayoutPlan plan, Dictionary<Cell, int> walk, PlanRegion region, double maxWalk)
    {
        var added = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var edge in plan.Links[^1].Edges)
        {
            var (inside, outside) = region.Units.Contains(edge.Cells.First) ? (edge.Cells.First, edge.Cells.Second) : (edge.Cells.Second, edge.Cells.First);
            if (walk.TryGetValue(outside, out int d) && d + 1 < added.GetValueOrDefault(inside, int.MaxValue))
            {
                added[inside] = d + 1;
                queue.Enqueue(inside);
            }
        }

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var (n, _) in EdgeKey.GetNeighbors(c))
            {
                if (region.Units.Contains(n) && added[c] + 1 < added.GetValueOrDefault(n, int.MaxValue))
                {
                    added[n] = added[c] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        if (added.Count == region.Units.Count && added.Values.Max() <= maxWalk)
        {
            foreach ((var cell, int distance) in added)
            {
                walk[cell] = distance;
            }

            return true;
        }

        foreach (var unit in region.Units)
        {
            plan.Occupancy.Remove(unit);
        }

        plan.Links.RemoveAt(plan.Links.Count - 1);
        plan.Regions.RemoveAt(plan.Regions.Count - 1);
        return false;
    }

    private static bool GrowWing(Random rng, LayoutPlan plan, int wingUnits)
    {
        var t = plan.Theme;
        var start = GetChainLink(plan, 0);
        double chain = MeasureChain(plan, start, GetChainLink(plan, plan.Stations.Count - 2));
        int[] hosts = Enumerable.Range(1, plan.Stations.Count - 3)
            .OrderBy(k => Math.Abs(MeasureChain(plan, start, GetChainLink(plan, k)) - 0.3 * chain)).Take(4).ToArray();
        foreach (int innerHost in hosts)
        {
            var host = plan.Stations[innerHost];
            var wing = new List<PlanRegion>();
            int area = 0;
            var previous = host;
            for (int tries = 0; area < wingUnits && tries < 60; tries++)
            {
                bool isHall = wing.Count == 0 || !previous.IsHall;
                int length = isHall ? Next(rng, t.HallLength) : Next(rng, t.RoomSize);
                int width = isHall ? Next(rng, t.HallWidth) : Next(rng, t.RoomSize);
                UnitRect? placed = null;
                foreach (int candidateHeading in Enumerable.Range(0, 4).OrderBy(_ => rng.Next()))
                {
                    placed = FindStationRect(rng, plan, previous, Headings[candidateHeading], length, width);
                    if (placed is not null)
                    {
                        break;
                    }
                }

                if (placed is null)
                {
                    break;
                }

                var region = plan.AddRegion(RegionKind.Wing, placed.Value, innerHost, previous.Id, isHall);
                region.IsGated = true;
                AddLink(rng, plan, previous, region, previous.Rects[0].GetSharedEdges(placed.Value), wing.Count == 0 ? LinkKind.Door : LinkKind.Arch);
                wing.Add(region);
                area += placed.Value.Area;
                previous = region;
            }

            if (area >= wingUnits * 0.6)
            {
                return true;
            }

            if (wing.Count == 0)
            {
                continue;
            }

            foreach (var region in wing)
            {
                foreach (var unit in region.Units)
                {
                    plan.Occupancy.Remove(unit);
                }
            }

            plan.Links.RemoveAll(l => wing.Any(r => r.Id == l.To));
            plan.Regions.RemoveRange(wing[0].Id, plan.Regions.Count - wing[0].Id);
        }

        return false;
    }

    private static bool PlaceWingGate(Random rng, LayoutPlan plan, GateRequest gate)
    {
        var first = plan.Regions.First(r => r.Kind == RegionKind.Wing);
        var gateLink = plan.Links.First(l => l.To == first.Id);
        var host = plan.Regions[gateLink.From];
        foreach (var region in plan.Regions)
        {
            region.IsGated = region.Kind == RegionKind.Wing || (region.Host is not null && plan.Regions[region.Host.Value].Kind == RegionKind.Wing);
        }

        plan.Gate = gate.Kind;
        plan.GateLink = gateLink;
        if (gate.Kind == GateKind.Fences)
        {
            SetLinkKind(rng, plan, gateLink, LinkKind.Fence);
        }
        else
        {
            foreach (var link in plan.Links)
            {
                if (link != gateLink && link.Kind == LinkKind.Door && !plan.Regions[link.To].IsGated)
                {
                    SetLinkKind(rng, plan, link, LinkKind.Arch, allowDoor: false);
                }
            }

            SetLinkKind(rng, plan, gateLink, LinkKind.Door);
        }

        if (gate.Kind == GateKind.Blackout)
        {
            plan.BlackoutLink = GetChainLink(plan, 0);
        }

        for (int attempt = 0; attempt < 80; attempt++)
        {
            var room = TryAttachRoom(rng, plan, host, Next(rng, (4, 6)), Next(rng, (4, 6)), RegionKind.SideRoom);
            if (room is not null)
            {
                plan.KeyRoom = room;
                return true;
            }
        }

        return false;
    }

    private static bool PlaceGeneratorRoom(Random rng, LayoutPlan plan, int cut)
    {
        var start = GetChainLink(plan, 0);
        double chain = MeasureChain(plan, start, GetChainLink(plan, plan.Stations.Count - 2));
        var gateLink = plan.GateLink!;
        int[] candidates = Enumerable.Range(1, cut)
            .Where(k => MeasureChain(plan, GetChainLink(plan, k - 1), gateLink) >= 900)
            .OrderBy(k => MeasureChain(plan, start, GetChainLink(plan, k - 1)) < 0.1 * chain ? 1 : 0)
            .ThenBy(k => Math.Abs(MeasureChain(plan, start, GetChainLink(plan, k - 1)) - 0.15 * chain))
            .ToArray();
        int size = plan.Theme.Theme == LevelTheme.Lab ? 5 : 3;
        foreach (int candidate in candidates)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var room = TryAttachRoom(rng, plan, plan.Stations[candidate], Next(rng, (size, size + 2)), Next(rng, (size, size + 2)), RegionKind.SideRoom);
                if (room is not null)
                {
                    plan.KeyRoom = room;
                    return true;
                }
            }
        }

        return false;
    }

    // Add breakable walls between touching open rooms at least three links apart
    private static void AddShortcuts(Random rng, LayoutPlan plan, int count)
    {
        if (count == 0)
        {
            return;
        }

        var boundaries = new Dictionary<(int, int), List<EdgeKey>>();
        foreach ((var unit, int area) in plan.Occupancy.OrderBy(p => p.Key.U).ThenBy(p => p.Key.V))
        {
            foreach (var (n, k) in EdgeKey.GetNeighbors(unit))
            {
                if (!plan.Occupancy.TryGetValue(n, out int b) || b <= area)
                {
                    continue;
                }

                if (!boundaries.TryGetValue((area, b), out var list))
                {
                    list = [];
                    boundaries[(area, b)] = list;
                }

                list.Add(k);
            }
        }

        int blackout = plan.BlackoutLink is null ? int.MaxValue : plan.Regions[plan.BlackoutLink.From].Station;
        var candidates = boundaries
            .Where(p => p.Value.Count >= 3 && IsOpenRoom(plan.Regions[p.Key.Item1]) && IsOpenRoom(plan.Regions[p.Key.Item2]) && IsSameSide(p.Key.Item1, p.Key.Item2)
                        && CountHops(plan, p.Key.Item1, p.Key.Item2) >= 3)
            .ToArray();
        
        rng.Shuffle(candidates);
        
        foreach (((int a, int b), var edges) in candidates.Take(count))
        {
            var sorted = edges.OrderBy(k => k.Axis).ThenBy(k => k.A).ThenBy(k => k.B).ToArray();
            plan.Links.Add(new PlanLink { From = a, To = b, Kind = LinkKind.SecretWall, Edges = [sorted[sorted.Length / 2]], Boundary = sorted });
        }

        return;

        bool IsSameSide(int a, int b) => plan.Regions[a].Station > blackout == plan.Regions[b].Station > blackout;
        
        bool IsOpenRoom(PlanRegion r) => r.Kind is RegionKind.Station or RegionKind.SideRoom && !r.IsGated && r != plan.KeyRoom;
    }

    private static int CountHops(LayoutPlan plan, int from, int to)
    {
        var hops = new Dictionary<int, int> { [from] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int r = queue.Dequeue();
            foreach (var link in plan.Links)
            {
                int other = link.From == r ? link.To : link.To == r ? link.From : -1;
                if (other >= 0 && link.Kind is not (LinkKind.SecretWall or LinkKind.SpawnWall) && hops.TryAdd(other, hops[r] + 1))
                {
                    queue.Enqueue(other);
                }
            }
        }

        return hops.GetValueOrDefault(to, int.MaxValue);
    }

    private static void AddClosets(Random rng, LayoutPlan plan, int count, RegionKind kind, LinkKind linkKind, IReadOnlyList<PlanRegion> hosts)
    {
        if (hosts.Count == 0)
        {
            return;
        }

        int made = 0;
        for (int tries = 0; made < count && tries < count * 80; tries++)
        {
            var host = hosts[rng.Next(hosts.Count)];
            var hr = host.Rects[rng.Next(host.Rects.Count)];
            (int w, int h) = ClosetSizes[rng.Next(ClosetSizes.Length)];
            var side = Headings[rng.Next(4)];
            UnitRect? r = side switch
            {
                (1, 0) when hr.H - h - 1 >= 1 => new UnitRect(hr.U + hr.W, rng.Next(hr.V + 1, hr.V + hr.H - h), w, h),
                (-1, 0) when hr.H - h - 1 >= 1 => new UnitRect(hr.U - w, rng.Next(hr.V + 1, hr.V + hr.H - h), w, h),
                (0, 1) when hr.W - w - 1 >= 1 => new UnitRect(rng.Next(hr.U + 1, hr.U + hr.W - w), hr.V + hr.H, w, h),
                (0, -1) when hr.W - w - 1 >= 1 => new UnitRect(rng.Next(hr.U + 1, hr.U + hr.W - w), hr.V - h, w, h),
                _ => null
            };
            if (r is null || !plan.IsFree(r.Value))
            {
                continue;
            }

            var shared = hr.GetSharedEdges(r.Value);
            bool hostSide = shared.All(k => new[] { k.Cells.First, k.Cells.Second }.Where(c => !r.Value.Contains(c)).All(c => plan.Occupancy.GetValueOrDefault(c, -1) == host.Id));
            if (shared.Length < 2 || !hostSide)
            {
                continue;
            }

            var closet = plan.AddRegion(kind, r.Value, host.Station, host.Id);
            closet.IsGated = host.IsGated;
            plan.Links.Add(new PlanLink { From = host.Id, To = closet.Id, Kind = linkKind, Edges = [shared[shared.Length / 2]], Boundary = shared });
            made++;
        }
    }
}
