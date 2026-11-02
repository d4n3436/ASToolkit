using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Layout;

// Lay a mission graph out as spines of path rooms with each branch tree as one block of partitioned rows along its path room's sides
internal sealed class BlockLayout
{

    private const int MinHallLength = 4;
    private const int MinArea = 6;
    private const float MaxStretch = 0.8f;
    private const float SpineEntryWeight = 4;
    private const int ExitDepth = 8;
    private const float SideJitter = 0.2f;
    private const float MaxCutShare = 0.45f;
    private const int PathMatchBonus = 2;
    private const int BridgeReach = 6;
    private const int MinConnectorLength = 2;
    private const int MinStep = 2;
    private const int MinLoopSteps = 2;
    private const int MaxLoopSteps = 4;

    private static readonly float[] Shrinks = [1f, 1f, 0.7f, 0.5f, 0.7f];
    private static readonly int[] CorridorLengths = [0, 2, 4, 6, 8, 12, 16];
    private static readonly ExitSide[] SideOrder = [ExitSide.Far, ExitSide.Left, ExitSide.Right];

    private readonly int _doorMin;
    private readonly int _exitWidth;
    private readonly int _minCut;
    private readonly AreaThemeModel _model;
    private readonly Random _rng;
    private readonly AreaBuildCounts _counts;
    private readonly LevelTheme _theme;
    private readonly RoomPacker _packer = new();
    private readonly Dictionary<int, List<int>> _children = [];
    private readonly Dictionary<int, int> _parent = [];
    private readonly Dictionary<int, MissionRole> _sizeRoles = [];
    private readonly HashSet<int> _spine = [];
    private readonly HashSet<int> _halls = [];
    private readonly HashSet<int> _connectors = [];
    private readonly Dictionary<int, HashSet<int>> _partners = [];
    private readonly Dictionary<int, Box> _boxOf = [];
    private readonly Dictionary<int, Heading> _headingOf = [];
    private readonly Dictionary<int, (List<(int Corner, int W, int H)> Cuts, int W, int H)> _planned = [];
    private readonly Dictionary<int, Dictionary<int, Heading>> _exits = [];
    private readonly List<(Box Corridor, int A, int B)> _corridors = [];
    private Dictionary<int, Box> _localReserves = [];
    private (int Low, int High)? _entrySpan;
    private float _shrink = 1f;
    private readonly Dictionary<int, float> _ratios = [];
    private readonly Dictionary<int, HallSize> _hallSizes = [];
    private readonly HashSet<int> _rings = [];
    private float _levelScale = 1f;

    public BlockLayout(AreaThemeModel model, LevelTheme theme, Random rng, AreaBuildCounts counts)
    {
        _model = model;
        _theme = theme;
        _doorMin = (theme == LevelTheme.Lab ? ThemeSettings.Lab : ThemeSettings.Military).MinShared;
        _exitWidth = _doorMin + 4;
        _minCut = theme == LevelTheme.Lab ? 2 : 1;
        _rng = rng;
        _counts = counts;
    }

    // Draw every room's size ratio and hall, turn the largest halls back into path rooms while the rest of the floor leaves the rooms less than
    // the smallest room scale, then size the rooms so the drawn floor meets the planned floor within the room scales the fitting levels show
    private float GetLevelScale(IEnumerable<int> rooms, float floorUnits)
    {
        float connectorArea = _model.Connector is null ? 0 : (float)(_model.Connector.Widths.Average() * _model.Connector.Lengths.Average());
        float fixedArea = 0;
        float scaled = 0;
        foreach (int room in rooms)
        {
            if (_connectors.Contains(room))
            {
                fixedArea += connectorArea;
            }
            else if (_halls.Contains(room) && _model.Halls.Count > 0)
            {
                var hall = _model.Halls[_rng.Next(_model.Halls.Count)];
                _hallSizes[room] = hall;
                fixedArea += hall.Length * hall.Width;
            }
            else
            {
                var sizes = GetRoleSizes(room);
                float ratio = sizes[_rng.Next(sizes.Count)];
                _ratios[room] = ratio;
                scaled += ratio;
            }
        }

        float least = _model.Scales.Min();
        foreach (int hall in _hallSizes.Keys.OrderByDescending(hall => _hallSizes[hall].Length * _hallSizes[hall].Width).ToArray())
        {
            if ((floorUnits - fixedArea) / Math.Max(1, scaled) >= least)
            {
                break;
            }

            fixedArea -= _hallSizes[hall].Length * _hallSizes[hall].Width;
            _hallSizes.Remove(hall);
            _halls.Remove(hall);
            var sizes = GetRoleSizes(hall);
            _ratios[hall] = sizes[_rng.Next(sizes.Count)];
            scaled += _ratios[hall];
        }

        return Math.Clamp((floorUnits - fixedArea) / Math.Max(1, scaled), least, _model.Scales.Max());
    }

    private IReadOnlyList<float> GetRoleSizes(int room)
    {
        var role = _sizeRoles.GetValueOrDefault(room, MissionRole.Branch);
        return _model.RoleSizes[role is MissionRole.Start or MissionRole.End or MissionRole.Key or MissionRole.Path ? role : MissionRole.Branch];
    }

    public BlockResult Lay(MissionGraph mission, float floorUnits)
    {
        var phases = mission.Progression.Phases;
        int start = phases[0].Path[0];
        var phaseOf = new Dictionary<int, int>();
        foreach (var phase in phases)
        {
            foreach (int place in phase.Places)
            {
                phaseOf.TryAdd(place, phase.Index);
            }
        }

        var neighbors = new Dictionary<int, HashSet<int>>();
        foreach (int room in phaseOf.Keys)
        {
            neighbors[room] = [];
        }

        foreach (var phase in phases)
        {
            _spine.UnionWith(phase.Path);
        }

        _spine.Add(start);
        foreach (var link in mission.Links.Where(link => link.Kind == MissionLinkKind.Tree && link.From != link.To))
        {
            GetSet(neighbors, link.From).Add(link.To);
            GetSet(neighbors, link.To).Add(link.From);
        }

        AssignRoles(mission);
        BuildTree(neighbors, start);
        EnterBranchesFromSources(mission);
        foreach (var link in mission.Links.Where(link => link.Kind == MissionLinkKind.Loop))
        {
            GetSet(_partners, link.From).Add(link.To);
            GetSet(_partners, link.To).Add(link.From);
        }

        int hallCount = neighbors.Keys.Count(room => _sizeRoles.GetValueOrDefault(room) == MissionRole.Path && _rng.NextSingle() < _model.HallShare);

        var halls = neighbors.Keys
            .Where(_spine.Contains)
            .Select(room => (Room: room, Draw: _rng.NextDouble()))
            .OrderBy(entry => -GetChildren(entry.Room).Count)
            .ThenBy(entry => entry.Draw)
            .Take(hallCount)
            .Select(entry => entry.Room);

        _halls.UnionWith(halls);
        if (_model.Connector is not null)
        {
            foreach (int room in neighbors.Keys.Order())
            {
                var role = _sizeRoles.GetValueOrDefault(room, MissionRole.Branch);
                if (neighbors[room].Count == 2 && GetChildren(room).Count == 1 && room != start && !_halls.Contains(room) && role is MissionRole.Path or MissionRole.Branch
                    && _rng.NextSingle() < _model.Connector.Share)
                {
                    _connectors.Add(room);
                }
            }

            _counts.Connectors += _connectors.Count;
        }

        _levelScale = GetLevelScale(neighbors.Keys, floorUnits);

        int failures = PlaceAll(start);
        var cells = new Dictionary<int, HashSet<Cell>>();
        foreach (var box in _packer.Placed.Where(box => box.Kind is BoxKind.Room or BoxKind.Neck))
        {
            var set = GetCells(cells, box.Room);
            AddCells(set, box);
        }

        var result = new BlockResult
        {
            Cells = cells,
            Corridors = _corridors.Select(entry => (GetBoxCells(entry.Corridor), entry.A, entry.B)).ToList(),
            Courts = _packer.Placed.Where(box => box.Kind == BoxKind.Court).SelectMany(GetBoxCells).ToHashSet(),
            PhaseOf = phaseOf,
            Parent = _parent,
            Spine = _spine,
            Halls = _halls,
            Connectors = _connectors,
            Decided = _planned.Keys.ToHashSet(),
            Start = start,
            End = mission.Roles.FirstOrDefault(pair => pair.Value == MissionRole.End, new KeyValuePair<int, MissionRole>(start, MissionRole.End)).Key,
            Failures = failures
        };

        foreach ((int child, int parent) in _parent)
        {
            if (cells.ContainsKey(parent) && cells.ContainsKey(child) && (CountContacts(cells, parent, child) >= _doorMin || _corridors.Any(entry => IsPair(entry, parent, child))))
            {
                result.Realized.Add((parent, child, MissionLinkKind.Tree));
            }
        }

        AddEntrances(mission, result);
        AddLoops(mission, result);
        foreach ((int room, var plan) in _planned)
        {
            if (plan.Cuts.Count > 0 && _boxOf.ContainsKey(room) && _headingOf.TryGetValue(room, out var heading))
            {
                result.Cuts[room] = GetGlobalCuts(plan.Cuts, plan.W, plan.H, heading);
            }
        }

        return result;
    }

    private static bool IsPair((Box Corridor, int A, int B) entry, int a, int b) => (entry.A == a && entry.B == b) || (entry.A == b && entry.B == a);

    private static HashSet<int> GetSet(Dictionary<int, HashSet<int>> sets, int key)
    {
        if (!sets.TryGetValue(key, out var set))
        {
            set = [];
            sets[key] = set;
        }

        return set;
    }

    private static HashSet<Cell> GetCells(Dictionary<int, HashSet<Cell>> cells, int key)
    {
        if (!cells.TryGetValue(key, out var set))
        {
            set = [];
            cells[key] = set;
        }

        return set;
    }

    private static void AddCells(HashSet<Cell> set, Box box)
    {
        for (int u = box.X0; u < box.X1; u++)
        {
            for (int v = box.Y0; v < box.Y1; v++)
            {
                set.Add(new Cell(u, v));
            }
        }
    }

    private static HashSet<Cell> GetBoxCells(Box box)
    {
        var set = new HashSet<Cell>();
        AddCells(set, box);
        return set;
    }

    private static int CountContacts(Dictionary<int, HashSet<Cell>> cells, int a, int b)
    {
        var other = cells[b];
        int count = 0;
        foreach (var cell in cells[a])
        {
            count += (other.Contains(cell with { U = cell.U + 1 }) ? 1 : 0) + (other.Contains(cell with { U = cell.U - 1 }) ? 1 : 0)
                + (other.Contains(cell with { V = cell.V + 1 }) ? 1 : 0) + (other.Contains(cell with { V = cell.V - 1 }) ? 1 : 0);
        }

        return count;
    }

    private static int FloorDivide(int value, int divisor) => (int)Math.Floor(value / (double)divisor);

    private List<int> GetChildren(int room) => _children.GetValueOrDefault(room) ?? [];

    private void AssignRoles(MissionGraph mission)
    {
        foreach (var phase in mission.Progression.Phases)
        {
            foreach (int room in phase.Path.Where(phase.Places.Contains))
            {
                _sizeRoles.TryAdd(room, MissionRole.Path);
            }

            foreach (int room in phase.Branches.SelectMany(branch => branch.Places))
            {
                _sizeRoles.TryAdd(room, MissionRole.Branch);
            }
        }

        foreach ((int room, var role) in mission.Roles)
        {
            if (role is MissionRole.Key or MissionRole.End or MissionRole.Start)
            {
                _sizeRoles[room] = role;
            }
        }
    }

    // Reach every room from the start over the shortest walk that enters a path room from another path room when it can
    private void BuildTree(Dictionary<int, HashSet<int>> neighbors, int start)
    {
        var distance = new Dictionary<int, float> { [start] = 0 };
        var predecessors = new Dictionary<int, List<int>>();
        var done = new HashSet<int>();
        while (true)
        {
            int? next = null;
            foreach ((int room, float value) in distance)
            {
                if (!done.Contains(room) && (next is null || value < distance[next.Value] || (value.Equals(distance[next.Value]) && room < next.Value)))
                {
                    next = room;
                }
            }

            if (next is null)
            {
                break;
            }

            int current = next.Value;
            done.Add(current);
            foreach (int neighbor in neighbors[current])
            {
                float candidate = distance[current] + (_spine.Contains(neighbor) && !_spine.Contains(current) ? SpineEntryWeight : 1);
                if (!distance.TryGetValue(neighbor, out float known) || candidate < known)
                {
                    distance[neighbor] = candidate;
                    predecessors[neighbor] = [current];
                }
                else if (candidate.Equals(known))
                {
                    predecessors[neighbor].Add(current);
                }
            }
        }

        foreach (int room in distance.Keys.Where(room => room != start).OrderBy(room => distance[room]).ThenBy(room => room))
        {
            int parent = predecessors[room]
                .OrderBy(candidate => _spine.Contains(candidate) ? 0 : 1)
                .ThenBy(candidate => candidate)
                .First();

            _parent[room] = parent;
            if (!_children.TryGetValue(parent, out var list))
            {
                list = [];
                _children[parent] = list;
            }

            list.Add(room);
        }
    }

    private int PlaceAll(int start)
    {
        int failures = 0;
        var pending = new Queue<(int Room, int? Host, bool IsBlock)>();
        pending.Enqueue((start, null, false));
        while (pending.Count > 0)
        {
            (int room, int? host, bool isBlock) = pending.Dequeue();
            var neck = host is not null ? DrawNeck(room, host.Value) : null;
            List<Box> local = [];
            Dictionary<int, ExitSide> exitSides = [];
            (Heading Heading, int X, int Y, Box? Corridor)? fit = null;
            for (int attempt = 0; attempt < Shrinks.Length; attempt++)
            {
                _shrink = Shrinks[attempt];
                _entrySpan = null;
                _localReserves = [];
                if (isBlock)
                {
                    local = Block(room).Boxes;
                    int rootStart = local[0].X0;
                    local = local.Select(box => box.Shift(-rootStart, 0)).ToList();
                    exitSides = [];
                }
                else
                {
                    (local, exitSides) = Compose(room, bare: attempt == Shrinks.Length - 1);
                    _counts.BareComposites += attempt == Shrinks.Length - 1 ? 1 : 0;
                }

                fit = host is null
                    ? (Heading.North, 0, 0, null)
                    : Attach(local, _boxOf[host.Value], _exits.GetValueOrDefault(host.Value)?.GetValueOrDefault(room), room, neck);

                _counts.Composites++;
                if (fit is not null)
                {
                    break;
                }

                _counts.CompositeRedraws++;
            }

            if (fit is null)
            {
                failures++;
                continue;
            }

            (var heading, int ox, int oy, var corridor) = fit.Value;
            _headingOf[room] = heading;
            _packer.RemoveReserve(room);
            if (corridor is not null)
            {
                _packer.Add(corridor.Value);
                if (corridor.Value.Kind == BoxKind.Corridor)
                {
                    _corridors.Add((corridor.Value, host!.Value, room));
                    _counts.Corridors++;
                }
                else
                {
                    _counts.NecksBuilt++;
                }
            }

            foreach (var box in local)
            {
                var placed = box.ToGlobal(heading, ox, oy);
                _packer.Add(placed);
                if (placed.Kind == BoxKind.Room)
                {
                    _boxOf[placed.Room] = placed;
                }
            }

            foreach ((int child, var side) in exitSides)
            {
                GetExits(room)[child] = RoomPacker.Turn(heading, side);
                if (_localReserves.TryGetValue(child, out var reserve))
                {
                    _packer.Add(reserve.ToGlobal(heading, ox, oy));
                }
            }

            var inLocal = local.Where(box => box.Kind == BoxKind.Room).Select(box => box.Room).ToHashSet();
            foreach (int member in local.Where(box => box.Kind == BoxKind.Room).Select(box => box.Room))
            {
                foreach (int child in GetChildren(member))
                {
                    if (_spine.Contains(child) && !_boxOf.ContainsKey(child))
                    {
                        pending.Enqueue((child, member, false));
                    }
                    else if (member == room && !isBlock && !_boxOf.ContainsKey(child) && !inLocal.Contains(child))
                    {
                        pending.Enqueue((child, member, true));
                    }
                }
            }
        }

        return failures;
    }

    private Dictionary<int, Heading> GetExits(int room)
    {
        if (!_exits.TryGetValue(room, out var exits))
        {
            exits = [];
            _exits[room] = exits;
        }

        return exits;
    }

    // Draw whether the door into the entered room opens through a neck, returning its width, depth and owner
    private (int Width, int Depth, int Owner)? DrawNeck(int entered, int host)
    {
        if (_model.Neck is null)
        {
            return null;
        }

        var fit = _model.Neck.Value;
        if (_rng.NextSingle() >= fit.LinkShare)
        {
            return null;
        }

        _counts.NecksDrawn++;
        int width = Math.Max(_doorMin, (int)Math.Round(fit.Width.Draw(_rng)));
        int depth = Math.Max(MinStep, (int)Math.Round(fit.Length.Draw(_rng)));
        int owner = _rng.NextSingle() < fit.EnteredShare || _halls.Contains(host) ? entered : host;
        if (_halls.Contains(owner))
        {
            owner = owner == entered ? host : entered;
        }

        return (width, depth, owner);
    }

    private (int W, int H) DrawRoomSize(int room)
    {
        if (_connectors.Contains(room))
        {
            var connector = _model.Connector!;
            return (Math.Max(_doorMin, (int)Math.Round(connector.Widths[_rng.Next(connector.Widths.Count)])),
                Math.Max(MinConnectorLength, (int)Math.Round(connector.Lengths[_rng.Next(connector.Lengths.Count)])));
        }

        if (_halls.Contains(room) && _model.Halls.Count > 0)
        {
            if (!_hallSizes.TryGetValue(room, out var hall))
            {
                hall = _model.Halls[_rng.Next(_model.Halls.Count)];
            }

            float scale = MathF.Sqrt(_shrink);
            return (Math.Max(_doorMin, (int)Math.Round(hall.Width * scale)), Math.Max(MinHallLength, (int)Math.Round(hall.Length * scale)));
        }

        if (!_ratios.TryGetValue(room, out float ratio))
        {
            var sizes = GetRoleSizes(room);
            ratio = sizes[_rng.Next(sizes.Count)];
        }

        float area = _shrink * Math.Max(MinArea, _levelScale * ratio);
        float stretch = 1 + MaxStretch * _rng.NextSingle();
        int w = Math.Max(_doorMin, (int)Math.Round(MathF.Sqrt(area * stretch)));
        int h = Math.Max(_doorMin, (int)Math.Round(area / w));
        return _rng.NextSingle() < 0.5f ? (w, h) : (h, w);
    }

    // Lay a branch chain whose last room loops back to the host as a ring around a drawn court, entered across y = 0 with the chain's first and
    // last rooms on the host's wall and the rest across the court's far side
    private (List<Box> Boxes, int Span)? Ring(int root, int host)
    {
        if (_model.Courts.Count == 0)
        {
            return null;
        }

        var chain = new List<int> { root };
        for (var kids = GetChildren(root); kids.Count > 0; kids = GetChildren(chain[^1]))
        {
            if (kids.Count > 1 || _spine.Contains(kids[0]))
            {
                return null;
            }

            chain.Add(kids[0]);
        }

        if (chain.Count < 3 || !_partners.TryGetValue(chain[^1], out var partners) || !partners.Contains(host))
        {
            return null;
        }

        var court = _model.Courts[_rng.Next(_model.Courts.Count)];
        int[] sides = chain.Select(room => DrawRoomSize(room)).Select(size => Math.Max(_doorMin, Math.Min(size.W, size.H))).ToArray();
        int across = chain.Count - 2;
        int width = Math.Max(across * _doorMin, (int)Math.Round(court.Width * MathF.Sqrt(_shrink)));
        int depth = Math.Max(_doorMin, (int)Math.Round(court.Depth * MathF.Sqrt(_shrink)));
        int band = Math.Max(_doorMin, (int)Math.Round(sides.Skip(1).SkipLast(1).Average()));
        int left = sides[0];
        var boxes = new List<Box>
        {
            new(0, 0, left, depth + band, BoxKind.Room, root),
            new(left, 0, left + width, depth, BoxKind.Court, root)
        };

        for (int index = 0; index < across; index++)
        {
            int x0 = left + width * index / across;
            int x1 = left + width * (index + 1) / across;
            boxes.Add(new Box(x0, depth, x1, depth + band, BoxKind.Room, chain[index + 1]));
        }

        boxes.Add(new Box(left + width, 0, left + width + sides[^1], depth + band, BoxKind.Room, chain[^1]));
        _rings.Add(root);
        return (boxes, left + width + sides[^1]);
    }

    // Lay a branch tree out as one block entered across y = 0 with each room spanning its share of its parent's far wall and siblings one row deep
    private (List<Box> Boxes, int Span) Block(int room)
    {
        int[] kids = GetChildren(room).Where(kid => !_spine.Contains(kid)).ToArray();
        (int w, int h) = DrawRoomSize(room);
        if (kids.Length == 0)
        {
            return ([new Box(0, 0, w, h, BoxKind.Room, room)], w);
        }

        var subs = kids.Select(Block).ToArray();
        if (_connectors.Contains(room) && kids.Length == 1)
        {
            var kidRoom = subs[0].Boxes.First(box => box.Kind == BoxKind.Room && box.Room == kids[0]);
            int shift = FloorDivide(w - kidRoom.X0 - kidRoom.X1, 2);
            var boxes = new List<Box> { new(0, 0, w, h, BoxKind.Room, room) };
            boxes.AddRange(subs[0].Boxes.Select(box => box.Shift(shift, h)));
            return Normalize(boxes);
        }

        if (kids.Length == 1 && !_connectors.Contains(room) && _rng.NextSingle() < _model.BranchTurn)
        {
            var turned = TurnChild(room, w, h, kids[0], subs[0]);
            if (turned is not null)
            {
                _counts.BranchTurns++;
                return turned.Value;
            }
        }

        int need = subs.Sum(sub => sub.Span);
        int span = Math.Max(w, need);
        var rects = new List<Box> { new(0, 0, span, h, BoxKind.Room, room) };
        int extra = span - need;
        int x = 0;
        var ownNecks = new List<(int Low, int High, int Depth)>();
        for (int index = 0; index < subs.Length; index++)
        {
            (var boxes, int width) = subs[index];
            int grow = extra / subs.Length + (index < extra % subs.Length ? 1 : 0);
            var neck = DrawNeck(kids[index], room);
            int depth = 0;
            if (neck is not null)
            {
                var kidRoom = boxes.First(box => box.Kind == BoxKind.Room && box.Room == kids[index]);
                int kidHigh = x + kidRoom.X1 + (kidRoom.X1 == width ? grow : 0);
                var placed = PlaceNeck(x + kidRoom.X0, x + kidRoom.X1, neck.Value, room, (0, span), kids[index], (x + kidRoom.X0, kidHigh), ownNecks);
                if (placed is not null)
                {
                    if (placed.Value.Owner == room)
                    {
                        ownNecks.Add((placed.Value.Start, placed.Value.Start + placed.Value.Width, neck.Value.Depth));
                    }

                    depth = neck.Value.Depth;
                    rects.Add(new Box(placed.Value.Start, h, placed.Value.Start + placed.Value.Width, h + depth, BoxKind.Neck, placed.Value.Owner));
                    _counts.NecksBuilt++;
                }
            }

            foreach (var box in boxes)
            {
                var grown = box.Kind == BoxKind.Room && box.Room == kids[index] && box.X1 == width ? box with { X1 = box.X1 + grow } : box;
                rects.Add(grown.Shift(x, h + depth));
            }

            x += width + grow;
        }

        return (rects, span);
    }

    private static (List<Box> Boxes, int Span) Normalize(List<Box> boxes)
    {
        int low = boxes.Min(box => box.X0);
        var shifted = boxes.Select(box => box.Shift(-low, 0)).ToList();
        return (shifted, shifted.Max(box => box.X1));
    }

    // Put a room's one child block on its left or right side ahead of the entry
    private (List<Box> Boxes, int Span)? TurnChild(int room, int w, int h, int kid, (List<Box> Boxes, int Span) sub)
    {
        var kidBox = sub.Boxes.First(box => box.Kind == BoxKind.Room && box.Room == kid);
        int kidWidth = kidBox.X1;
        var options = new List<(Heading Heading, int X, int Y)>();
        for (int oy = 0; oy <= h - _doorMin; oy++)
        {
            options.Add((Heading.West, 0, oy));
        }

        for (int oy = Math.Max(sub.Span, _doorMin); oy <= h + kidWidth - _doorMin; oy++)
        {
            options.Add((Heading.East, w, oy));
        }

        var parent = new Box(0, 0, w, h, BoxKind.Room, room);
        var kidRoom = sub.Boxes.First(box => box.Kind == BoxKind.Room && box.Room == kid);
        var valid = options
            .Where(option => sub.Boxes.All(box => box.ToGlobal(option.Heading, option.X, option.Y).Y0 >= 0)
                && kidRoom.ToGlobal(option.Heading, option.X, option.Y).GetContact(parent) >= _doorMin)
            .ToArray();

        if (valid.Length == 0)
        {
            return null;
        }

        var chosen = valid[_rng.Next(valid.Length)];
        var boxes = new List<Box> { new(0, 0, w, h, BoxKind.Room, room) };
        boxes.AddRange(sub.Boxes.Select(box => box.ToGlobal(chosen.Heading, chosen.X, chosen.Y)));
        return Normalize(boxes);
    }

    // Lay a path room out entered across y = 0 with its blocks along its sides and a reservation in front of each exit
    private (List<Box> Boxes, Dictionary<int, ExitSide> ExitSides) Compose(int room, bool bare)
    {
        (int w, int h) = DrawRoomSize(room);
        if (_halls.Contains(room) && h < w)
        {
            (w, h) = (h, w);
        }

        int[] spineChildren = GetChildren(room).Where(_spine.Contains).ToArray();
        var exitSides = new Dictionary<int, ExitSide>();
        if (spineChildren.Length > 0)
        {
            var turns = _model.Turns;
            float draw = _rng.NextSingle() * (turns.Straight + turns.Turn + turns.Back);
            bool straight = draw < turns.Straight;
            var first = straight || _connectors.Contains(room) ? ExitSide.Far : _rng.Next(2) == 0 ? ExitSide.Left : ExitSide.Right;
            var order = new[] { first }.Concat(SideOrder.Where(side => side != first)).ToArray();
            for (int index = 0; index < spineChildren.Length; index++)
            {
                exitSides[spineChildren[index]] = index < order.Length ? order[index] : ExitSide.Far;
            }
        }

        var blocks = bare ? [] : GetChildren(room).Where(kid => !_spine.Contains(kid)).Select(kid => Ring(kid, room) ?? Block(kid)).ToList();
        var cuts = PlanCut(room, w, h);
        var corner = cuts.ToDictionary(cut => cut.Corner, cut => (cut.W, cut.H));
        var low = new Dictionary<ExitSide, int>
        {
            [ExitSide.Left] = corner.GetValueOrDefault(0).H,
            [ExitSide.Right] = corner.GetValueOrDefault(1).H,
            [ExitSide.Far] = corner.GetValueOrDefault(3).W
        };

        var high = new Dictionary<ExitSide, int>
        {
            [ExitSide.Left] = corner.GetValueOrDefault(3).H,
            [ExitSide.Right] = corner.GetValueOrDefault(2).H,
            [ExitSide.Far] = corner.GetValueOrDefault(2).W
        };

        var reserve = exitSides.Values
            .GroupBy(side => side)
            .ToDictionary(group => group.Key, group => group.Count() * _exitWidth + (group.Count() - 1) * MinStep);

        var openSides = new[] { ExitSide.Left, ExitSide.Right, ExitSide.Far }
            .Where(side => !(side == ExitSide.Far && reserve.ContainsKey(ExitSide.Far)))
            .ToArray();

        var load = new Dictionary<ExitSide, int> { [ExitSide.Left] = 0, [ExitSide.Right] = 0, [ExitSide.Far] = 0 };
        var sideOf = new List<(List<Box> Boxes, int Span, ExitSide Side, int Offset)>();
        float largest = GetLargestArea(room);
        int drawnW = w;
        int drawnH = h;
        foreach ((var boxes, int span) in blocks.OrderByDescending(block => block.Span))
        {
            int rowGap = Math.Max(0, (int)Math.Round(_model.RowGaps.Count > 0 ? _model.RowGaps[_rng.Next(_model.RowGaps.Count)] : 0));
            var sides = openSides
                .Where(candidate => MeasureGrownArea(candidate, load[candidate] + (load[candidate] > 0 ? rowGap : 0) + span) <= Math.Max(largest, drawnW * drawnH))
                .Select(candidate => (Side: candidate, Score: (load[candidate] + span) / (float)Math.Max(1, (candidate == ExitSide.Far ? drawnW : drawnH) - reserve.GetValueOrDefault(candidate) - low[candidate] - high[candidate])
                    + SideJitter * _rng.NextSingle()))
                .ToArray();

            if (sides.Length == 0)
            {
                _rings.Remove(boxes[0].Room);
                continue;
            }

            var side = sides.MinBy(candidate => candidate.Score).Side;
            int gap = load[side] > 0 ? rowGap : 0;
            sideOf.Add((boxes, span, side, low[side] + load[side] + gap));
            load[side] += gap + span;
        }

        (w, h) = MeasureGrown(load);
        _planned[room] = (cuts, w, h);
        _localReserves = [];
        var exitsOn = new Dictionary<ExitSide, int>();
        foreach ((int child, var side) in exitSides)
        {
            int shift = exitsOn.GetValueOrDefault(side) * (_exitWidth + MinStep);
            exitsOn[side] = exitsOn.GetValueOrDefault(side) + 1;
            if (side == ExitSide.Left)
            {
                int top = h - high[ExitSide.Left] - shift;
                _localReserves[child] = new Box(-ExitDepth, Math.Max(0, top - _exitWidth), 0, top, BoxKind.Reserve, child);
            }
            else if (side == ExitSide.Right)
            {
                int top = h - high[ExitSide.Right] - shift;
                _localReserves[child] = new Box(w, Math.Max(0, top - _exitWidth), w + ExitDepth, top, BoxKind.Reserve, child);
            }
            else
            {
                int left = low[ExitSide.Far] + shift;
                _localReserves[child] = new Box(left, h, Math.Min(w - high[ExitSide.Far], left + _exitWidth), h + ExitDepth, BoxKind.Reserve, child);
            }
        }

        _entrySpan = (corner.GetValueOrDefault(0).W, w - corner.GetValueOrDefault(1).W);
        var output = new List<Box> { new(0, 0, w, h, BoxKind.Room, room) };
        var sideNecks = new Dictionary<ExitSide, List<(int Low, int High, int Depth)>>();
        foreach ((var boxes, int _, var side, int offset) in sideOf)
        {
            var root = boxes[0];
            var neck = _rings.Contains(root.Room) ? null : DrawNeck(root.Room, room);
            int depth = 0;
            (int Low, int High) hostRange = side switch
            {
                ExitSide.Right => (high[side] - offset, h - low[side] - offset),
                ExitSide.Far => (low[side] - offset, w - high[side] - offset),
                _ => (low[side] - offset, h - high[side] - offset)
            };

            var taken = sideNecks.TryGetValue(side, out var spans) ? spans.Select(span => (span.Low - offset, span.High - offset, span.Depth)).ToArray() : [];
            var placed = neck is null ? null : PlaceNeck(Math.Max(root.X0, hostRange.Low), Math.Min(root.X1, hostRange.High), neck.Value, room, hostRange, root.Room, (root.X0, root.X1),
                taken);
            if (neck is not null && placed is not null)
            {
                (int from, int neckWidth, int owner) = placed.Value;
                if (owner == room)
                {
                    if (!sideNecks.TryGetValue(side, out var list))
                    {
                        list = [];
                        sideNecks[side] = list;
                    }

                    list.Add((offset + from, offset + from + neckWidth, neck.Value.Depth));
                }

                depth = neck.Value.Depth;
                output.Add(side switch
                {
                    ExitSide.Far => new Box(offset + from, h, offset + from + neckWidth, h + depth, BoxKind.Neck, owner),
                    ExitSide.Left => new Box(-depth, offset + from, 0, offset + from + neckWidth, BoxKind.Neck, owner),
                    _ => new Box(w, h - offset - from - neckWidth, w + depth, h - offset - from, BoxKind.Neck, owner)
                });
                _counts.NecksBuilt++;
            }

            foreach (var box in boxes)
            {
                output.Add(side switch
                {
                    ExitSide.Far => box.ToGlobal(Heading.North, offset, h + depth),
                    ExitSide.Left => box.ToGlobal(Heading.West, -depth, offset),
                    _ => box.ToGlobal(Heading.East, w + depth, h - offset)
                });
            }
        }

        return (output, exitSides);

        (int W, int H) MeasureGrown(Dictionary<ExitSide, int> loads) =>
            (Math.Max(drawnW, Math.Max(loads[ExitSide.Far] + low[ExitSide.Far] + high[ExitSide.Far] + reserve.GetValueOrDefault(ExitSide.Far), _doorMin)),
                Math.Max(Math.Max(drawnH, _doorMin), Math.Max(loads[ExitSide.Left] + low[ExitSide.Left] + high[ExitSide.Left] + reserve.GetValueOrDefault(ExitSide.Left),
                    loads[ExitSide.Right] + low[ExitSide.Right] + high[ExitSide.Right] + reserve.GetValueOrDefault(ExitSide.Right))));

        float MeasureGrownArea(ExitSide side, int sideLoad)
        {
            var loads = new Dictionary<ExitSide, int>(load) { [side] = sideLoad };
            (int grownW, int grownH) = MeasureGrown(loads);
            return grownW * (float)grownH;
        }
    }

    // Return the largest area the fitting levels show for the room's role at the level's scale
    private float GetLargestArea(int room)
    {
        if (_halls.Contains(room) && _model.Halls.Count > 0)
        {
            return _shrink * _model.Halls.Max(hall => hall.Width * hall.Length);
        }

        return _shrink * Math.Max(MinArea, _levelScale * GetRoleSizes(room).Max());
    }

    // Draw a path room's outline cut and return its corners with their sizes in the entry frame
    private List<(int Corner, int W, int H)> PlanCut(int room, int w, int h)
    {
        if (_connectors.Contains(room))
        {
            return [];
        }

        var fit = _model.CutShares[AreaThemeModel.GetSizeClass(Math.Min(w, h))];
        if (_rng.NextSingle() >= fit.Share)
        {
            return [];
        }

        var names = fit.Classes.Where(pair => _model.CutSizes.ContainsKey(pair.Key) && GetCutOptions(GetCornerCount(pair.Key), w, h).Length > 0).ToArray();
        if (names.Length == 0)
        {
            _counts.CutsDropped++;
            return [];
        }

        var name = DrawWeighted(names);
        int corners = GetCornerCount(name);
        _counts.CutsPlanned++;
        double share = Math.Min(MaxCutShare, _model.CutSizes[name].Draw(_rng) / 100) / corners;
        var options = GetCutOptions(corners, w, h);
        (int[] picks, int spareW, int spareH) = options[_rng.Next(options.Length)];

        var cuts = new List<(int Corner, int W, int H)>();
        foreach (int pick in picks)
        {
            int cw = Math.Max(_minCut, Math.Min(spareW, (int)Math.Round(w * Math.Sqrt(share) * (0.6 + 0.8 * _rng.NextDouble()))));
            int ch = Math.Max(_minCut, Math.Min(spareH, (int)Math.Round(share * w * h / cw)));
            cuts.Add((pick, cw, ch));
        }

        return cuts;
    }

    // List the corner sets of a cut that leave a door's width of wall beside them, with the spare length on each side
    private (int[] Picks, int SpareW, int SpareH)[] GetCutOptions(int corners, int w, int h)
    {
        int[][] options = corners switch
        {
            1 => Enumerable.Range(0, 4).Select(corner => new[] { corner }).ToArray(),
            2 => Enumerable.Range(0, 4).Select(corner => new[] { corner, (corner + 1) % 4 }).ToArray(),
            _ => [[0, 1, 2, 3]]
        };

        return options
            .Select(picks => (Picks: picks, SpareW: w - _doorMin - (corners == 4 || (corners == 2 && picks[0] is 0 or 2) ? _minCut : 0),
                SpareH: h - _doorMin - (corners == 4 || (corners == 2 && picks[0] is 1 or 3) ? _minCut : 0)))
            .Where(option => option.SpareW >= _minCut && option.SpareH >= _minCut)
            .ToArray();
    }

    public static int GetCornerCount(OutlineClass name) => name switch
    {
        OutlineClass.T => 2,
        OutlineClass.Cross => 4,
        _ => 1
    };

    private OutlineClass DrawWeighted(KeyValuePair<OutlineClass, float>[] weights)
    {
        float draw = _rng.NextSingle() * weights.Sum(pair => pair.Value);
        foreach (var pair in weights)
        {
            draw -= pair.Value;
            if (draw < 0)
            {
                return pair.Key;
            }
        }

        return weights[^1].Key;
    }

    // Turn planned corner cuts to the heading the room was placed with, as corners of the placed box
    private static List<(int Corner, int W, int H)> GetGlobalCuts(List<(int Corner, int W, int H)> planned, int w, int h, Heading heading)
    {
        var origin = new Box(0, 0, w, h, BoxKind.Room, 0).ToGlobal(heading, 0, 0);
        var cuts = new List<(int Corner, int W, int H)>();
        foreach ((int corner, int cw, int ch) in planned)
        {
            (int lx, int ly) = corner switch
            {
                0 => (0, 0),
                1 => (w, 0),
                2 => (w, h),
                _ => (0, h)
            };
            var point = new Box(lx, ly, lx, ly, BoxKind.Room, 0).ToGlobal(heading, 0, 0);
            bool lowX = point.X0 == origin.X0;
            bool lowY = point.Y0 == origin.Y0;
            int index = lowX && lowY ? 0 : lowY ? 1 : !lowX ? 2 : 3;
            cuts.Add(heading is Heading.East or Heading.West ? (index, ch, cw) : (index, cw, ch));
        }

        return cuts;
    }

    private List<Box> GetCutBoxes(int room)
    {
        if (!_planned.TryGetValue(room, out var plan) || !_headingOf.TryGetValue(room, out var heading) || !_boxOf.TryGetValue(room, out var box))
        {
            return [];
        }

        var boxes = new List<Box>();
        foreach ((int corner, int cw, int ch) in GetGlobalCuts(plan.Cuts, plan.W, plan.H, heading))
        {
            int cu = corner is 0 or 3 ? box.X0 : box.X1 - cw;
            int cv = corner is 0 or 1 ? box.Y0 : box.Y1 - ch;
            boxes.Add(new Box(cu, cv, cu + cw, cv + ch, BoxKind.Room, 0));
        }

        return boxes;
    }

    // Return whether the entry wall left after the room's own cut meets the host's wall left after its cut over a door's width
    private bool IsUsable(List<Box> local, Heading heading, int ox, int oy, Box host)
    {
        (int lo, int hi) = _entrySpan ?? (0, local[0].Width);
        var row = new Box(lo, 0, hi, 1, BoxKind.Room, 0).ToGlobal(heading, ox, oy);
        int met = row.GetContact(host);
        foreach (var cut in GetCutBoxes(host.Room))
        {
            met -= Math.Max(0, row.GetContact(cut));
        }

        return met >= _doorMin;
    }

    // Find a heading and origin putting the composite's first room against the host, clear of everything placed, through a neck or a
    // short corridor when it has to
    private (Heading Heading, int X, int Y, Box? Corridor)? Attach(List<Box> local, Box host, Heading? wanted, int room, (int Width, int Depth, int Owner)? neck)
    {
        var headings = (wanted is null ? [] : new[] { wanted.Value }).Concat(Enum.GetValues<Heading>().Where(heading => heading != wanted)).ToArray();
        int roomWidth = local[0].Width;
        int[] lengths = (neck is null ? [] : new[] { neck.Value.Depth }).Concat(CorridorLengths).ToArray();
        bool linked = local.Any(box => box.Kind == BoxKind.Room && _partners.ContainsKey(box.Room));
        for (int index = 0; index < lengths.Length; index++)
        {
            int length = lengths[index];
            bool necked = neck is not null && index == 0;
            var candidates = new List<(int Score, Heading Heading, int X, int Y)>();
            foreach (var heading in headings)
            {
                var spots = GetSpots(host, heading, length, roomWidth);
                Shuffle(spots);
                var bounds = _packer.GetBounds();
                if (bounds is not null && length == 0 && _theme == LevelTheme.Lab)
                {
                    (int bx0, int by0, int bx1, int by1) = bounds.Value;
                    var keyed = spots
                        .Select(spot => (Spot: spot, Growth: MeasureGrowth(local, heading, spot, bx0, by0, bx1, by1)))
                        .OrderBy(entry => entry.Growth)
                        .Select(entry => entry.Spot)
                        .ToList();

                    spots = keyed;
                }

                if (length == 0 && linked)
                {
                    (int Met, int X, int Y)? best = null;
                    foreach ((int ox, int oy) in spots)
                    {
                        if (!IsUsable(local, heading, ox, oy, host))
                        {
                            continue;
                        }

                        var moved = Move(local, heading, ox, oy);
                        if (_packer.IsClear(moved, room))
                        {
                            int met = moved
                                .Where(box => box.Kind == BoxKind.Room)
                                .Sum(box => (_partners.GetValueOrDefault(box.Room) ?? []).Count(partner => _boxOf.TryGetValue(partner, out var other) && box.GetContact(other) >= _doorMin));

                            if (best is null || met > best.Value.Met)
                            {
                                best = (met, ox, oy);
                            }
                        }
                    }

                    if (best is not null)
                    {
                        candidates.Add((best.Value.Met + (heading == wanted ? PathMatchBonus : 0), heading, best.Value.X, best.Value.Y));
                    }

                    continue;
                }

                foreach ((int ox, int oy) in spots)
                {
                    if (length == 0 && !IsUsable(local, heading, ox, oy, host))
                    {
                        continue;
                    }

                    var moved = Move(local, heading, ox, oy);
                    Box? corridor = null;
                    if (length > 0)
                    {
                        corridor = MakeCorridor(moved[0], host, heading);
                        if (corridor is not null && necked)
                        {
                            corridor = NarrowToNeck(corridor.Value, local, heading, ox, oy, host, room, neck!.Value);
                        }

                        if (corridor is null)
                        {
                            continue;
                        }

                        moved.Add(corridor.Value);
                    }

                    if (_packer.IsClear(moved, room))
                    {
                        return (heading, ox, oy, corridor);
                    }
                }
            }

            if (candidates.Count > 0)
            {
                var chosen = candidates.MaxBy(candidate => candidate.Score);
                return (chosen.Heading, chosen.X, chosen.Y, null);
            }
        }

        return null;
    }

    private static Box? MakeCorridor(Box first, Box host, Heading heading)
    {
        Box corridor;
        if (heading is Heading.North or Heading.South)
        {
            int lo = Math.Max(first.X0, host.X0);
            int hi = Math.Min(first.X1, host.X1);
            int a = heading == Heading.South ? Math.Min(first.Y1, host.Y1) : host.Y1;
            int b = heading == Heading.North ? first.Y0 : host.Y0;
            corridor = new Box(lo, Math.Min(a, b), hi, Math.Max(a, b), BoxKind.Corridor, 0);
        }
        else
        {
            int lo = Math.Max(first.Y0, host.Y0);
            int hi = Math.Min(first.Y1, host.Y1);
            (int a, int b) = heading == Heading.East ? (host.X1, first.X0) : (first.X1, host.X0);
            corridor = new Box(Math.Min(a, b), lo, Math.Max(a, b), hi, BoxKind.Corridor, 0);
        }

        return corridor.Width < 1 || corridor.Height < 1 ? null : corridor;
    }

    // Narrow a gap's corridor to a neck between the uncut walls of the entered room and its host, onto the entered room's cut corner only when the
    // uncut wall has no spot
    private Box? NarrowToNeck(Box corridor, List<Box> local, Heading heading, int ox, int oy, Box host, int room, (int Width, int Depth, int Owner) neck)
    {
        bool alongX = heading is Heading.North or Heading.South;
        (int lo, int hi) = _entrySpan ?? (0, local[0].Width);
        var uncut = new Box(lo, 0, hi, 1, BoxKind.Room, 0).ToGlobal(heading, ox, oy);
        var whole = new Box(0, 0, local[0].Width, 1, BoxKind.Room, 0).ToGlobal(heading, ox, oy);
        (int hostLow, int hostHigh) = alongX ? (host.X0, host.X1) : (host.Y0, host.Y1);
        int wall = heading switch
        {
            Heading.North => host.Y1,
            Heading.South => host.Y0,
            Heading.East => host.X1,
            _ => host.X0
        };

        foreach (var cut in GetCutBoxes(host.Room))
        {
            bool onWall = alongX ? cut.Y0 == wall || cut.Y1 == wall : cut.X0 == wall || cut.X1 == wall;
            (int cutLow, int cutHigh) = alongX ? (cut.X0, cut.X1) : (cut.Y0, cut.Y1);
            if (onWall && cutLow == hostLow)
            {
                hostLow = cutHigh;
            }
            else if (onWall && cutHigh == hostHigh)
            {
                hostHigh = cutLow;
            }
        }

        (int spanLow, int spanHigh) = alongX ? (corridor.X0, corridor.X1) : (corridor.Y0, corridor.Y1);
        var hostNecks = _packer.Placed
            .Where(box => box.Kind == BoxKind.Neck && box.Room == host.Room)
            .Where(box => alongX ? (box.Y0 == wall || box.Y1 == wall) && box.X1 > host.X0 && box.X0 < host.X1 : (box.X0 == wall || box.X1 == wall) && box.Y1 > host.Y0 && box.Y0 < host.Y1)
            .Select(box => alongX ? (Low: box.X0, High: box.X1, Depth: box.Y1 - box.Y0) : (Low: box.Y0, High: box.Y1, Depth: box.X1 - box.X0))
            .ToArray();

        (int Start, int Width, int Owner)? placed = null;
        foreach (var entry in new[] { uncut, whole })
        {
            (int enteredLow, int enteredHigh) = alongX ? (entry.X0, entry.X1) : (entry.Y0, entry.Y1);
            placed ??= PlaceNeck(Math.Max(spanLow, Math.Max(enteredLow, hostLow)), Math.Min(spanHigh, Math.Min(enteredHigh, hostHigh)), neck, host.Room, (hostLow, hostHigh),
                room, (enteredLow, enteredHigh), hostNecks);
        }

        if (placed is null)
        {
            return null;
        }

        (int start, int width, int owner) = placed.Value;
        return alongX
            ? corridor with { X0 = start, X1 = start + width, Kind = BoxKind.Neck, Room = owner }
            : corridor with { Y0 = start, Y1 = start + width, Kind = BoxKind.Neck, Room = owner };
    }

    // Pick where a neck starts between the bounds so it stands flush with or at least two units from both ends of its owner's wall and from the
    // first room's other necks on that wall, giving it to the other room when its drawn owner has no such spot
    private (int Start, int Width, int Owner)? PlaceNeck(int low, int high, (int Width, int Depth, int Owner) neck, int first, (int Low, int High) firstWall, int second,
        (int Low, int High) secondWall, IReadOnlyList<(int Low, int High, int Depth)> firstNecks)
    {
        var owners = neck.Owner == first ? new[] { (Room: first, Wall: firstWall), (Room: second, Wall: secondWall) } : [(second, secondWall), (first, firstWall)];
        foreach ((int owner, var wall) in owners)
        {
            if (_halls.Contains(owner))
            {
                continue;
            }

            for (int narrowed = Math.Min(neck.Width, high - low); narrowed >= _doorMin; narrowed--)
            {
                int size = narrowed;
                int[] starts = Enumerable.Range(low, high - low - size + 1)
                    .Where(start => start - wall.Low != 1 && wall.High - start - size != 1)
                    .Where(start => owner != first || firstNecks.All(other => IsNeckClear(start, start + size, neck.Depth, other)))
                    .ToArray();

                if (starts.Length > 0)
                {
                    return (starts[_rng.Next(starts.Length)], size, owner);
                }
            }
        }

        return null;
    }

    // Keep two necks of one wall apart by two units or more, or flush with depths equal or two units apart or more
    private static bool IsNeckClear(int low, int high, int depth, (int Low, int High, int Depth) other)
    {
        int gap = Math.Max(low - other.High, other.Low - high);
        return gap >= MinStep || (gap == 0 && Math.Abs(depth - other.Depth) != 1) || gap < 0;
    }

    private List<(int X, int Y)> GetSpots(Box host, Heading heading, int length, int roomWidth)
    {
        var spots = new List<(int X, int Y)>();
        switch (heading)
        {
            case Heading.North:
                for (int x = host.X0 - roomWidth + _doorMin; x <= host.X1 - _doorMin; x++)
                {
                    spots.Add((x, host.Y1 + length));
                }

                break;
            case Heading.South:
                for (int x = host.X0 + _doorMin; x <= host.X1 + roomWidth - _doorMin; x++)
                {
                    spots.Add((x, host.Y0 - length));
                }

                break;
            case Heading.East:
                for (int y = host.Y0 + _doorMin; y <= host.Y1 + roomWidth - _doorMin; y++)
                {
                    spots.Add((host.X1 + length, y));
                }

                break;
            default:
                for (int y = host.Y0 - roomWidth + _doorMin; y <= host.Y1 - _doorMin; y++)
                {
                    spots.Add((host.X0 - length, y));
                }

                break;
        }

        return spots;
    }

    private static long MeasureGrowth(List<Box> local, Heading heading, (int X, int Y) spot, int bx0, int by0, int bx1, int by1)
    {
        foreach (var box in local)
        {
            var moved = box.ToGlobal(heading, spot.X, spot.Y);
            (bx0, by0, bx1, by1) = (Math.Min(bx0, moved.X0), Math.Min(by0, moved.Y0), Math.Max(bx1, moved.X1), Math.Max(by1, moved.Y1));
        }

        return (long)(bx1 - bx0) * (by1 - by0);
    }

    private List<Box> Move(List<Box> local, Heading heading, int ox, int oy)
    {
        var moved = local.Select(box => box.ToGlobal(heading, ox, oy)).ToList();
        moved.AddRange(_localReserves.Values.Select(box => box.ToGlobal(heading, ox, oy)));
        return moved;
    }

    private static Dictionary<int, int> MeasureSteps(HashSet<(int A, int B)> doors, int from)
    {
        var steps = new Dictionary<int, int> { [from] = 0 };
        var queue = new Queue<int>([from]);
        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            if (steps[room] >= MaxLoopSteps)
            {
                continue;
            }

            foreach ((int a, int b) in doors)
            {
                int next = a == room ? b : b == room ? a : int.MinValue;
                if (next != int.MinValue && steps.TryAdd(next, steps[room] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return steps;
    }

    private void Shuffle<T>(List<T> items)
    {
        for (int index = items.Count - 1; index > 0; index--)
        {
            int other = _rng.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }

    // Hang each branch room an extra entrance enters from its source, keeping every room of a critical path under its own path
    private void EnterBranchesFromSources(MissionGraph mission)
    {
        foreach (var link in mission.Links.Where(link => link.Kind == MissionLinkKind.Entrance && !_spine.Contains(link.To) && _parent.ContainsKey(link.To)))
        {
            if (IsBelow(link.From, link.To) || GetDescendants(link.To).Any(_spine.Contains))
            {
                continue;
            }

            _children[_parent[link.To]].Remove(link.To);
            _parent[link.To] = link.From;
            if (!_children.TryGetValue(link.From, out var list))
            {
                list = [];
                _children[link.From] = list;
            }

            list.Add(link.To);
        }
    }

    private bool IsBelow(int room, int ancestor)
    {
        for (int current = room; _parent.TryGetValue(current, out int parent); current = parent)
        {
            if (parent == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<int> GetDescendants(int room) => _children.GetValueOrDefault(room, []).SelectMany(child => GetDescendants(child).Prepend(child));

    // Build the drawn extra entrances where their rooms touch, and stand in a touching pair of the same two phases whose source lies on
    // or next to the earlier phase's critical path, the drawn rooms first, before bridging any pair with a corridor
    private void AddEntrances(MissionGraph mission, BlockResult result)
    {
        var drawn = mission.Links
            .Where(link => link.Kind == MissionLinkKind.Entrance && result.Cells.ContainsKey(link.From) && result.Cells.ContainsKey(link.To))
            .Select(link => (Source: link.From, Entered: link.To))
            .Distinct()
            .ToList();

        int onTree = drawn.Count(pair => result.Realized.Contains((pair.Source, pair.Entered, MissionLinkKind.Tree)));
        result.EntrancesDrawn = drawn.Count;
        result.EntrancesBuilt = onTree;
        drawn.RemoveAll(pair => _parent.GetValueOrDefault(pair.Entered, -1) == pair.Source);
        if (drawn.Count == 0)
        {
            return;
        }

        var doors = result.Realized.Select(link => Order(link.From, link.To)).ToHashSet();
        doors.UnionWith(_corridors.Select(entry => Order(entry.A, entry.B)));
        bool lockedStart = mission.Links.Any(link => link.IsLocked && (link.From == result.Start || link.To == result.Start));
        var sources = GetEntranceSources(mission, result, lockedStart);
        Shuffle(drawn);
        var chosen = new List<(int Source, int Entered)>();
        foreach ((int source, int entered) in drawn)
        {
            int sourcePhase = result.PhaseOf[source];
            int enteredPhase = result.PhaseOf[entered];

            var pairs = sources.GetValueOrDefault(sourcePhase, [])
                .SelectMany(from => result.Cells.Keys.Where(to => result.PhaseOf.GetValueOrDefault(to, -1) == enteredPhase).Select(to => (Source: from, Entered: to)))
                .Where(pair => !doors.Contains(Order(pair.Source, pair.Entered)) && chosen.All(other => Order(other.Source, other.Entered) != Order(pair.Source, pair.Entered)))
                .Select(pair => (Pair: pair, Rank: pair == (source, entered) ? 0 : pair.Source == source || pair.Entered == entered ? 1 : 2, Draw: _rng.NextDouble()))
                .OrderBy(entry => entry.Rank)
                .ThenBy(entry => entry.Draw)
                .Select(entry => entry.Pair)
                .ToList();

            var touching = pairs.FirstOrDefault(pair => CountContacts(result.Cells, pair.Source, pair.Entered) >= _doorMin, (Source: -1, Entered: -1));
            if (touching.Source >= 0)
            {
                chosen.Add(touching);
                continue;
            }

            foreach (var pair in pairs.Where(pair => pair == (source, entered) || pair.Source == source || pair.Entered == entered))
            {
                var corridor = Bridge(_boxOf[pair.Source], _boxOf[pair.Entered]);
                if (corridor is null)
                {
                    continue;
                }

                _packer.Add(corridor.Value);
                result.Corridors.Add((GetBoxCells(corridor.Value), pair.Source, pair.Entered));
                chosen.Add(pair);
                _counts.Corridors++;
                break;
            }
        }

        foreach ((int source, int entered) in chosen)
        {
            result.Realized.Add((source, entered, MissionLinkKind.Entrance));
        }

        result.EntrancesBuilt += chosen.Count;
        result.EntrancesMoved = chosen.Count(pair => !drawn.Contains(pair));
    }

    // List per phase the rooms on its critical path or joined to it by a tree link, the start only when the mission locks a door off it
    private static Dictionary<int, List<int>> GetEntranceSources(MissionGraph mission, BlockResult result, bool lockedStart)
    {
        var sources = new Dictionary<int, List<int>>();
        foreach (var phase in mission.Progression.Phases)
        {
            var path = phase.Path.ToHashSet();
            var near = mission.Links
                .Where(link => link.Kind == MissionLinkKind.Tree && (path.Contains(link.From) || path.Contains(link.To)))
                .SelectMany(link => new[] { link.From, link.To })
                .Concat(path)
                .Where(room => result.PhaseOf.GetValueOrDefault(room, -1) == phase.Index && result.Cells.ContainsKey(room) && (lockedStart || room != result.Start))
                .Distinct()
                .Order()
                .ToList();

            sources[phase.Index] = near;
        }

        return sources;
    }

    // Build the drawn loops where their rooms touch and stand in touching pairs of the same phase as far apart, nearest the drawn rooms
    // first, before bridging any pair with a corridor
    private void AddLoops(MissionGraph mission, BlockResult result)
    {
        var drawn = mission.Links
            .Where(link => link.Kind == MissionLinkKind.Loop && result.Cells.ContainsKey(link.From) && result.Cells.ContainsKey(link.To))
            .Select(link => Order(link.From, link.To))
            .Distinct()
            .ToList();

        result.LoopsDrawn = drawn.Count;
        if (drawn.Count == 0)
        {
            return;
        }

        var doors = result.Realized.Select(link => Order(link.From, link.To)).ToHashSet();
        doors.UnionWith(_corridors.Select(entry => Order(entry.A, entry.B)));
        int[] rooms = result.Cells.Keys.Order().ToArray();
        var ends = drawn.SelectMany(pair => new[] { pair.Item1, pair.Item2 }).ToHashSet();
        var near = new List<(int A, int B)>();
        var others = new List<(int A, int B)>();
        foreach (int room in rooms)
        {
            foreach ((int other, int steps) in MeasureSteps(doors, room))
            {
                var pair = (room, other);
                if (room < other && steps is >= MinLoopSteps and <= MaxLoopSteps && !drawn.Contains(pair)
                    && result.PhaseOf.GetValueOrDefault(room, -1) == result.PhaseOf.GetValueOrDefault(other, -2))
                {
                    (ends.Contains(room) || ends.Contains(other) ? near : others).Add(pair);
                }
            }
        }

        Shuffle(drawn);
        Shuffle(near);
        Shuffle(others);
        var chosen = new List<(int A, int B)>();
        foreach (bool bridged in new[] { false, true })
        {
            foreach (var candidates in new[] { drawn, near, others })
            {
                foreach ((int a, int b) in candidates)
                {
                    if (chosen.Count >= drawn.Count)
                    {
                        break;
                    }

                    if (doors.Contains((a, b)) || chosen.Contains((a, b)))
                    {
                        continue;
                    }

                    if (!bridged)
                    {
                        if (CountContacts(result.Cells, a, b) >= _doorMin)
                        {
                            chosen.Add((a, b));
                        }

                        continue;
                    }

                    var corridor = Bridge(_boxOf[a], _boxOf[b]);
                    if (corridor is null)
                    {
                        continue;
                    }

                    _packer.Add(corridor.Value);
                    result.Corridors.Add((GetBoxCells(corridor.Value), a, b));
                    chosen.Add((a, b));
                    _counts.Corridors++;
                }
            }
        }

        foreach ((int a, int b) in chosen)
        {
            result.Realized.Add((a, b, MissionLinkKind.Loop));
        }

        result.LoopsBuilt = chosen.Count;
        result.LoopsMoved = chosen.Count(pair => !drawn.Contains(pair));
        result.Rings = _rings.Count(root => result.Cells.ContainsKey(root));
    }

    private static (int, int) Order(int a, int b) => a < b ? (a, b) : (b, a);

    // Return a corridor joining two rooms across a free gap of at most the bridge reach
    private Box? Bridge(Box a, Box b)
    {
        foreach (var (p, q) in new[] { (a, b), (b, a) })
        {
            int lo = Math.Max(p.Y0, q.Y0);
            int hi = Math.Min(p.Y1, q.Y1);
            Box corridor;
            if (q.X0 - p.X1 > 0 && q.X0 - p.X1 <= BridgeReach && hi - lo >= _doorMin)
            {
                int middle = FloorDivide(lo + hi - _doorMin, 2);
                corridor = new Box(p.X1, middle, q.X0, middle + _doorMin, BoxKind.Corridor, 0);
            }
            else
            {
                lo = Math.Max(p.X0, q.X0);
                hi = Math.Min(p.X1, q.X1);
                if (!(q.Y0 - p.Y1 > 0 && q.Y0 - p.Y1 <= BridgeReach && hi - lo >= _doorMin))
                {
                    continue;
                }

                int middle = FloorDivide(lo + hi - _doorMin, 2);
                corridor = new Box(middle, p.Y1, middle + _doorMin, q.Y0, BoxKind.Corridor, 0);
            }

            if (_packer.IsClearOfBuilt(corridor))
            {
                return corridor;
            }
        }

        return null;
    }
}

internal sealed class BlockResult
{
    public required Dictionary<int, HashSet<Cell>> Cells { get; init; }

    public required List<(HashSet<Cell> Cells, int A, int B)> Corridors { get; init; }

    public required HashSet<Cell> Courts { get; init; }

    public required Dictionary<int, int> PhaseOf { get; init; }

    public required Dictionary<int, int> Parent { get; init; }

    public required HashSet<int> Spine { get; init; }

    public required HashSet<int> Halls { get; init; }

    public required HashSet<int> Connectors { get; init; }

    public required HashSet<int> Decided { get; init; }

    public required int Start { get; init; }

    public required int End { get; init; }

    public required int Failures { get; init; }

    public List<(int From, int To, MissionLinkKind Kind)> Realized { get; } = [];

    public Dictionary<int, List<(int Corner, int W, int H)>> Cuts { get; } = [];

    public int EntrancesDrawn { get; set; }

    public int EntrancesBuilt { get; set; }

    public int EntrancesMoved { get; set; }

    public int LoopsDrawn { get; set; }

    public int LoopsBuilt { get; set; }

    public int LoopsMoved { get; set; }

    public int Rings { get; set; }
}
