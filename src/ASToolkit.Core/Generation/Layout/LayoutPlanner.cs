using System.Runtime.InteropServices;
using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Provides the planning of a level layout around its main route.
/// </summary>
public static class LayoutPlanner
{
    private const float LabChainPerSpine = 0.65f;
    private const float MilitaryChainPerSpine = 0.9f;
    private const float MaxStationShare = 0.58f;
    private const float WalkPerUnitStep = 0.8f;
    private const float BranchShare = 0.6f;
    private const float ForkChance = 0.35f;
    private const float MaxAspectStretch = 0.6f;
    private const int MaxPocketUnits = 2;
    private const int MaxBranchTries = 160;
    private const float BlackoutReach = 1700;
    private const float MinDoorGap = 160;
    private const float ChainShareOfBreakerWalk = 0.8f;
    private const float MinBreakerWalkShare = 0.5f;
    private const float LockedShareBand = 0.05f;
    private const float BreakerChainShare = 0.55f;
    private const float PixelsPerOpenStation = 700;
    private const float WingHostShare = 0.45f;
    private const int OpenHostTries = 4000;
    private const int RoomsPerLoop = 8;
    private const int MaxLoops = 6;
    private static readonly (int U, int V)[] Headings = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private static readonly Size[] ClosetSizes = [new(2, 3), new(3, 2), new(3, 3), new(2, 2)];

    /// <summary>
    /// Plans a layout of about the target area inside a box that it fills to about the given share, with a main route
    /// as long as the given elongation times the square root of the area and no room walked farther than the maximum
    /// elongation allows, or returns null when the attempt boxed itself in.
    /// </summary>
    public static LayoutPlan? Plan(Random rng, ThemeSettings theme, LayoutStyleProfile profile, int targetUnits, float fill, float elongation, float maxElongation, GateRequest? gate, int secretClosets, int spawnClosets, int shortcuts)
    {
        var plan = new LayoutPlan(theme, profile) { RoomCap = Math.Max(6, (int)MathF.Sqrt(targetUnits * MaxStationShare / 5)) };
        float spine = elongation * MathF.Sqrt(targetUnits);
        float chainUnits = spine * (theme.Theme == LevelTheme.Lab ? LabChainPerSpine : MilitaryChainPerSpine);
        if (gate is { Kind: GateKind.LockedDoors, IsWing: false })
        {
            chainUnits = MathF.Max(chainUnits, gate.WalkPixels / (BreakerChainShare * theme.UnitPixels));
        }

        GrowStations(rng, plan, (int)(targetUnits * MaxStationShare), chainUnits, targetUnits / fill);
        if (plan.Stations.Count < 5)
        {
            return null;
        }

        if (gate is not null && gate.IsWing && !GrowWing(rng, plan, (int)(targetUnits * gate.Share)))
        {
            return null;
        }

        // Walks cut corners and cover less than a unit per step. Leave room for a blackout's search walk past the trigger.
        float maxWalk = maxElongation * MathF.Sqrt(targetUnits) / WalkPerUnitStep;
        if (gate is { Kind: GateKind.Blackout, IsWing: false })
        {
            maxWalk = Math.Max(maxWalk, (BlackoutReach + gate.SearchPixels) / (theme.UnitPixels * WalkPerUnitStep));
        }

        int firstHost = gate is { Kind: GateKind.LockedDoors, IsWing: false, WalkPixels: > 0 } ? FindGateStation(plan, gate.WalkPixels) + 1 : 0;
        var walk = MeasureWalk(plan, [GetStartUnit(plan)], openOnly: false);
        var branches = GrowBranches(rng, plan, (int)((targetUnits - plan.Area) * BranchShare), walk, maxWalk, firstHost);
        AddSideRooms(rng, plan, targetUnits, branches, walk, maxWalk, firstHost);
        if (gate is not null && !(gate.IsWing ? PlaceWingGate(rng, plan, gate) : PlaceGate(rng, plan, gate)))
        {
            return null;
        }

        AddClosets(rng, plan, secretClosets, RegionKind.SecretCloset, LinkKind.SecretWall, plan.Regions.Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom).ToArray());
        AddClosets(rng, plan, spawnClosets, RegionKind.SpawnCloset, LinkKind.SpawnWall, plan.Stations.Skip(1).Take(Math.Max(1, plan.Stations.Count / 2)).ToArray());
        AddShortcuts(rng, plan, shortcuts);
        AddLoops(rng, plan);
        FillPockets(plan);
        AlignLinks(plan);
        SpaceDoors(plan);
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
    public static float MeasureChain(LayoutPlan plan, PlanLink from, PlanLink to)
    {
        int a = plan.Stations.FindIndex(s => s.Id == from.To);
        int b = plan.Stations.FindIndex(s => s.Id == to.From);
        if (a < 0 || b < a)
        {
            return 0;
        }

        float units = 0;
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

    private static (float U, float V) GetMidpoint(IReadOnlyList<EdgeKey> edges)
    {
        float u = 0;
        float v = 0;
        foreach (var edge in edges)
        {
            (float eu, float ev) = edge.Axis == 'u' ? (edge.A, edge.B + 0.5f) : (edge.A + 0.5f, (float)edge.B);
            u += eu;
            v += ev;
        }

        return (u / edges.Count, v / edges.Count);
    }

    private static int Next(Random rng, (int Min, int Max) range) => rng.Next(range.Min, range.Max + 1);

    private static void GrowStations(Random rng, LayoutPlan plan, int stationUnits, float chainUnits, float boxUnits)
    {
        var t = plan.Theme;
        var first = new UnitRect(0, 0, Next(rng, (t.RoomSize.Min, (t.RoomSize.Min + t.RoomSize.Max) / 2)), Next(rng, (t.RoomSize.Min, (t.RoomSize.Min + t.RoomSize.Max) / 2)));
        float aspect = 1 + MaxAspectStretch * rng.NextSingle();
        int boxW = Math.Max(first.W + 2, (int)MathF.Round(MathF.Sqrt(boxUnits * aspect)));
        int boxH = Math.Max(first.H + 2, (int)MathF.Round(boxUnits / boxW));
        if (rng.Next(2) == 0)
        {
            (boxW, boxH) = (boxH, boxW);
        }

        plan.Bounds = new UnitRect(-rng.Next(boxW - first.W + 1), -rng.Next(boxH - first.H + 1), boxW, boxH);
        plan.Stations.Add(plan.AddRegion(RegionKind.Station, first, 0, null));
        int heading = rng.Next(4);
        int area = first.Area;
        float chain = 0;
        var entry = (U: first.U + first.W / 2.0f, V: first.V + first.H / 2.0f);
        for (int tries = 0; area < stationUnits && chain < chainUnits && tries < 400; tries++)
        {
            var previous = plan.Stations[^1];
            bool isHall = !previous.IsHall || rng.NextSingle() < 0.3f;
            int size = PickRoomSize(rng, plan);
            int length = isHall ? Next(rng, t.HallLength) : size;
            int width = isHall ? Next(rng, t.HallWidth) : size;
            int turn = rng.NextSingle() < 0.35f ? rng.Next(2) == 0 ? 1 : 3 : 0;
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
            var kind = PickLink(rng, plan, LinkContext.Chain);
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

            if (p.GetSharedEdges(r).Length >= ms && plan.IsFree(r) && !plan.TouchesOtherAtCorner(r, previous.Id))
            {
                return r;
            }
        }

        return null;
    }

    // Returns the station, one per PixelsPerOpenStation of the walk to the breaker or later, whose exit lies closest to ChainShareOfBreakerWalk of that walk
    private static int FindGateStation(LayoutPlan plan, float walkPixels)
    {
        var first = GetChainLink(plan, 0);
        int last = Math.Max(1, plan.Stations.Count - 3);
        int earliest = Math.Clamp((int)(walkPixels / PixelsPerOpenStation), 1, last);
        return Enumerable.Range(earliest, last - earliest + 1)
            .OrderBy(k => Math.Abs(MeasureChain(plan, first, GetChainLink(plan, k)) - walkPixels * ChainShareOfBreakerWalk))
            .First();
    }

    private static PlanRegion[] GrowBranches(Random rng, LayoutPlan plan, int branchUnits, Dictionary<Cell, int> walk, float maxWalk, int firstHost)
    {
        var t = plan.Theme;
        var all = new List<PlanRegion>();
        int count = Math.Clamp(branchUnits / (4 * t.RoomSize.Max * t.RoomSize.Max), 1, 3);
        int firstBranchHost = Math.Max(1, firstHost);
        int[] hosts = Enumerable.Range(firstBranchHost, Math.Max(1, plan.Stations.Count - 1 - firstBranchHost)).OrderBy(_ => rng.Next()).ToArray();
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
                var previous = branch.Count == 0 ? plan.Stations[host] : rng.NextSingle() < ForkChance ? branch[rng.Next(branch.Count)] : branch[^1];
                bool isHall = branch.Count > 0 && !previous.IsHall && rng.NextSingle() < 0.4f;
                int size = PickRoomSize(rng, plan);
                int length = isHall ? Next(rng, t.HallLength) : size;
                int width = isHall ? Next(rng, t.HallWidth) : size;
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
                var kind = PickLink(rng, plan, LinkContext.Branch, branch.Count);
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

    private static void AddSideRooms(Random rng, LayoutPlan plan, int targetUnits, IReadOnlyList<PlanRegion> branches, Dictionary<Cell, int> walk, float maxWalk, int firstHost)
    {
        for (int tries = 0; plan.Area < targetUnits && tries < 6000; tries++)
        {
            var hosts = plan.Stations
                .Concat(plan.Stations)
                .Concat(plan.Stations)
                .Concat(branches)
                .Concat(plan.Regions.Where(r => r.Kind == RegionKind.SideRoom && plan.Regions[r.Host!.Value].Kind == RegionKind.Station))
                .Where(r => r.Station >= firstHost || tries >= OpenHostTries)
                .ToArray();


            var host = hosts[rng.Next(hosts.Length)];
            int sideSize = PickRoomSize(rng, plan);
            var room = TryAttachRoom(rng, plan, host, sideSize, sideSize, RegionKind.SideRoom);
            if (room is null || !TryExtendWalk(plan, walk, room, maxWalk))
            {
                continue;
            }

            var kind = PickLink(rng, plan, LinkContext.Side);
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
        if (!plan.IsFree(r) || shared.Length < 3 || plan.TouchesOtherAtCorner(r, host.Id))
        {
            return null;
        }

        var room = plan.AddRegion(kind, r, host.Station, host.Id);
        AddLink(rng, plan, host, room, shared, LinkKind.Arch);
        return room;
    }

    private static void AddLink(Random rng, LayoutPlan plan, PlanRegion from, PlanRegion to, EdgeKey[] boundary, LinkKind kind)
    {
        var link = new PlanLink { From = from.Id, To = to.Id, Kind = kind, Edges = [], Boundary = boundary };
        plan.Links.Add(link);
        SetLinkKind(rng, plan, link, kind);
    }

    private static void SetLinkKind(Random rng, LayoutPlan plan, PlanLink link, LinkKind kind, bool allowDoor = true)
    {
        var all = link.Boundary;
        var inner = GetInnerBoundary(all);
        if (kind == LinkKind.Arch && inner.Length < 2)
        {
            kind = plan.Theme.Theme == LevelTheme.Lab && allowDoor ? LinkKind.Door : LinkKind.Open;
        }

        int width = kind switch
        {
            LinkKind.Arch => Math.Min(Next(rng, plan.Theme.ArchWidth), inner.Length),
            LinkKind.Door => inner.Length >= 2 && rng.NextSingle() < 0.45f ? 2 : 1,
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

    // Move each lab door or arch that stands closer than MinDoorGap to another one to the farthest clear span of its boundary
    private static void SpaceDoors(LayoutPlan plan)
    {
        if (plan.Theme.Theme != LevelTheme.Lab)
        {
            return;
        }

        var doorLinks = plan.Links.Where(l => l is { Kind: LinkKind.Door or LinkKind.Arch, Edges.Count: > 0 }).ToList();
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
                .Where(i => IsClear(plan, inner.AsSpan(i, width)))
                .Select(i => (Index: i, Gap: MeasureDoorGap(doorLinks, link, inner.AsSpan(i, width).ToArray())))
                .OrderByDescending(s => s.Gap)
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
        foreach (var other in doorLinks.Where(o => o != link))
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

    private static PointF ConvertToLabPixels(float a, float b) => new(40f * (a + b), 28f * (b - a));

    private static EdgeKey[] GetInnerBoundary(EdgeKey[] boundary) => boundary.Length > 2 ? boundary[1..^1] : boundary;

    // Move each doorway to the nearest span of its boundary where no other wall meets it, keeping a wall piece at both ends when it can
    private static void AlignLinks(LayoutPlan plan)
    {
        foreach (var link in plan.Links.Where(l => l.Kind != LinkKind.Open && l.Edges.Count > 0))
        {
            var inner = GetInnerBoundary(link.Boundary);
            if (inner.Contains(link.Edges[0]) && inner.Contains(link.Edges[^1]) && IsClear(plan, CollectionsMarshal.AsSpan(link.Edges)))
            {
                continue;
            }

            int start = Array.IndexOf(link.Boundary, link.Edges[0]);
            foreach (var span in new[] { inner, link.Boundary })
            {
                if (TryAlignLink(plan, link, span, start))
                {
                    break;
                }
            }
        }
    }

    private static bool TryAlignLink(LayoutPlan plan, PlanLink link, EdgeKey[] boundary, int start)
    {
        int offset = Array.IndexOf(link.Boundary, boundary[0]);
        for (int width = link.Edges.Count; width >= 1; width--)
        {
            int[] spans = Enumerable.Range(0, boundary.Length - width + 1)
                .Where(i => IsClear(plan, boundary.AsSpan(i, width)))
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

    private enum LinkContext { Chain, Branch, Side }

    private static int PickRoomSize(Random rng, LayoutPlan plan)
    {
        var profile = plan.Profile;
        var range = plan.Theme.RoomSize;
        int size = Next(rng, ((int)MathF.Round(range.Min * profile.SizeScale), (int)MathF.Round(range.Max * profile.SizeScale)));
        return Math.Min(plan.RoomCap, rng.NextSingle() < profile.BigRoomChance ? Next(rng, profile.BigRoom) : size);
    }

    private static LinkKind PickLink(Random rng, LayoutPlan plan, LinkContext context, int branchIndex = 0)
    {
        bool lab = plan.Theme.Theme == LevelTheme.Lab;
        if (plan.Profile.PrefersWideLinks)
        {
            return lab
                ? Pick(rng, (LinkKind.Arch, 0.40f), (LinkKind.Open, 0.52f), (LinkKind.Door, 0.08f))
                : Pick(rng, (LinkKind.Arch, 0.45f), (LinkKind.Open, 0.55f));
        }

        return context switch
        {
            LinkContext.Chain => lab
                ? Pick(rng, (LinkKind.Door, 0.45f), (LinkKind.Arch, 0.45f))
                : Pick(rng, (LinkKind.Arch, 0.8f), (LinkKind.Open, 0.2f)),
            LinkContext.Branch => lab
                ? Pick(rng, (LinkKind.Door, branchIndex == 0 ? 0.6f : 0.4f), (LinkKind.Arch, 0.5f))
                : Pick(rng, (LinkKind.Arch, 0.8f), (LinkKind.Open, 0.2f)),
            _ => lab
                ? Pick(rng, (LinkKind.Door, 0.62f), (LinkKind.Arch, 0.3f))
                : Pick(rng, (LinkKind.Arch, 0.8f), (LinkKind.Open, 0.2f))
        };
    }

    private static LinkKind Pick(Random rng, params (LinkKind Kind, float Weight)[] choices)
    {
        float roll = rng.NextSingle() * choices.Sum(c => c.Weight);
        foreach ((var kind, float weight) in choices)
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
            float startOffset = first.Edges.Count > 0 ? plan.Theme.UnitPixels * (plan.Stations[0].Rects[0].W + plan.Stations[0].Rects[0].H) / 4 : 0;
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
                cut = cuts.Where(k => Math.Abs(BehindShare(k) - gate.Share) <= 0.08f).DefaultIfEmpty(cut).Max();
            }
            else if (gate.WalkPixels > 0)
            {
                // Among the cuts within LockedShareBand of the share, take the one closest to the walk to the breaker
                int[] inBand = cuts.Where(k => Math.Abs(BehindShare(k) - gate.Share) <= LockedShareBand).ToArray();
                var first = GetChainLink(plan, 0);
                if (inBand.Length > 0)
                {
                    cut = inBand.OrderBy(k => Math.Abs(MeasureChain(plan, first, GetChainLink(plan, k)) - gate.WalkPixels)).First();
                }

                if (MeasureChain(plan, first, GetChainLink(plan, cut)) < gate.WalkPixels * MinBreakerWalkShare)
                {
                    return false;
                }
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
            float Farthest(PlanRegion r) => r.Units.Where(walk.ContainsKey).Select(c => walk[c]).DefaultIfEmpty(0).Max() * plan.Theme.UnitPixels * WalkPerUnitStep;
            hosts = plan.Regions.Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom && !r.IsGated && r.Station > blackout)
                .OrderBy(r => Math.Abs(Farthest(r) - gate.SearchPixels)).Take(6).ToArray();
        }

        for (int hostIndex = 0; hostIndex < hosts.Length; hostIndex++)
        {
            var host = hosts[hostIndex];
            // Try the gate station three times, the last time with smaller rooms
            int rounds = hostIndex == 0 ? 3 : 1;
            for (int round = 0; round < rounds; round++)
            {
                (int Min, int Max) size = hostIndex == 0 && round == 2 ? (4, 5) : (4, 6);
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var room = TryAttachRoom(rng, plan, host, Next(rng, size), Next(rng, size), RegionKind.SideRoom);
                    if (room is null)
                    {
                        continue;
                    }

                    if (gate.WalkPixels > 0 && MeasureWalkTo(plan, room) < gate.WalkPixels * MinBreakerWalkShare)
                    {
                        RemoveLastRegion(plan);
                        continue;
                    }

                    plan.KeyRoom = room;
                    return true;
                }
            }
        }

        return false;

        float BehindShare(int k) => plan.Regions.Where(r => r.Station > k).Sum(r => r.Units.Count) / (float)plan.Area;
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
    private static bool TryExtendWalk(LayoutPlan plan, Dictionary<Cell, int> walk, PlanRegion region, float maxWalk)
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

        RemoveLastRegion(plan);
        return false;
    }

    private static void RemoveLastRegion(LayoutPlan plan)
    {
        foreach (var unit in plan.Regions[^1].Units)
        {
            plan.Occupancy.Remove(unit);
        }

        plan.Links.RemoveAt(plan.Links.Count - 1);
        plan.Regions.RemoveAt(plan.Regions.Count - 1);
    }

    // Returns the walk in pixels from the start to the nearest unit of a region
    private static float MeasureWalkTo(LayoutPlan plan, PlanRegion region)
    {
        var walk = MeasureWalk(plan, [GetStartUnit(plan)], openOnly: false);
        return region.Units.Where(walk.ContainsKey).Select(unit => walk[unit]).DefaultIfEmpty(0).Min() * plan.Theme.UnitPixels * WalkPerUnitStep;
    }

    private static bool GrowWing(Random rng, LayoutPlan plan, int wingUnits)
    {
        var t = plan.Theme;
        var start = GetChainLink(plan, 0);
        float chain = MeasureChain(plan, start, GetChainLink(plan, plan.Stations.Count - 2));
        
        int[] hosts = Enumerable.Range(1, plan.Stations.Count - 3)
            .OrderBy(k => Math.Abs(MeasureChain(plan, start, GetChainLink(plan, k)) - WingHostShare * chain))
            .Take(4)
            .ToArray();
        
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

            if (area >= wingUnits * 0.6f)
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
            var firstLink = GetChainLink(plan, 0);
            int trigger = Enumerable.Range(0, Math.Max(1, host.Station))
                .Where(k => MeasureChain(plan, firstLink, GetChainLink(plan, k)) <= BlackoutReach)
                .DefaultIfEmpty(0)
                .Max();

            plan.BlackoutLink = GetChainLink(plan, trigger);
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
        var gateLink = plan.GateLink!;
        int mid = Math.Max(1, cut / 2);
        
        int[] candidates = Enumerable.Range(1, cut)
            .Where(k => MeasureChain(plan, GetChainLink(plan, k - 1), gateLink) >= 900)
            .OrderBy(k => Math.Abs(k - mid))
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

        int blackout = plan.BlackoutLink is null ? int.MaxValue : plan.Regions[plan.BlackoutLink.From].Station;
        var candidates = FindBoundaries(plan)
            .Where(p => p.Value.Count >= 3 && IsOpenRoom(plan.Regions[p.Key.First]) && IsOpenRoom(plan.Regions[p.Key.Second]) && IsSameSide(p.Key.First, p.Key.Second)
                        && CountHops(plan, p.Key.First, p.Key.Second) >= 3)
            .ToArray();

        rng.Shuffle(candidates);

        foreach (((int a, int b), var edges) in candidates.Take(count))
        {
            var sorted = SortEdges(edges);
            plan.Links.Add(new PlanLink { From = a, To = b, Kind = LinkKind.SecretWall, Edges = [sorted[sorted.Length / 2]], Boundary = sorted });
        }

        return;

        bool IsSameSide(int a, int b) => plan.Regions[a].Station > blackout == plan.Regions[b].Station > blackout;

        bool IsOpenRoom(PlanRegion r) => r.Kind is RegionKind.Station or RegionKind.SideRoom && !r.IsGated && r != plan.KeyRoom;
    }

    // Link touching lab rooms on the same side of the gate that no link joins yet, one loop per RoomsPerLoop rooms, keeping the walk to the key room
    private static void AddLoops(Random rng, LayoutPlan plan)
    {
        if (plan.Theme.Theme != LevelTheme.Lab)
        {
            return;
        }

        int rooms = plan.Regions.Count(r => r.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing);
        int count = Math.Clamp(rooms / RoomsPerLoop, 1, MaxLoops);
        int blackout = plan.BlackoutLink is null ? int.MaxValue : plan.Regions[plan.BlackoutLink.From].Station;
        var last = plan.Stations[^1];
        var candidates = FindBoundaries(plan)
            .Select(p => (Pair: p.Key, Edges: GetLongestLine(p.Value)))
            .Where(p => p.Edges.Length >= plan.Theme.MinShared && CanLoop(plan.Regions[p.Pair.First]) && CanLoop(plan.Regions[p.Pair.Second])
                        && plan.Regions[p.Pair.First].IsGated == plan.Regions[p.Pair.Second].IsGated
                        && plan.Regions[p.Pair.First].Station > blackout == plan.Regions[p.Pair.Second].Station > blackout
                        && CountHops(plan, p.Pair.First, p.Pair.Second) >= 2)
            .ToArray();

        rng.Shuffle(candidates);
        float keyWalk = plan.KeyRoom is null ? 0 : MeasureWalkTo(plan, plan.KeyRoom);
        foreach (((int a, int b), var edges) in candidates)
        {
            if (count == 0)
            {
                break;
            }

            var kind = plan.Regions[a].IsGated ? PickLink(rng, plan, LinkContext.Side) : LinkKind.Arch;
            AddLink(rng, plan, plan.Regions[a], plan.Regions[b], edges, kind);
            if (plan.KeyRoom is not null && MeasureWalkTo(plan, plan.KeyRoom) < keyWalk)
            {
                plan.Links.RemoveAt(plan.Links.Count - 1);
                continue;
            }

            count--;
        }

        return;

        bool CanLoop(PlanRegion r) => r.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing && r != plan.KeyRoom && r != last;
    }

    private static Dictionary<(int First, int Second), List<EdgeKey>> FindBoundaries(LayoutPlan plan)
    {
        var boundaries = new Dictionary<(int First, int Second), List<EdgeKey>>();
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

        return boundaries;
    }

    private static EdgeKey[] GetLongestLine(List<EdgeKey> edges) => edges
        .GroupBy(k => (k.Axis, Line: k.Axis == 'v' ? k.B : k.A))
        .Select(line => SortEdges(line))
        .MaxBy(line => line.Length)!;

    private static EdgeKey[] SortEdges(IEnumerable<EdgeKey> edges) => edges.OrderBy(k => k.Axis).ThenBy(k => k.A).ThenBy(k => k.B).ToArray();

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
            if (r is null || !plan.IsFree(r.Value) || plan.TouchesOtherAtCorner(r.Value, host.Id))
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
