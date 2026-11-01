using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Provides layouts of a hub hall with the branch entrances in rows on its sides, a neck, and a second hall with rows of rooms and a
/// block of big rooms in the original's order.
/// </summary>
public sealed class LayoutBuilder
{
    private const int MinSide = 3;
    private const int PlaceTries = 40;
    private const int RowTries = 12;
    private const int MinRoomUnits = 12;
    private const float ChainFanOut = 0.3f;
    private const float BodyDoorChance = 0.9f;
    private const float BranchCycleChance = 0.3f;
    private const int NotchTries = 6;
    private const float FillSlack = 0.05f;
    private const int PillarHallWidth = 8;
    private const int PillarSpacing = 11;
    private const int PillarLength = 2;
    private const int PillarMargin = 2;
    private const float NotchToNeighborChance = 0.4f;
    private const float LockedShareSlack = 0.05f;
    private const float LockedShareMiss = 0.1f;
    private const float MaxBreakerToGatePixels = 600;
    private const int KeyBranch = -2;
    private const float SearchReach = 1000;
    private const float WingShareSlack = 0.1f;
    private const float MinWingShare = 0.18f;
    private const float CompoundDoorChance = 0.63f;
    private const int MaxChainRooms = 3;
    private const float MaxRoomStretch = 2.5f;
    private const int RingRooms = 5;
    private const float SmallBlockShare = 0.4f;
    private const float BodyDepthSlack = 0.15f;
    private const int EntryJitter = 10;
    private const float CenterPull = 0.5f;
    private const int MinCorridorTails = 3;
    private const float CorridorChance = 0.5f;
    private const int CorridorWidth = 4;
    private const float Unreachable = 1e8f;
    private const float MinFenceGap = 900;
    private const float CenteredDoorShare = 0.95f;
    private const float MinDoorGap = 160;
    private const int MaxPocketUnits = 2;
    private const int FarClosetTries = 60;
    private const float MinGeneratorWalk = 1200;
    private const float FencedShareMiss = 0.06f;
    private const float GeneratorWalkPerUnit = 20;

    private static readonly (int Min, int Max) RowGap = (0, 1);
    private static readonly (int Min, int Max) PodGap = (1, 5);
    private static readonly (float Min, float Max) NotchSide = (0.25f, 0.5f);
    private static readonly (int Min, int Max) CorridorLength = (8, 16);
    private static readonly (float Min, float Max) RingBand = (0.85f, 1.1f);
    private static readonly (float Min, float Max) ShapeRatio = (0.6f, 1.6f);
    private static readonly (float Min, float Max) KeyProgress = (0.12f, 0.22f);
    private static readonly (int U, int V)[] Headings = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private static readonly (int U, int V)[] FarHeadings = [(1, 0), (0, -1)];
    private static readonly (int W, int H)[] ClosetSizes = [(2, 3), (3, 2), (3, 3), (2, 2)];

    private readonly Random _rng;
    private readonly LevelProfile _profile;
    private readonly LayoutRequest _request;
    private readonly List<Piece> _pieces = [];
    private readonly Dictionary<Cell, Piece> _owner = [];
    private readonly HashSet<(Piece A, Piece B)> _joins = [];
    private readonly LayoutPlan _plan;
    private readonly Dictionary<Piece, PlanRegion> _regions = [];
    private Piece? _start;
    private Piece? _hub;
    private Piece? _neck;
    private int _wing = -1;
    private Piece? _key;

    private LayoutBuilder(Random rng, LevelProfile profile, LayoutRequest request)
    {
        _plan = new LayoutPlan(profile.Settings);
        _rng = rng;
        _profile = profile;
        _request = request;
    }

    private enum Role
    {
        Start,
        Lead,
        Hall,
        Row,
        Neck,
        Block,
        Branch
    }

    private enum Side
    {
        Right,
        Left,
        Bottom,
        Top
    }

    private int MinShared => _profile.Settings.MinShared + 1;

    private float WalkPixelsPerUnit => 0.8f * _profile.Settings.UnitPixels;

    private int HubStation => _plan.Stations.IndexOf(_regions[_hub!]);

    /// <summary>
    /// Builds a lab layout, or returns null when the draw cannot hold the profile.
    /// </summary>
    public static LayoutPlan? Build(Random rng, LevelProfile profile, LayoutRequest request)
    {
        var builder = new LayoutBuilder(rng, profile.Inflate(), request);
        return builder.TryBuild() ? builder._plan : null;
    }

    private bool TryBuild()
    {
        var end = LayLevel();
        if (end is null || _start is null)
        {
            return false;
        }

        ShapeRooms();
        if (!Commit(end))
        {
            return false;
        }

        bool gated = _request.Gate switch
        {
            null => true,
            { IsWing: true } gate => CloseWing(gate),
            { Kind: GateKind.LockedDoors } gate => LockSections(gate),
            { Kind: GateKind.Blackout } gate => PlaceBlackout(gate),
            { Kind: GateKind.Fences } gate => PlaceFences(gate),
            _ => false
        };
        if (!gated)
        {
            return false;
        }

        AddPens(_request.Pens);
        CollectBranches();
        var hosts = _plan.Regions.Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom).ToArray();
        foreach (int area in _profile.SecretRooms)
        {
            AddClosets(1, RegionKind.SecretRoom, LinkKind.SecretWall, hosts, () => DrawSize(area));
        }

        AddClosets(Math.Max(0, _request.SecretClosets - _profile.SecretRooms.Count), RegionKind.SecretCloset, LinkKind.SecretWall, hosts,
            () => ClosetSizes[_rng.Next(ClosetSizes.Length)]);
        AddClosets(_request.SpawnClosets, RegionKind.SpawnCloset, LinkKind.SpawnWall, _plan.Stations.Skip(2).ToArray(), () => ClosetSizes[_rng.Next(ClosetSizes.Length)]);
        if (_plan.Gate != GateKind.Blackout)
        {
            AddShortcuts(_request.Shortcuts);
        }

        FillPockets();
        RaisePillars();
        AlignLinks();
        SpaceDoors();
        return true;
    }

    // Lay the start and the hub with the branch roots and the hub's rooms in rows on its sides, the neck at its far end, the second hall
    // and the block in the original's order, then the dead ends off rooms past the neck
    private Piece? LayLevel()
    {
        var rows = _profile.Branches
            .Select((branch, index) => branch.Take(_profile.BranchEntrances[index]).Select(area => (Area: area, Branch: index)).ToArray())
            .Concat(_profile.HubRooms.Select(area => new[] { (Area: area, Branch: -1) }))
            .OrderBy(_ => _rng.Next())
            .ToList();
        
        var deadEnds = _profile.DeadEnds.ToList();
        var corridors = Enumerable.Range(0, _profile.Branches.Count)
            .Where(index => _profile.Branches[index].Count - _profile.BranchEntrances[index] >= MinCorridorTails && _rng.NextSingle() < CorridorChance)
            .ToHashSet();
        
        var keySide = _rng.Next(2) == 0 ? Side.Top : Side.Bottom;
        var wing = _request.Gate;
        if (wing is { IsWing: true } && _profile.Branches.Count > 0)
        {
            _wing = Enumerable.Range(0, _profile.Branches.Count).MinBy(index => Math.Abs(_profile.Branches[index].Sum() / (float)_profile.Units - wing.Share));
            rows.Insert(Math.Min(rows.Count, rows.FindIndex(row => row[0].Branch == _wing) + 2), [(_profile.KeyUnits, KeyBranch)]);
        }

        if (_profile.Halls.Count > 0)
        {
            (int length, int width) = (_profile.Halls[0].Length, Math.Max(MinShared, _profile.Halls[0].Width));
            int span = Enumerable.Range(0, 2).Max(parity => rows.Where((_, index) => index % 2 == parity).SelectMany(group => group).SkipLast(1).Sum(row => (int)MathF.Ceiling(MathF.Sqrt(row.Area))) + MinShared);
            _hub = AddPiece(Role.Hall, new UnitRect(0, 0, Math.Max(length, span + (int)MathF.Ceiling(MathF.Sqrt(_profile.KeyUnits))), width), 0);
            var lead = _hub;
            foreach (int area in _profile.LeadRooms.Reverse())
            {
                lead = Attach(area, lead, Side.Left, Role.Lead, 0);
                if (lead is null)
                {
                    return null;
                }
            }

            _start = Attach(_profile.StartUnits, lead, Side.Left, Role.Start, 0);
        }
        else
        {
            (int w, int h) = DrawSize(_profile.StartUnits);
            _start = AddPiece(Role.Start, new UnitRect(0, 0, w, h), 0);
            _hub = _start;
        }

        if (_start is null)
        {
            return null;
        }

        if (_request.Gate is { IsWing: false, Kind: GateKind.Fences } && _profile.IsKeyApart)
        {
            _key = LayKey(keySide) ?? LayKey(keySide == Side.Top ? Side.Bottom : Side.Top);
        }

        if (_request.Gate is { IsWing: false, Kind: GateKind.LockedDoors })
        {
            _key = LayKey(keySide);
            if (_key is null)
            {
                return null;
            }
        }

        foreach ((var side, int parity) in new[] { (Side.Top, 0), (Side.Bottom, 1) })
        {
            var items = rows
                .Where((_, index) => index % 2 == parity)
                .SelectMany(group => group)
                .ToArray();
            
            int? stop = _key is not null && side == keySide ? _key.Bounds.U : null;
            bool[] narrow = items.Select((item, index) => corridors.Contains(item.Branch) && items.Take(index).All(other => other.Branch != item.Branch)).ToArray();
            var laid = LayRow(_hub, side, items.Select(item => item.Area).ToArray(), RowGap, Role.Row, 0, items.Select(item => item.Branch).ToArray(), stop, narrow);
            
            foreach (var entrances in laid.OfType<Piece>().Where(piece => piece.Branch >= 0).GroupBy(piece => piece.Branch))
            {
                var root = entrances.FirstOrDefault(piece => piece.IsCorridor) ?? entrances.Last();
                LayBranch(root, side, _profile.Branches[entrances.Key].Skip(_profile.BranchEntrances[entrances.Key]).ToArray(), entrances.Key);
            }
        }

        _neck = Attach(_profile.NeckUnits, _hub, Side.Right, Role.Neck, 1);
        if (_neck is null)
        {
            return null;
        }

        var body = _profile.IsBlockFirst ? LayBodyBlock(_neck) : LayBodyBlock(LaySecondHall(_neck, false));
        if (body is null)
        {
            return null;
        }

        var end = _profile.IsBlockFirst ? LaySecondHall(body, true) : body;
        OpenBodyContacts();
        if (_request.Gate is { IsWing: false, Kind: GateKind.Blackout })
        {
            var walk = MeasureJoinWalk(_neck);
            foreach (var host in _pieces.Where(piece => piece.Role == Role.Block && piece != end).OrderByDescending(walk.GetValueOrDefault))
            {
                _key = Enum.GetValues<Side>()
                    .OrderBy(_ => _rng.Next())
                    .Select(side => Attach(_profile.KeyUnits, host, side, Role.Branch, 1, alone: true))
                    .FirstOrDefault(key => key is not null);
                
                if (_key is not null)
                {
                    break;
                }
            }
        }

        var hosts = _pieces.Where(piece => piece.Section == 1 && piece.Role != Role.Branch && piece != end).ToArray();
        // Lay each dead end's rooms in line, out from the side of its host it leaves
        foreach (var chain in deadEnds)
        {
            Piece? placed = null;
            var outward = Side.Right;
            var tries = Enumerable.Range(0, MinSide)
                .Where(_ => hosts.Length > 0)
                .Select(_ => (Host: hosts[_rng.Next(hosts.Length)], Side: Enum.GetValues<Side>()[_rng.Next(4)]))
                .Concat(hosts.OrderByDescending(host => host.Units.Count).SelectMany(host => Enum.GetValues<Side>().OrderBy(_ => _rng.Next()).Select(side => (Host: host, Side: side))));
            
            foreach (var (host, side) in tries)
            {
                placed = Attach(chain[0], host, side, Role.Branch, 1, alone: true);
                if (placed is not null)
                {
                    outward = side;
                    break;
                }
            }

            foreach (int area in chain.Skip(1).TakeWhile(_ => placed is not null))
            {
                placed = Attach(area, placed!, outward, Role.Branch, 1, alone: true);
            }
        }

        return end;
    }

    // Lay the second hall off a room, along the hub's axis or turning across it as the original's does, with its rooms in rows on both sides,
    // and return its row room farthest out when it ends the main body or else a room of its far row for the block
    private Piece LaySecondHall(Piece anchor, bool endsBody)
    {
        if (_profile.Halls.Count < 2)
        {
            return anchor;
        }

        (int length, int width) = (_profile.Halls[1].Length, Math.Max(MinShared, _profile.Halls[1].Width));
        var at = anchor.Bounds;
        int turnU = at.U + _rng.Next(0, Math.Max(1, at.W - width + 1));
        bool along = !_profile.IsSecondHallAcross;
        var rect = along
            ? new UnitRect(at.U + at.W, at.V - _rng.Next(0, Math.Max(1, width - MinShared)), length, width)
            : _rng.Next(2) == 0 ? new UnitRect(turnU, at.V - length, width, length) : new UnitRect(turnU, at.V + at.H, width, length);
        var hall = IsFree(rect) && CountContacts(rect).GetValueOrDefault(anchor) >= MinShared ? AddPiece(Role.Hall, rect, 1) : Attach(length * width, anchor, Side.Right, Role.Hall, 1);
        if (hall is null)
        {
            return anchor;
        }

        Join(anchor, hall);
        int[] rooms = _profile.HallRooms.OrderBy(_ => _rng.Next()).ToArray();
        var gap = _profile.HasPods ? PodGap : RowGap;
        int[] nearRooms = rooms.Where((_, index) => index % 2 == 0).ToArray();
        var near = LayRow(hall, along ? Side.Top : Side.Left, nearRooms, gap, Role.Row, 1);
        int[] farRooms = rooms.Where((_, index) => index % 2 == 1).Concat(nearRooms.Where((_, index) => near[index] is null)).ToArray();
        var far = LayRow(hall, along ? Side.Bottom : Side.Right, farRooms, gap, Role.Row, 1).OfType<Piece>().ToArray();
        if (endsBody)
        {
            var laid = near.OfType<Piece>().Concat(far).DefaultIfEmpty(hall);
            return along ? laid.MaxBy(piece => piece.Bounds.U + piece.Bounds.W)! : laid.MaxBy(piece => Math.Abs(2 * piece.Bounds.V + piece.Bounds.H - 2 * at.V - at.H))!;
        }

        return far.Length > 0 ? far[_rng.Next(far.Length)] : hall;
    }

    // Lay the block's big rooms running away from a room, as a ring around a court when there are enough of them, or else as one rectangle,
    // or else as a chain, hang its small rooms off them, and return its room farthest out
    private Piece? LayBodyBlock(Piece anchor)
    {
        int[] all = _profile.BlockRooms.ToArray();
        if (all.Length == 0)
        {
            return anchor;
        }

        float median = all.Order().ElementAt(all.Length / 2);
        int[] areas = all.Where(area => area >= SmallBlockShare * median).ToArray();
        int[] small = all.Where(area => area < SmallBlockShare * median).ToArray();

        float openChance = _profile.Halls.Count > 0 ? 1 : CompoundDoorChance;
        var depth = (_profile.BodyDepth * (1 - BodyDepthSlack), _profile.BodyDepth * (1 + BodyDepthSlack));
        float middle = anchor.Bounds.V + anchor.Bounds.H / 2f;
        float mass = _pieces.Sum(piece => piece.Units.Count * (piece.Bounds.V + piece.Bounds.H / 2f)) / _pieces.Sum(piece => piece.Units.Count);
        float entry = mass >= middle ? _profile.BodyEntry : 1 - _profile.BodyEntry;
        var block = areas.Length >= RingRooms ? LayBlock(anchor, Side.Right, areas, Role.Block, 1, -1, openChance, depth, outward: true, ring: true, entry: entry) : [];
        if (block.Count == 0)
        {
            block = LayBlock(anchor, Side.Right, areas, Role.Block, 1, -1, openChance, depth, outward: true, entry: entry);
        }

        if (block.Count == 0 && areas.Length > 0)
        {
            return null;
        }

        var end = block.DefaultIfEmpty(anchor).MaxBy(piece => piece.Bounds.U + piece.Bounds.W)!;
        var hosts = block.Where(piece => piece != end).DefaultIfEmpty(end).ToArray();
        foreach (int area in small)
        {
            for (int attempt = 0; attempt < MinSide; attempt++)
            {
                if (Attach(area, hosts[_rng.Next(hosts.Length)], Enum.GetValues<Side>()[_rng.Next(4)], Role.Block, 1) is not null)
                {
                    break;
                }
            }
        }

        return end;
    }

    // Lay the key's room at the hub's far end on one side, beside the neck
    private Piece? LayKey(Side side)
    {
        var hb = _hub!.Bounds;
        for (int attempt = 0; attempt < RowTries; attempt++)
        {
            (int w, int h) = DrawSize(_profile.KeyUnits);
            var rect = new UnitRect(hb.U + hb.W - w, side == Side.Top ? hb.V - h : hb.V + hb.H, w, h);
            if (w <= hb.W / 2 && IsFree(rect))
            {
                var key = AddPiece(Role.Row, rect, 0, KeyBranch);
                Join(_hub, key);
                return key;
            }
        }

        return null;
    }

    // Lay a branch's further rooms in rows along both sides of its corridor with the rest in a block at its tip, or as a block off its last
    // entrance on any side, or as two smaller blocks, or as a short chain
    private void LayBranch(Piece root, Side side, int[] tails, int branch)
    {
        if (root.IsCorridor)
        {
            int[] order = tails.OrderBy(_ => _rng.Next()).ToArray();
            var flanks = side is Side.Top or Side.Bottom ? new[] { Side.Left, Side.Right } : [Side.Top, Side.Bottom];
            var rest = new List<int>();
            for (int flank = 0; flank < 2; flank++)
            {
                int[] areas = order.Where((_, index) => index % 2 == flank).ToArray();
                var laid = LayRow(root, flanks[flank], areas, RowGap, Role.Branch, root.Section, areas.Select(_ => branch).ToArray());
                rest.AddRange(areas.Where((_, index) => laid[index] is null));
            }

            if (rest.Count > 0)
            {
                LayBlock(root, side, rest.ToArray(), Role.Branch, root.Section, branch, 0, (1, 1.6f));
            }

            return;
        }

        if (tails.Length > MaxChainRooms)
        {
            Side[] sides = Enum.GetValues<Side>().Where(other => other != side).OrderBy(_ => _rng.Next()).Prepend(side).ToArray();
            if (sides.Any(option => LayBlock(root, option, tails, Role.Branch, 0, branch, 0, (1, 1.6f)).Count > 0))
            {
                return;
            }

            var first = sides.Select(option => LayBlock(root, option, tails[..(tails.Length / 2)], Role.Branch, 0, branch, 0, (1, 1.6f))).FirstOrDefault(block => block.Count > 0);
            if (first is not null)
            {
                foreach (var host in first.OrderBy(_ => _rng.Next()))
                {
                    if (sides.Any(option => LayBlock(host, option, tails[(tails.Length / 2)..], Role.Branch, 0, branch, 0, (1, 1.6f)).Count > 0))
                    {
                        return;
                    }
                }

                return;
            }
        }

        LayChain(root, side, tails.Take(MaxChainRooms).ToArray(), branch);
    }

    // Pack rooms end to end along a side of a hall and return the room laid for each area, or null where the side ran out
    private Piece?[] LayRow(Piece hall, Side side, int[] areas, (int Min, int Max) gap, Role role, int section, int[]? branches = null, int? stop = null, bool[]? corridors = null)
    {
        var laid = new Piece?[areas.Length];
        var hb = hall.Bounds;
        bool across = side is Side.Top or Side.Bottom;
        int cursor = across ? hb.U : hb.V;
        int end = stop ?? (across ? hb.U + hb.W : hb.V + hb.H);
        for (int index = 0; index < areas.Length && cursor < end - 2; index++)
        {
            for (int attempt = 0; attempt < RowTries; attempt++)
            {
                bool corridor = corridors?[index] == true;
                int length = Math.Clamp((int)MathF.Round(areas[index] / (float)CorridorWidth), CorridorLength.Min, CorridorLength.Max);
                (int w, int h) = !corridor ? DrawSize(areas[index]) : across ? (CorridorWidth, length) : (length, CorridorWidth);
                int room = end - cursor;
                int depth = Math.Max(MinSide, (int)MathF.Ceiling(areas[index] / (float)Math.Max(1, room)));
                if (attempt == RowTries - 1 && !corridor && (across ? w : h) > room && room >= MinShared && Math.Max(room, depth) <= MaxRoomStretch * Math.Min(room, depth))
                {
                    (w, h) = across ? (room, depth) : (depth, room);
                }

                var rect = side switch
                {
                    Side.Top => new UnitRect(cursor, hb.V - h, w, h),
                    Side.Bottom => new UnitRect(cursor, hb.V + hb.H, w, h),
                    Side.Left => new UnitRect(hb.U - w, cursor, w, h),
                    _ => new UnitRect(hb.U + hb.W, cursor, w, h)
                };
                int span = across ? w : h;
                if (cursor + span > end + 2 && attempt < RowTries - 1)
                {
                    continue;
                }

                if (IsFree(rect) && CountContacts(rect).GetValueOrDefault(hall) >= MinShared)
                {
                    laid[index] = AddPiece(role, rect, section, branches?[index] ?? -1);
                    laid[index]!.IsCorridor = corridor;
                    Join(hall, laid[index]!);
                    cursor += span + _rng.Next(gap.Min, gap.Max + 1);
                    break;
                }
            }
        }

        return laid;
    }

    // Divide one rectangle holding all the areas into rooms and stand it against a side of the anchor, its long side away from the anchor or
    // along it, opening a tree of contacts and each other contact by a chance, or one more contact now and then when there is none
    private List<Piece> LayBlock(Piece anchor, Side side, int[] areas, Role role, int section, int branch, float openChance, (float Min, float Max) aspect, bool outward = false,
        bool ring = false, float? entry = null)
    {
        int total = areas.Sum();
        var ab = anchor.Bounds;
        int[] sorted = areas.OrderDescending().ToArray();
        for (int attempt = 0; attempt < PlaceTries; attempt++)
        {
            float stretch = aspect.Min + (aspect.Max - aspect.Min) * _rng.NextSingle();
            int w = Math.Max(MinSide, (int)MathF.Round(MathF.Sqrt(total * stretch)));
            int h = Math.Max(MinSide, (int)MathF.Round(total / (float)w));
            int band = (int)MathF.Round(MathF.Sqrt(total / (float)areas.Length) * (RingBand.Min + (RingBand.Max - RingBand.Min) * _rng.NextSingle()));
            if (ring)
            {
                float perimeter = (total + 4f * band * band) / (2 * band);
                h = (int)MathF.Round(perimeter / (1 + stretch));
                w = (int)MathF.Round(perimeter) - h;
                if (w - 2 * band < MinSide || h - 2 * band < MinSide)
                {
                    continue;
                }
            }

            if (outward ? side is Side.Top or Side.Bottom : _rng.Next(2) == 0)
            {
                (w, h) = (h, w);
            }

            var rect = PlaceBeside(ab, side, w, h, entry);
            if (rect is null || !IsFree(rect.Value))
            {
                continue;
            }

            var rects = new List<UnitRect>();
            if (ring)
            {
                SplitRing(rect.Value, sorted, band, rects);
            }
            else
            {
                Split(rect.Value, sorted, rects);
            }

            if (rects.Count < sorted.Length || rects.Any(r => r.W < MinSide || r.H < MinSide || Math.Max(r.W, r.H) > MaxRoomStretch * Math.Min(r.W, r.H)))
            {
                continue;
            }

            var pieces = rects.Select(r => AddPiece(role, r, section, branch)).ToList();
            var touching = pieces.Where(piece => CountContacts(piece.Bounds).GetValueOrDefault(anchor) >= MinShared).ToArray();
            if (touching.Length == 0)
            {
                Remove(pieces);
                continue;
            }

            var root = touching[_rng.Next(touching.Length)];
            Join(anchor, root);
            var pairs = pieces.SelectMany((a, index) => pieces.Skip(index + 1).Select(b => (A: a, B: b))).Where(pair => Touches(pair.A, pair.B)).ToList();
            var reached = new List<Piece> { root };
            for (int index = 0; index < reached.Count; index++)
            {
                foreach (var next in pieces.Where(piece => !reached.Contains(piece) && Touches(reached[index], piece)).ToArray())
                {
                    Join(reached[index], next);
                    reached.Add(next);
                }
            }

            var extra = pairs.Where(pair => !_joins.Contains(Order(pair.A, pair.B))).ToArray();
            if (openChance > 0)
            {
                extra.Where(_ => _rng.NextSingle() < openChance).ToList().ForEach(pair => Join(pair.A, pair.B));
                return pieces;
            }

            if (extra.Length > 0 && _rng.NextSingle() < BranchCycleChance)
            {
                var pair = extra[_rng.Next(extra.Length)];
                Join(pair.A, pair.B);
            }

            return pieces;
        }

        return [];
    }

    // Divide a band of a depth around the rectangle's edge into rooms and leave its middle as a court, each side of the band taking the next
    // largest area while it has the most room left
    private static void SplitRing(UnitRect rect, int[] areas, int depth, List<UnitRect> rects)
    {
        UnitRect[] bands =
        [
            new(rect.U, rect.V, rect.W, depth),
            new(rect.U, rect.V + rect.H - depth, rect.W, depth),
            new(rect.U, rect.V + depth, depth, rect.H - 2 * depth),
            new(rect.U + rect.W - depth, rect.V + depth, depth, rect.H - 2 * depth)
        ];
        var shares = bands.Select(_ => new List<int>()).ToArray();
        float[] room = bands.Select(band => (float)band.Area).ToArray();
        foreach (int area in areas)
        {
            int band = Array.IndexOf(room, room.Max());
            shares[band].Add(area);
            room[band] -= area;
        }

        if (shares.Any(share => share.Count == 0))
        {
            return;
        }

        for (int band = 0; band < bands.Length; band++)
        {
            Split(bands[band], shares[band].ToArray(), rects);
        }
    }

    private static void Split(UnitRect rect, int[] areas, List<UnitRect> rects)
    {
        if (areas.Length == 1)
        {
            rects.Add(rect);
            return;
        }

        int total = areas.Sum();
        int count = 1;
        for (int acc = areas[0]; count < areas.Length - 1 && acc < total / 2; count++)
        {
            acc += areas[count];
        }

        float share = areas.Take(count).Sum() / (float)total;
        if (rect.W >= rect.H)
        {
            int cut = Math.Clamp((int)MathF.Round(rect.W * share), MinSide, Math.Max(MinSide, rect.W - MinSide));
            Split(rect with { W = cut }, areas[..count], rects);
            Split(rect with { U = rect.U + cut, W = rect.W - cut }, areas[count..], rects);
            return;
        }

        int depth = Math.Clamp((int)MathF.Round(rect.H * share), MinSide, Math.Max(MinSide, rect.H - MinSide));
        Split(rect with { H = depth }, areas[..count], rects);
        Split(rect with { V = rect.V + depth, H = rect.H - depth }, areas[count..], rects);
    }

    // Grow rooms outward from a root, each off the last or now and then off an earlier one, touching nothing but its parent
    private void LayChain(Piece root, Side side, int[] areas, int branch)
    {
        var laid = new List<Piece>();
        var previous = root;
        foreach (int area in areas)
        {
            var parent = laid.Count == 0 || _rng.NextSingle() >= ChainFanOut ? previous : laid[_rng.Next(laid.Count)];
            Piece? room = null;
            foreach (var option in new[] { side, side }.Concat(Enum.GetValues<Side>().OrderBy(_ => _rng.Next())))
            {
                room = Attach(area, parent, option, Role.Branch, root.Section, branch, alone: true);
                if (room is not null)
                {
                    break;
                }
            }

            if (room is null)
            {
                return;
            }

            laid.Add(room);
            previous = room;
        }
    }

    // Place a room of an area against a side of a parent, touching as few other rooms as it can, or none when alone
    private Piece? Attach(int area, Piece parent, Side side, Role role, int section, int branch = -1, bool alone = false)
    {
        UnitRect? best = null;
        float bestScore = float.MinValue;
        float pull = _plan.Theme.Theme == LevelTheme.Lab ? CenterPull : 0;
        int floor = _pieces.Sum(piece => piece.Units.Count);
        float centerU = _pieces.Sum(piece => piece.Units.Count * (piece.Bounds.U + piece.Bounds.W / 2f)) / floor;
        float centerV = _pieces.Sum(piece => piece.Units.Count * (piece.Bounds.V + piece.Bounds.H / 2f)) / floor;
        for (int attempt = 0; attempt < PlaceTries; attempt++)
        {
            (int w, int h) = DrawSize(area);
            var rect = PlaceBeside(parent.Bounds, side, w, h);
            if (rect is null || !IsFree(rect.Value))
            {
                continue;
            }

            var contacts = CountContacts(rect.Value);
            int others = contacts.Keys.Count(piece => piece != parent);
            if (contacts.GetValueOrDefault(parent) < MinShared || (alone && others > 0))
            {
                continue;
            }

            var r = rect.Value;
            float score = _rng.NextSingle() - 2 * others - pull * (MathF.Abs(r.U + r.W / 2f - centerU) + MathF.Abs(r.V + r.H / 2f - centerV)) / MathF.Sqrt(floor);
            if (score > bestScore)
            {
                best = rect;
                bestScore = score;
            }
        }

        if (best is null)
        {
            return null;
        }

        var room = AddPiece(role, best.Value, section, branch);
        Join(parent, room);
        return room;
    }

    // Stand a rectangle against a side of another at a random offset, or with the other's middle at a share of the way along it
    private UnitRect? PlaceBeside(UnitRect at, Side side, int w, int h, float? entry = null)
    {
        bool across = side is Side.Top or Side.Bottom;
        int low = across ? at.U - w + MinShared : at.V - h + MinShared;
        int high = across ? at.U + at.W - MinShared : at.V + at.H - MinShared;
        if (high < low)
        {
            return null;
        }

        int extent = across ? w : h;
        int offset = _rng.Next(low, high + 1);
        if (entry is not null)
        {
            float middle = across ? at.U + at.W / 2f : at.V + at.H / 2f;
            offset = Math.Clamp((int)MathF.Round(middle - extent * entry.Value) + _rng.Next(-extent / EntryJitter, extent / EntryJitter + 1), low, high);
        }

        return side switch
        {
            Side.Top => new UnitRect(offset, at.V - h, w, h),
            Side.Bottom => new UnitRect(offset, at.V + at.H, w, h),
            Side.Left => new UnitRect(at.U - w, offset, w, h),
            _ => new UnitRect(at.U + at.W, offset, w, h)
        };
    }

    // Draw a width and depth for an area, mostly a little longer than square
    private (int W, int H) DrawSize(int area)
    {
        float stretch = 1 + 1.2f * MathF.Pow(_rng.NextSingle(), 1.5f);
        int w = Math.Max(MinSide, (int)MathF.Round(MathF.Sqrt(area * stretch)));
        int h = Math.Max(MinSide, (int)MathF.Round(area / (float)w));
        return _rng.Next(2) == 0 ? (w, h) : (h, w);
    }

    // Open most contacts between the rooms of the main body or of one branch's entrances within a section
    private void OpenBodyContacts()
    {
        var body = _pieces.Where(piece => piece.Role != Role.Branch && piece.Branch != KeyBranch).ToArray();
        for (int index = 0; index < body.Length; index++)
        {
            foreach (var other in body.Skip(index + 1))
            {
                if (body[index].Section == other.Section && body[index].Branch == other.Branch && !_joins.Contains(Order(body[index], other)) && Touches(body[index], other)
                    && _rng.NextSingle() < BodyDoorChance)
                {
                    Join(body[index], other);
                }
            }
        }
    }

    // Carve each room but the breaker's toward the box fill of an original room of a like size by cutting corners into steps
    private void ShapeRooms()
    {
        foreach (var piece in _pieces.Where(p => p.Role is not (Role.Hall or Role.Start) && !p.IsCorridor && p != _key && p.Branch != KeyBranch).OrderBy(_ => _rng.Next()).ToArray())
        {
            var like = _profile.Shapes.Where(shape => shape.Units / shape.Fill >= piece.Units.Count * ShapeRatio.Min && shape.Units / shape.Fill <= piece.Units.Count * ShapeRatio.Max).ToArray();
            float fill = like.Length == 0 ? 1 : like[_rng.Next(like.Length)].Fill;
            for (int attempt = 0; attempt < NotchTries && piece.Units.Count / (float)piece.Bounds.Area > fill + FillSlack; attempt++)
            {
                CutNotch(piece);
            }

            if (piece.Units.Count / (float)piece.Bounds.Area > fill + FillSlack)
            {
                CutCourt(piece, fill);
            }
        }
    }

    // Leave a court of void in the middle of a room that corner cuts could not bring down to its fill, as large as the fill asks with a band
    // around it as wide as a corridor at least
    private void CutCourt(Piece piece, float fill)
    {
        var box = piece.Bounds;
        int excess = piece.Units.Count - (int)(fill * box.Area);
        var court = Enumerable.Range(CorridorWidth, Math.Max(0, Math.Min(box.W, box.H) / 2 - CorridorWidth))
            .Select(band => new UnitRect(box.U + band, box.V + band, box.W - 2 * band, box.H - 2 * band))
            .Where(rect => rect is { W: >= MinSide, H: >= MinSide })
            .OrderBy(rect => Math.Abs(rect.Area - excess))
            .Cast<UnitRect?>()
            .FirstOrDefault();
        if (court is null || !court.Value.Units.All(piece.Units.Contains))
        {
            return;
        }

        var cut = court.Value.Units.ToHashSet();
        piece.Units.ExceptWith(cut);
        if (!IsSolid(piece.Units))
        {
            piece.Units.UnionWith(cut);
            return;
        }

        foreach (var cell in cut)
        {
            _owner.Remove(cell);
        }
    }

    // Cut a corner off a room, leaving the cut empty or giving it to the room across it, never through a contact a join needs or down to a
    // strip narrower than a room
    private void CutNotch(Piece piece)
    {
        var box = piece.Bounds;
        int cw = Math.Max(2, (int)MathF.Round(box.W * (NotchSide.Min + (NotchSide.Max - NotchSide.Min) * _rng.NextSingle())));
        int ch = Math.Max(2, (int)MathF.Round(box.H * (NotchSide.Min + (NotchSide.Max - NotchSide.Min) * _rng.NextSingle())));
        int corner = _rng.Next(4);
        var rect = new UnitRect(corner % 2 == 0 ? box.U : box.U + box.W - cw, corner < 2 ? box.V : box.V + box.H - ch, cw, ch);
        var cut = rect.Units
            .Where(piece.Units.Contains)
            .ToHashSet();
        
        if (cut.Count < 4 || piece.Units.Count - cut.Count < MinRoomUnits)
        {
            return;
        }

        var joined = _joins
            .Where(pair => pair.A == piece || pair.B == piece)
            .Select(pair => pair.A == piece ? pair.B : pair.A)
            .ToArray();
        
        piece.Units.ExceptWith(cut);
        if (!IsSolid(piece.Units) || joined.Any(other => GetContact(piece, other) < MinShared))
        {
            piece.Units.UnionWith(cut);
            return;
        }

        var neighbor = cut
            .SelectMany(Around)
            .Where(_owner.ContainsKey)
            .Select(cell => _owner[cell])
            .Where(other => other != piece && other.Role != Role.Hall)
            .GroupBy(other => other)
            .MaxBy(group => group.Count())?.Key;
        
        foreach (var cell in cut)
        {
            _owner.Remove(cell);
        }

        if (neighbor is not null && _rng.NextSingle() < NotchToNeighborChance && IsSolid(neighbor.Units.Concat(cut).ToHashSet()))
        {
            neighbor.Units.UnionWith(cut);
            foreach (var cell in cut)
            {
                _owner[cell] = neighbor;
            }
        }
    }

    // Tell whether units form one piece in which every unit stands in a square as wide as the narrowest room
    private static bool IsSolid(HashSet<Cell> units)
    {
        var covered = new HashSet<Cell>();
        foreach (var unit in units)
        {
            var square = new UnitRect(unit.U, unit.V, MinSide, MinSide);
            if (square.Units.All(units.Contains))
            {
                covered.UnionWith(square.Units);
            }
        }

        if (covered.Count != units.Count)
        {
            return false;
        }

        var reached = new HashSet<Cell> { units.First() };
        var queue = new Queue<Cell>(reached);
        while (queue.Count > 0)
        {
            foreach (var next in Around(queue.Dequeue()).Where(next => units.Contains(next) && reached.Add(next)))
            {
                queue.Enqueue(next);
            }
        }

        return reached.Count == units.Count;
    }

    // Leave pillars of void down the middle of wide halls
    private void RaisePillars()
    {
        foreach (var hall in _plan.Regions.Where(region => region.IsHall))
        {
            int minU = hall.Units.Min(unit => unit.U), minV = hall.Units.Min(unit => unit.V);
            var box = new UnitRect(minU, minV, hall.Units.Max(unit => unit.U) - minU + 1, hall.Units.Max(unit => unit.V) - minV + 1);
            bool along = box.W >= box.H;
            int length = along ? box.W : box.H, width = along ? box.H : box.W;
            if (width < PillarHallWidth)
            {
                continue;
            }

            int count = length / PillarSpacing - 1;
            for (int index = 1; index <= count; index++)
            {
                int at = (along ? box.U : box.V) + index * length / (count + 1) - PillarLength / 2;
                int middle = (along ? box.V : box.U) + width / 2;
                var pillar = along ? new UnitRect(at, middle, PillarLength, 1) : new UnitRect(middle, at, 1, PillarLength);
                var ring = new UnitRect(pillar.U - PillarMargin, pillar.V - PillarMargin, pillar.W + 2 * PillarMargin, pillar.H + 2 * PillarMargin);
                if (ring.Units.All(hall.Units.Contains))
                {
                    foreach (var unit in pillar.Units)
                    {
                        hall.Units.Remove(unit);
                        _plan.Occupancy.Remove(unit);
                    }
                }
            }
        }
    }

    // Turn the pieces into regions, the path of joins from the start to the end into the chain of stations, and hang every other room off
    // the room the walk from the chain first reaches it through
    private bool Commit(Piece end)
    {
        foreach (var piece in new[] { _start! }.Concat(_pieces.Where(p => p.Role == Role.Hall)).Concat(_pieces).Distinct())
        {
            var rects = DecomposeRects(piece.Units);
            var region = _plan.AddRegion(RegionKind.SideRoom, rects[0], 0, null, piece.Role == Role.Hall || piece.IsCorridor);
            rects.Skip(1).ToList().ForEach(rect => _plan.ExtendRegion(region, rect));
            _regions[piece] = region;
        }

        var boundaries = FindBoundaries();
        var lines = new Dictionary<(int, int), EdgeKey[]>();
        var neighbors = _plan.Regions.ToDictionary(region => region.Id, _ => new List<int>());
        foreach (var (a, b) in _joins)
        {
            (int first, int second) = (Math.Min(_regions[a].Id, _regions[b].Id), Math.Max(_regions[a].Id, _regions[b].Id));
            var line = boundaries.TryGetValue((first, second), out var edges) ? GetLongestRun(edges) : [];
            if (line.Length < MinShared)
            {
                return false;
            }

            lines[(first, second)] = line;
            neighbors[first].Add(second);
            neighbors[second].Add(first);
        }

        var path = FindPath(neighbors, _regions[_start!].Id, _regions[end].Id);
        if (path is null)
        {
            return false;
        }

        var linked = new HashSet<(int, int)>();
        for (int index = 0; index < path.Count; index++)
        {
            var station = _plan.Regions[path[index]];
            station.Kind = RegionKind.Station;
            station.Station = index;
            _plan.Stations.Add(station);
            if (index > 0)
            {
                Link(_plan.Regions[path[index - 1]], station);
            }
        }

        var queue = new Queue<int>(path);
        var reached = path.ToHashSet();
        while (queue.Count > 0)
        {
            var host = _plan.Regions[queue.Dequeue()];
            foreach (int next in neighbors[host.Id].OrderBy(_ => _rng.Next()).Where(reached.Add))
            {
                var room = _plan.Regions[next];
                room.Host = host.Id;
                room.Station = host.Station;
                Link(host, room);
                queue.Enqueue(next);
            }
        }

        if (reached.Count != _plan.Regions.Count)
        {
            return false;
        }

        var depth = MeasureHops(neighbors, path[0]);
        foreach ((int first, int second) in lines.Keys.Where(key => !linked.Contains(key)))
        {
            (int from, int to) = depth[first] <= depth[second] ? (first, second) : (second, first);
            Link(_plan.Regions[from], _plan.Regions[to]);
        }

        return true;

        void Link(PlanRegion from, PlanRegion to)
        {
            var key = (Math.Min(from.Id, to.Id), Math.Max(from.Id, to.Id));
            linked.Add(key);
            AddLink(from, to, lines[key], LinkKind.Arch);
        }
    }

    private static List<int>? FindPath(Dictionary<int, List<int>> neighbors, int from, int to)
    {
        var previous = new Dictionary<int, int> { [from] = from };
        var queue = new Queue<int>([from]);
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            int at = queue.Dequeue();
            foreach (int next in neighbors[at].Where(next => previous.TryAdd(next, at)))
            {
                queue.Enqueue(next);
            }
        }

        if (!previous.ContainsKey(to))
        {
            return null;
        }

        var path = new List<int> { to };
        while (path[^1] != from)
        {
            path.Add(previous[path[^1]]);
        }

        path.Reverse();
        return path;
    }

    private static Dictionary<int, int> MeasureHops(Dictionary<int, List<int>> neighbors, int from)
    {
        var hops = new Dictionary<int, int> { [from] = 0 };
        var queue = new Queue<int>([from]);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (int next in neighbors[at].Where(next => hops.TryAdd(next, hops[at] + 1)))
            {
                queue.Enqueue(next);
            }
        }

        return hops;
    }

    // Lock the neck and every branch off the hub, leaving the start, the hub and the breaker's room open, then open loot branches while more
    // of the floor is locked than the original locks
    private bool LockSections(GateRequest gate)
    {
        if (_plan.Stations.Count < HubStation + 2 || _plan.Stations[HubStation + 1] != _regions[_neck!] || _key is null)
        {
            return false;
        }

        var gateLink = GetChainLink(HubStation);
        var free = new HashSet<PlanRegion>(_plan.Stations.Take(HubStation + 1)) { _regions[_key] };
        float floor = _plan.Regions.Sum(region => region.Units.Count);
        float locked = 1 - free.Sum(region => region.Units.Count) / floor;
        
        var branches = _pieces.Where(piece => piece is { Branch: >= 0, Section: 0 })
            .GroupBy(piece => piece.Branch)
            .Select(group => group.Select(piece => _regions[piece]).ToList());
        
        foreach (var loot in branches.OrderBy(branch => branch.Count).ThenBy(_ => _rng.Next()))
        {
            float opened = locked - loot.Sum(region => region.Units.Count) / floor;
            if (locked > gate.Share + LockedShareSlack && Math.Abs(opened - gate.Share) < Math.Abs(locked - gate.Share))
            {
                free.UnionWith(loot);
                locked = opened;
            }
        }

        if (Math.Abs(locked - gate.Share) > LockedShareMiss)
        {
            return false;
        }

        foreach (var link in _plan.Links)
        {
            bool open = free.Contains(_plan.Regions[link.From]) == free.Contains(_plan.Regions[link.To]);
            SetLinkKind(link, open ? LinkKind.Arch : LinkKind.Door, allowDoor: !open);
        }

        foreach (var region in _plan.Regions)
        {
            region.IsGated = !free.Contains(region);
        }

        _plan.Gate = GateKind.LockedDoors;
        _plan.GateLink = gateLink;
        _plan.KeyRoom = _regions[_key];
        return true;
    }

    // Put the trigger at the hub's exit, the gate on the chain link that leaves the original's share behind it with a door on every link
    // across it, and the breaker in the leaf before the gate farthest from the trigger; lock the hub's branches behind doors and open every
    // other link
    private bool PlaceBlackout(GateRequest gate)
    {
        int count = _plan.Stations.Count;
        if (count < HubStation + 3)
        {
            return false;
        }

        var trigger = GetChainLink(HubStation);
        var walk = MeasureWalk(trigger.Edges.SelectMany(e => new[] { e.Cells.First, e.Cells.Second }).Where(_plan.Occupancy.ContainsKey), openOnly: false);
        
        var leaves = _key is not null ? [_regions[_key]] : _plan.Regions.Where(region => IsLeaf(region) && region.Station > HubStation)
            .OrderByDescending(region => region.Units.Where(walk.ContainsKey).Select(unit => walk[unit]).DefaultIfEmpty(0).Max())
            .ToArray();
        
        int cut = Enumerable.Range(HubStation + 1, count - HubStation - 2)
            .Where(k => leaves.Any(leaf => leaf.Station <= k))
            .OrderBy(k => Math.Abs(MeasureLocked(k) - gate.Share))
            .DefaultIfEmpty(-1)
            .First();
        
        if (cut < 0)
        {
            return false;
        }

        var key = leaves.First(leaf => leaf.Station <= cut);
        var gateLink = GetChainLink(cut);
        foreach (var region in _plan.Regions)
        {
            region.IsGated = region.Station > cut;
        }

        foreach (var link in _plan.Links)
        {
            bool shut = IsShut(link, cut);
            SetLinkKind(link, shut ? LinkKind.Door : LinkKind.Arch, allowDoor: shut);
        }

        LengthenSearch(trigger, key, gate.SearchPixels);
        _plan.Gate = GateKind.Blackout;
        _plan.BlackoutLink = trigger;
        _plan.GateLink = gateLink;
        _plan.KeyRoom = key;
        return true;
    }

    // Fence off a room whose walk from the start leads on to the original's share of the floor, a station with every later one or a side room
    // with the rooms hung off it, on every link across, nearest the share first, and give the generator the open room past the hub whose back
    // the tour reaches nearest the original's share of it among those far enough from the start and from every fence
    private bool PlaceFences(GateRequest gate)
    {
        float floor = _plan.Regions.Sum(region => region.Units.Count);
        var start = _plan.Stations[0].Rects[0];
        var fromStart = MeasureWalk([new Cell(start.U + start.W / 2, start.V + start.H / 2)], openOnly: false);
        var roles = _plan.MeasureRoles();
        
        var cuts = _plan.Regions.Where(region => region != _plan.Stations[0] && region.Kind is RegionKind.Station or RegionKind.SideRoom)
            .Select(region => (Root: region, Part: GetBehind(region)))
            .Select(cut => (cut.Root, cut.Part, Miss: Math.Abs(cut.Part.Sum(region => region.Units.Count) / floor - gate.Share)))
            .Where(cut => cut.Miss <= FencedShareMiss)
            .OrderBy(cut => roles.GetRole(cut.Root.Id, cut.Root.IsHall) == _profile.FenceRole ? 0 : 1)
            .ThenBy(cut => cut.Miss);
        
        foreach (var (root, part, _) in cuts)
        {
            foreach (var region in _plan.Regions)
            {
                region.IsGated = part.Contains(region);
            }

            var fences = _plan.Links
                .Where(link => part.Contains(_plan.Regions[link.From]) != part.Contains(_plan.Regions[link.To]))
                .ToArray();
            
            var fromFences = fences
                .Select(fence => MeasureWalk(fence.Edges.SelectMany(edge => new[] { edge.Cells.First, edge.Cells.Second }).Where(_plan.Occupancy.ContainsKey), openOnly: true))
                .ToArray();
            
            var candidates = _plan.Regions.Where(region => (region.Kind == RegionKind.SideRoom || region.Station > HubStation) && !region.IsGated)
                .Select(region => (Room: region, Back: FindBack(region)))
                .Where(c => fromStart.GetValueOrDefault(c.Back) * WalkPixelsPerUnit >= MinGeneratorWalk && fromFences.All(walk => walk.GetValueOrDefault(c.Back, int.MaxValue / 2) * WalkPixelsPerUnit >= MinFenceGap))
                .Select(c => (c.Room, WalkPixels: fromStart.GetValueOrDefault(c.Back) * WalkPixelsPerUnit))
                .ToArray();
            
            var key = candidates.OrderBy(c => Math.Abs(c.WalkPixels - Math.Clamp(gate.KeyProgress, KeyProgress.Min, KeyProgress.Max) * GeneratorWalkPerUnit * floor))
                .Select(c => c.Room)
                .FirstOrDefault();
            
            if (key is null)
            {
                continue;
            }

            foreach (var link in _plan.Links)
            {
                SetLinkKind(link, fences.Contains(link) ? LinkKind.Fence : LinkKind.Arch);
            }

            _plan.Gate = GateKind.Fences;
            _plan.GateLink = _plan.Links.First(link => link.To == root.Id && !part.Contains(_plan.Regions[link.From]));
            _plan.KeyRoom = key;
            _plan.GeneratorRooms = candidates;
            return true;
        }

        foreach (var region in _plan.Regions)
        {
            region.IsGated = false;
        }

        return false;
    }

    // Find the unit of a room farthest from its entrance, where the generator stands
    private Cell FindBack(PlanRegion region)
    {
        var entrance = _plan.Links.First(link => link.To == region.Id);
        var walk = MeasureWalk(entrance.Edges.SelectMany(edge => new[] { edge.Cells.First, edge.Cells.Second }).Where(region.Units.Contains), openOnly: false);
        return region.Units.MaxBy(walk.GetValueOrDefault)!;
    }

    // Collect a room and the rooms the walk from the start reaches only through it, every later station for a station and the rooms hung off
    // it for a side room
    private HashSet<PlanRegion> GetBehind(PlanRegion root)
    {
        var part = new HashSet<PlanRegion> { root };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var region in _plan.Regions.Where(region => !part.Contains(region)))
            {
                bool hung = region.Host is not null && part.Contains(_plan.Regions[region.Host.Value]);
                bool later = root.Kind == RegionKind.Station && region.Kind == RegionKind.Station && region.Station > root.Station;
                if (hung || later)
                {
                    part.Add(region);
                    grew = true;
                }
            }
        }

        return part;
    }

    // Fence leaves of the gated part in as pens
    private void AddPens(int count)
    {
        foreach (var pen in _plan.Regions.Where(region => region.IsGated && IsLeaf(region)).OrderBy(_ => _rng.Next()).Take(count).ToArray())
        {
            pen.Kind = RegionKind.Pen;
            SetLinkKind(_plan.Links.First(link => link.To == pen.Id), LinkKind.Fence);
        }
    }

    // Close the branch nearest the asked share behind doors on every link into it, with a leaf near one of those doors for the breaker, or
    // behind fences with the generator in a leaf far enough from them
    private bool CloseWing(GateRequest gate)
    {
        float floor = _plan.Regions.Sum(region => region.Units.Count);
        var wings = _pieces.Where(piece => piece.Branch >= 0).GroupBy(piece => piece.Branch).Select(group => group.Select(piece => _regions[piece]).ToList())
            .Where(branch => Math.Abs(branch.Sum(region => region.Units.Count) / floor - gate.Share) <= WingShareSlack && branch.Sum(region => region.Units.Count) / floor >= MinWingShare)
            .OrderBy(branch => _regions.First(pair => pair.Value == branch[0]).Key.Branch != _wing);
        
        foreach (var wing in wings)
        {
            var doors = _plan.Links.Where(link => wing.Contains(_plan.Regions[link.From]) != wing.Contains(_plan.Regions[link.To])).ToArray();
            bool fenced = gate.Kind == GateKind.Fences;
            var key = _plan.Regions.Where(region => IsLeaf(region) && !wing.Contains(region))
                .Select(region => (Region: region, Door: doors.MinBy(door => MeasureFarthestFrom(door, region))!))
                .Select(c => (c.Region, c.Door, Gap: MeasureFarthestFrom(c.Door, c.Region)))
                .Where(c => fenced ? c.Gap >= MinFenceGap : c.Gap <= MaxBreakerToGatePixels)
                .OrderBy(c => c.Gap)
                .FirstOrDefault();
            
            if (key.Region is null)
            {
                continue;
            }

            foreach (var region in wing)
            {
                region.IsGated = true;
                region.Kind = RegionKind.Wing;
            }

            foreach (var link in _plan.Links)
            {
                bool shut = doors.Contains(link);
                SetLinkKind(link, !shut ? LinkKind.Arch : fenced ? LinkKind.Fence : LinkKind.Door, allowDoor: shut);
            }

            _plan.Gate = fenced ? GateKind.Fences : GateKind.LockedDoors;
            _plan.GateLink = key.Door;
            _plan.KeyRoom = key.Region;
            return true;
        }

        return false;
    }

    // Wall up the cycle links between open rooms that most shorten the walk from the trigger to the breaker until the walk reaches the
    // search length
    private void LengthenSearch(PlanLink trigger, PlanRegion key, float searchPixels)
    {
        float walk = MeasureFarthestFrom(trigger, key);
        while (walk < searchPixels - SearchReach)
        {
            var best = _plan.Links
                .Where(link => link.Kind == LinkKind.Arch && !_plan.Regions[link.From].IsGated && !_plan.Regions[link.To].IsGated && _plan.Regions[link.To].Host != link.From
                    && !(_plan.Regions[link.From].Kind == RegionKind.Station && _plan.Regions[link.To].Kind == RegionKind.Station && _plan.Regions[link.To].Station == _plan.Regions[link.From].Station + 1))
                .ToArray()
                .Select(link => (Link: link, Walk: MeasureWithout(link)))
                .Where(c => c.Walk < float.MaxValue && c.Walk > walk)
                .OrderBy(c => Math.Abs(c.Walk - searchPixels))
                .FirstOrDefault();
            
            if (best.Link is null)
            {
                return;
            }

            _plan.Links.Remove(best.Link);
            walk = best.Walk;
        }

        float MeasureWithout(PlanLink link)
        {
            int index = _plan.Links.IndexOf(link);
            _plan.Links.RemoveAt(index);
            float measured = MeasureFarthestFrom(trigger, key);
            _plan.Links.Insert(index, link);
            return measured < Unreachable ? measured : float.MaxValue;
        }
    }

    // Shut the links across the cut and the entrances to the hub's branches
    private bool IsShut(PlanLink link, int cut)
    {
        var from = _plan.Regions[link.From];
        var to = _plan.Regions[link.To];
        return from.Station > cut != to.Station > cut || (to.Station <= HubStation && to.Kind == RegionKind.SideRoom && from.Kind == RegionKind.Station);
    }

    // Measure the share of the floor the start cannot reach without passing a shut link
    private float MeasureLocked(int cut)
    {
        var open = new HashSet<int> { _plan.Stations[0].Id };
        var queue = new Queue<int>(open);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (var link in _plan.Links.Where(link => (link.From == at || link.To == at) && !IsShut(link, cut)))
            {
                int next = link.From == at ? link.To : link.From;
                if (open.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return 1 - _plan.Regions.Where(region => open.Contains(region.Id)).Sum(region => region.Units.Count) / (float)_plan.Regions.Sum(region => region.Units.Count);
    }

    private PlanLink GetChainLink(int station) => _plan.Links.First(link => link.From == _plan.Stations[station].Id && link.To == _plan.Stations[station + 1].Id);

    private bool IsLeaf(PlanRegion region) =>
        region.Kind == RegionKind.SideRoom && _plan.Links.Count(link => link.From == region.Id || link.To == region.Id) == 1;

    private void CollectBranches()
    {
        foreach (var root in _plan.Regions.Where(region => region.Host is not null && _plan.Regions[region.Host.Value].Kind == RegionKind.Station && region.Kind != RegionKind.Station))
        {
            var tree = new List<PlanRegion> { root };
            for (int index = 0; index < tree.Count; index++)
            {
                tree.AddRange(_plan.Regions.Where(region => region.Host == tree[index].Id && region.Kind != RegionKind.Station));
            }

            _plan.Branches.Add(tree);
        }
    }

    private Piece AddPiece(Role role, UnitRect rect, int section, int branch = -1)
    {
        var piece = new Piece { Role = role, Section = section, Branch = branch };
        piece.Units.UnionWith(rect.Units);
        foreach (var unit in piece.Units)
        {
            _owner[unit] = piece;
        }

        _pieces.Add(piece);
        return piece;
    }

    private void Remove(List<Piece> pieces)
    {
        foreach (var piece in pieces)
        {
            piece.Units.ToList().ForEach(unit => _owner.Remove(unit));
            _pieces.Remove(piece);
        }

        _joins.RemoveWhere(pair => pieces.Contains(pair.A) || pieces.Contains(pair.B));
    }

    private void Join(Piece a, Piece b) => _joins.Add(Order(a, b));

    // Measure the walk in units from a room to every room it reaches through the joins laid so far, from middle to middle
    private Dictionary<Piece, float> MeasureJoinWalk(Piece from)
    {
        var walk = new Dictionary<Piece, float> { [from] = 0 };
        var queue = new PriorityQueue<Piece, float>();
        queue.Enqueue(from, 0);
        while (queue.TryDequeue(out var piece, out float distance))
        {
            if (distance > walk[piece])
            {
                continue;
            }

            foreach (var (a, b) in _joins.Where(pair => pair.A == piece || pair.B == piece))
            {
                var next = a == piece ? b : a;
                float step = distance + MathF.Abs(next.Bounds.U + next.Bounds.W / 2f - piece.Bounds.U - piece.Bounds.W / 2f) + MathF.Abs(next.Bounds.V + next.Bounds.H / 2f - piece.Bounds.V - piece.Bounds.H / 2f);
                if (step < walk.GetValueOrDefault(next, float.MaxValue))
                {
                    walk[next] = step;
                    queue.Enqueue(next, step);
                }
            }
        }

        return walk;
    }

    private (Piece, Piece) Order(Piece a, Piece b) => _pieces.IndexOf(a) < _pieces.IndexOf(b) ? (a, b) : (b, a);

    private bool IsFree(UnitRect rect) => rect.Units.All(unit => !_owner.ContainsKey(unit));

    private bool Touches(Piece a, Piece b) => GetContact(a, b) >= MinShared;

    private static int GetContact(Piece a, Piece b) => a.Units.Sum(unit => Around(unit).Count(b.Units.Contains));

    private Dictionary<Piece, int> CountContacts(UnitRect rect)
    {
        var contacts = new Dictionary<Piece, int>();
        foreach (var unit in rect.Units)
        {
            foreach (var next in Around(unit))
            {
                if (!rect.Contains(next) && _owner.TryGetValue(next, out var piece))
                {
                    contacts[piece] = contacts.GetValueOrDefault(piece) + 1;
                }
            }
        }

        return contacts;
    }

    private static IEnumerable<Cell> Around(Cell cell) => [cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 }];

    // Merge small enclosed holes, which can't take a roof tile, into the room beside them
    private void FillPockets()
    {
        int u0 = _plan.Occupancy.Keys.Min(c => c.U) - 1;
        int u1 = _plan.Occupancy.Keys.Max(c => c.U) + 1;
        int v0 = _plan.Occupancy.Keys.Min(c => c.V) - 1;
        int v1 = _plan.Occupancy.Keys.Max(c => c.V) + 1;
        var outside = new HashSet<Cell> { new(u0, v0) };
        var queue = new Queue<Cell>(outside);
        while (queue.Count > 0)
        {
            foreach (var (n, _) in EdgeKey.GetNeighbors(queue.Dequeue()))
            {
                if (IsInside(n) && !_plan.Occupancy.ContainsKey(n) && outside.Add(n))
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
                if (_plan.Occupancy.ContainsKey(start) || outside.Contains(start) || !seen.Add(start))
                {
                    continue;
                }

                var pocket = new List<Cell> { start };
                for (int cellIndex = 0; cellIndex < pocket.Count; cellIndex++)
                {
                    foreach (var (n, _) in EdgeKey.GetNeighbors(pocket[cellIndex]))
                    {
                        if (!_plan.Occupancy.ContainsKey(n) && seen.Add(n))
                        {
                            pocket.Add(n);
                        }
                    }
                }

                if (pocket.Count <= MaxPocketUnits)
                {
                    var host = GetPocketHost(pocket);
                    foreach (var cell in pocket)
                    {
                        _plan.Occupancy[cell] = host.Id;
                        host.Units.Add(cell);
                    }
                }
            }
        }

        return;

        bool IsInside(Cell c) => c.U >= u0 && c.U <= u1 && c.V >= v0 && c.V <= v1;
    }

    private PlanRegion GetPocketHost(List<Cell> pocket)
    {
        var shared = new Dictionary<int, int>();
        foreach (var cell in pocket)
        {
            foreach (var (n, _) in EdgeKey.GetNeighbors(cell))
            {
                if (_plan.Occupancy.TryGetValue(n, out int region))
                {
                    shared[region] = shared.GetValueOrDefault(region) + 1;
                }
            }
        }

        return shared.OrderBy(p => _plan.Regions[p.Key].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset ? 1 : 0)
            .ThenByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => _plan.Regions[p.Key]).First();
    }

    private void AddLink(PlanRegion from, PlanRegion to, EdgeKey[] boundary, LinkKind kind)
    {
        var link = new PlanLink { From = from.Id, To = to.Id, Kind = kind, Edges = [], Boundary = boundary };
        _plan.Links.Add(link);
        SetLinkKind(link, kind);
    }

    private void SetLinkKind(PlanLink link, LinkKind kind, bool allowDoor = true)
    {
        var all = link.Boundary;
        var inner = GetInnerBoundary(all);
        bool lab = _plan.Theme.Theme == LevelTheme.Lab;
        if (kind == LinkKind.Arch && inner.Length < (lab ? 2 : 1))
        {
            kind = lab && allowDoor ? LinkKind.Door : LinkKind.Open;
        }

        // Leave a wall stub beside military arches to keep each room's own walls
        int width = kind switch
        {
            LinkKind.Arch => Math.Min(Next(_plan.Theme.ArchWidth), lab ? inner.Length : Math.Max(1, inner.Length - 1)),
            LinkKind.Door => inner.Length >= 2 && _rng.NextSingle() < 0.45f ? 2 : 1,
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

    // Move each lab door or arch that stands closer than MinDoorGap to another one to the farthest clear span of its boundary
    private void SpaceDoors()
    {
        if (_plan.Theme.Theme != LevelTheme.Lab)
        {
            return;
        }

        var doorLinks = _plan.Links.Where(l => l is { Kind: LinkKind.Door or LinkKind.Arch, Edges.Count: > 0 }).ToList();
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
    private void AlignLinks()
    {
        foreach (var link in _plan.Links.Where(l => l.Kind != LinkKind.Open && l.Edges.Count > 0))
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

    private Dictionary<Cell, int> MeasureWalk(IEnumerable<Cell> sources, bool openOnly)
    {
        var passable = _plan.Links.Where(l => l.Kind is not (LinkKind.SecretWall or LinkKind.SpawnWall)).SelectMany(l => l.Edges).ToHashSet();
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
            int region = _plan.Occupancy[c];
            foreach (var (n, edge) in EdgeKey.GetNeighbors(c))
            {
                if (walk.ContainsKey(n) || !_plan.Occupancy.TryGetValue(n, out int other) || (openOnly && _plan.Regions[other].IsGated) || other != region && !passable.Contains(edge))
                {
                    continue;
                }

                walk[n] = walk[c] + 1;
                queue.Enqueue(n);
            }
        }

        return walk;
    }

    // Measure the walk in pixels from a link to the farthest unit of a region over the open part
    private float MeasureFarthestFrom(PlanLink link, PlanRegion region)
    {
        var sources = link.Edges.SelectMany(edge => new[] { edge.Cells.First, edge.Cells.Second }).Where(_plan.Occupancy.ContainsKey).Distinct();
        var walk = MeasureWalk(sources, openOnly: true);
        return region.Units.Select(unit => walk.GetValueOrDefault(unit, int.MaxValue / 2)).Max() * WalkPixelsPerUnit;
    }

    // Add breakable walls between touching open rooms at least three links apart
    private void AddShortcuts(int count)
    {
        if (count == 0)
        {
            return;
        }

        int blackout = _plan.BlackoutLink is null ? int.MaxValue : _plan.Regions[_plan.BlackoutLink.From].Station;
        var candidates = FindBoundaries()
            .Where(p => p.Value.Count >= 3 && IsOpenRoom(_plan.Regions[p.Key.First]) && IsOpenRoom(_plan.Regions[p.Key.Second]) && IsSameSide(p.Key.First, p.Key.Second)
                        && CountHops(p.Key.First, p.Key.Second) >= 3)
            .ToArray();

        _rng.Shuffle(candidates);

        foreach (((int a, int b), var edges) in candidates.Take(count))
        {
            var sorted = SortEdges(edges);
            _plan.Links.Add(new PlanLink { From = a, To = b, Kind = LinkKind.SecretWall, Edges = [sorted[sorted.Length / 2]], Boundary = sorted });
        }

        return;

        bool IsSameSide(int a, int b) => _plan.Regions[a].Station > blackout == _plan.Regions[b].Station > blackout;

        bool IsOpenRoom(PlanRegion r) => r.Kind is RegionKind.Station or RegionKind.SideRoom && !r.IsGated && r != _plan.KeyRoom;
    }

    private Dictionary<(int First, int Second), List<EdgeKey>> FindBoundaries()
    {
        var boundaries = new Dictionary<(int First, int Second), List<EdgeKey>>();
        foreach ((var unit, int area) in _plan.Occupancy.OrderBy(p => p.Key.U).ThenBy(p => p.Key.V))
        {
            foreach (var (n, k) in EdgeKey.GetNeighbors(unit))
            {
                if (!_plan.Occupancy.TryGetValue(n, out int b) || b <= area)
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

    private static EdgeKey[] GetLongestRun(List<EdgeKey> edges)
    {
        EdgeKey[] best = [];
        foreach (var line in edges.GroupBy(k => (k.Axis, Line: k.Axis == 'v' ? k.B : k.A)))
        {
            var sorted = SortEdges(line);
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

    private static EdgeKey[] SortEdges(IEnumerable<EdgeKey> edges) => edges
        .OrderBy(k => k.Axis)
        .ThenBy(k => k.A)
        .ThenBy(k => k.B)
        .ToArray();

    private int CountHops(int from, int to)
    {
        var hops = new Dictionary<int, int> { [from] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int r = queue.Dequeue();
            foreach (var link in _plan.Links)
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

    private void AddClosets(int count, RegionKind kind, LinkKind linkKind, IReadOnlyList<PlanRegion> hosts, Func<(int W, int H)> drawSize)
    {
        if (hosts.Count == 0)
        {
            return;
        }

        int made = 0;
        for (int tries = 0; made < count && tries < count * 80; tries++)
        {
            var host = hosts[_rng.Next(hosts.Count)];
            var hr = host.Rects[_rng.Next(host.Rects.Count)];
            (int w, int h) = drawSize();
            // Open secret closets in the host's far walls first, where the camera sees the breakable wall
            var side = kind is RegionKind.SecretCloset or RegionKind.SecretRoom && tries < count * FarClosetTries ? FarHeadings[_rng.Next(FarHeadings.Length)] : Headings[_rng.Next(4)];
            UnitRect? r = side switch
            {
                (1, 0) when hr.H - h - 1 >= 1 => new UnitRect(hr.U + hr.W, _rng.Next(hr.V + 1, hr.V + hr.H - h), w, h),
                (-1, 0) when hr.H - h - 1 >= 1 => new UnitRect(hr.U - w, _rng.Next(hr.V + 1, hr.V + hr.H - h), w, h),
                (0, 1) when hr.W - w - 1 >= 1 => new UnitRect(_rng.Next(hr.U + 1, hr.U + hr.W - w), hr.V + hr.H, w, h),
                (0, -1) when hr.W - w - 1 >= 1 => new UnitRect(_rng.Next(hr.U + 1, hr.U + hr.W - w), hr.V - h, w, h),
                _ => null
            };
            if (r is null || !_plan.IsFree(r.Value) || _plan.TouchesOtherAtCorner(r.Value, host.Id))
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
            _plan.Links.Add(new PlanLink { From = host.Id, To = closet.Id, Kind = linkKind, Edges = [shared[shared.Length / 2]], Boundary = shared });
            made++;
        }
    }

    private int Next((int Min, int Max) range) => _rng.Next(range.Min, range.Max + 1);

    private sealed class Piece
    {
        public HashSet<Cell> Units { get; } = [];

        public required Role Role { get; init; }

        public required int Section { get; init; }

        public required int Branch { get; init; }

        public bool IsCorridor { get; set; }

        public UnitRect Bounds
        {
            get
            {
                int minU = Units.Min(unit => unit.U), minV = Units.Min(unit => unit.V);
                return new UnitRect(minU, minV, Units.Max(unit => unit.U) - minU + 1, Units.Max(unit => unit.V) - minV + 1);
            }
        }
    }
}
