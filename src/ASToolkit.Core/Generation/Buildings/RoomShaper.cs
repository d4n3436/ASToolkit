using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

// Shapes a building's rooms and halls after the wings stand, rooms taking footprint outlines, pods interlocking and halls narrowing between
// their door bands
internal sealed class RoomShaper
{
    private const double PodShapeShare = 0.25;
    private const double PodInterlockShare = 0.5;
    private const double WingShapeShare = 0.85;
    private const int PodFootprintCells = 36;

    private readonly BuildingDraft _draft;

    public RoomShaper(BuildingDraft draft) => _draft = draft;

    public void ShapeRooms(IReadOnlyList<RoomFootprint> footprints)
    {
        if (footprints.Count == 0)
        {
            return;
        }

        var groupSizes = _draft.Grid.Rooms
            .Where(room => room.Kind == GridKind.Room)
            .GroupBy(room => room.Group)
            .ToDictionary(group => group.Key, group => group.Count());

        var small = footprints.Where(footprint => footprint.Cells.Count <= PodFootprintCells).ToArray();
        var rooms = _draft.Grid.Rooms.Where(room => room is { Kind: GridKind.Room, IsJoint: false } && !_draft.KeepsOutline.Contains(room.Id)).ToArray();
        var mates = rooms
            .Where(room => room.Type != RoomType.Passage && !_draft.WingPieces.Contains(room.Id))
            .GroupBy(room => room.Group)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var pod in mates.Values)
        {
            InterlockPod(pod, PodInterlockShare);
        }

        var across = rooms
            .Where(room => room.Type != RoomType.Passage && !_draft.WingPieces.Contains(room.Id))
            .ToArray();

        var acrossPairs = across
            .SelectMany(first => across.Where(second => second.Id > first.Id && second.Group != first.Group && second.Phase == first.Phase
                && second.Rect.SharesWall(first.Rect)).Select(second => (First: first, Second: second)))
            .ToArray();

        foreach (var (first, second) in _draft.Random.Shuffled(acrossPairs))
        {
            if (!_draft.Shaped.Contains(first.Id) && !_draft.Shaped.Contains(second.Id) && _draft.Random.Next() < PodInterlockShare)
            {
                TryInterlock(first, second);
            }
        }

        foreach (var room in rooms)
        {
            bool isPodRoom = groupSizes[room.Group] <= 5 && room.Type is not (RoomType.Depot or RoomType.ComputerRoom or RoomType.Passage);
            bool isPacked = mates.TryGetValue(room.Group, out var pod) && pod.Length >= 3 && SharesPodWall(room);
            if (_draft.Shaped.Contains(room.Id) || isPacked || (isPodRoom && SharesPodWall(room)))
            {
                continue;
            }

            if (_draft.WingPieces.Contains(room.Id))
            {
                if (!TryShape(room, footprints))
                {
                    TryNotch(room);
                }
            }
            else if (_draft.Random.Next() < (isPodRoom ? PodShapeShare : WingShapeShare))
            {
                TryShape(room, isPodRoom && small.Length > 0 ? small : footprints);
            }
        }

        // Keep a pod of four or more rooms from being all boxes by interlocking its rooms, else outlining a lone room, else notching one
        foreach (var pod in rooms.GroupBy(room => room.Group).Where(group => group.Count() >= 4))
        {
            if (pod.Any(room => _draft.Shaped.Contains(room.Id)) || !mates.TryGetValue(pod.Key, out var regular) || InterlockPod(regular, 1.0))
            {
                continue;
            }

            var apart = _draft.Random.Shuffled(regular.Where(room => !SharesPodWall(room)).ToArray());
            if (!apart.Any(room => TryShape(room, small.Length > 0 ? small : footprints) || TryNotch(room)) && !_draft.Random.Shuffled(regular).Any(TryNotch))
            {
                _draft.Count(BuildingTally.PodBoxes);
            }
        }

        return;

        bool SharesPodWall(GridRoom room) => mates.TryGetValue(room.Group, out var pod) && pod.Any(other => other.Id != room.Id && other.Rect.SharesWall(room.Rect));
    }

    private bool InterlockPod(GridRoom[] pod, double share)
    {
        var pairs = pod
            .SelectMany(first => pod.Where(second => second.Id > first.Id && second.Rect.SharesWall(first.Rect)).Select(second => (First: first, Second: second)))
            .ToArray();

        bool any = false;
        foreach (var (first, second) in _draft.Random.Shuffled(pairs))
        {
            if (_draft.Shaped.Contains(first.Id) || _draft.Shaped.Contains(second.Id) || _draft.Random.Next() >= share)
            {
                continue;
            }

            bool isFirstPushing = _draft.Random.Next() < 0.5;
            any |= isFirstPushing ? TryInterlock(first, second) || TryInterlock(second, first) : TryInterlock(second, first) || TryInterlock(first, second);
        }

        return any;
    }

    // Pushes a tab two or three units deep from one room into a neighbor, keeping every shared wall a door wide, the tab clear of any third room
    // and no outline stepping by one unit
    private bool TryInterlock(GridRoom pusher, GridRoom yielder)
    {
        var a = pusher.Rect;
        var b = yielder.Rect;
        var side = b.V == a.V + a.H ? Side.North : b.V + b.H == a.V ? Side.South : b.U == a.U + a.W ? Side.West : Side.East;
        (int low, int high) = (Math.Max(a.GetSpan(side).Low, b.GetSpan(side).Low), Math.Min(a.GetSpan(side).High, b.GetSpan(side).High));
        int across = side.RunsAlongU ? b.H : b.W;
        var tabs = Enumerable.Range(3, Math.Max(0, Math.Min(6, high - low) - 2))
            .SelectMany(length => Enumerable.Range(2, 2).Where(depth => across - depth >= 3).Select(depth => (Length: length, Depth: depth)))
            .SelectMany(size => Enumerable.Range(low, high - size.Length - low + 1).Select(at => (size.Length, size.Depth, At: at)))
            .ToArray();

        foreach ((int length, int depth, int at) in _draft.Random.Shuffled(tabs))
        {
            var tab = b.Inside(side, length, depth, at);

            bool[,] mask = new bool[b.W, b.H];
            for (int x = 0; x < b.W; x++)
            {
                for (int y = 0; y < b.H; y++)
                {
                    mask[x, y] = !tab.Contains(new Cell(b.U + x, b.V + y));
                }
            }

            bool touchesOther = tab.Units
                .SelectMany(near => near.GetAdjacent())
                .Where(near => !tab.Contains(near))
                .Select(near => _draft.Grid.GetOwner(near))
                .Any(owner => owner is not null && (_draft.Grid[owner.Value].Owner ?? owner.Value) != pusher.Id && (_draft.Grid[owner.Value].Owner ?? owner.Value) != yielder.Id);

            var grown = a.Units
                .Concat(tab.Units)
                .ToArray();

            if (touchesOther || HasOneUnitFeature(mask) || HasOneUnitFeature(MaskOf(grown, out _)) || !KeepsWalls(yielder, mask, pusher.Id))
            {
                continue;
            }

            Apply(yielder, mask);
            _draft.AddArm(pusher.Id, tab, tab.W >= tab.H);
            _draft.Shaped.Add(pusher.Id);
            return true;
        }

        return false;
    }

    private bool TryNotch(GridRoom room)
    {
        var rect = room.Rect;
        if (rect.W < 4 || rect.H < 4)
        {
            return false;
        }

        int w = Math.Max(2, rect.W / 3);
        int h = Math.Max(2, rect.H / 3);
        (bool IsLeft, bool IsTop)[] corners = [(true, true), (true, false), (false, true), (false, false)];
        foreach ((bool isLeft, bool isTop) in _draft.Random.Shuffled(corners))
        {
            bool[,] mask = new bool[rect.W, rect.H];
            for (int x = 0; x < rect.W; x++)
            {
                for (int y = 0; y < rect.H; y++)
                {
                    mask[x, y] = !((isLeft ? x < w : x >= rect.W - w) && (isTop ? y < h : y >= rect.H - h));
                }
            }

            if (KeepsWalls(room, mask))
            {
                Apply(room, mask);
                return true;
            }
        }

        return false;
    }

    private bool TryShape(GridRoom room, IReadOnlyList<RoomFootprint> pool)
    {
        var rect = room.Rect;
        if (rect.W < 4 || rect.H < 4)
        {
            return false;
        }

        bool[,]? mask = DrawOutline(pool, rect.W, rect.H, mask => KeepsWalls(room, mask));
        if (mask is null)
        {
            return false;
        }

        Apply(room, mask);
        return true;
    }

    // Returns whether a mask keeps a door-wide stretch of every wall the room shares, except with one room
    private bool KeepsWalls(GridRoom room, bool[,] mask, int? except = null)
    {
        var rect = room.Rect;
        var shared = new List<(Side Side, int Owner, List<int> Along)>();
        foreach (var side in Enum.GetValues<Side>())
        {
            (int low, int high) = rect.GetSpan(side);
            var byOwner = new Dictionary<int, List<int>>();
            for (int along = low; along < high; along++)
            {
                var outside = rect.Beside(side, 1, 1, along);
                int? owner = _draft.Grid.GetOwner(new Cell(outside.U, outside.V));
                if (owner is not null && _draft.Grid[owner.Value].Kind != GridKind.Court && (_draft.Grid[owner.Value].Owner ?? owner.Value) != except)
                {
                    byOwner.TryAdd(owner.Value, []);
                    byOwner[owner.Value].Add(along - low);
                }
            }

            shared.AddRange(byOwner.Select(pair => (side, pair.Key, pair.Value)));
        }

        foreach (var (side, _, along) in shared)
        {
            int kept = 0;
            int run = 0;
            foreach (int index in along)
            {
                bool isKept = side switch
                {
                    Side.North => mask[index, 0],
                    Side.South => mask[index, rect.H - 1],
                    Side.West => mask[0, index],
                    _ => mask[rect.W - 1, index]
                };

                run = isKept ? run + 1 : 0;
                kept = Math.Max(kept, run);
            }

            if (kept < Math.Min(3, along.Count))
            {
                return false;
            }
        }

        for (int x = 0; x < rect.W; x++)
        {
            for (int y = 0; y < rect.H; y++)
            {
                if (except is null && !mask[x, y] && ((IsSolid(x - 1, y) && IsSolid(x + 1, y) && (IsOther(x - 1, y) || IsOther(x + 1, y)))
                    || (IsSolid(x, y - 1) && IsSolid(x, y + 1) && (IsOther(x, y - 1) || IsOther(x, y + 1)))))
                {
                    return false;
                }
            }
        }

        var parts = Decompose(mask, rect);
        foreach (int neighbor in shared.Select(pair => _draft.Grid[pair.Owner].Owner ?? pair.Owner).Distinct())
        {
            var theirs = _draft.Grid[neighbor].Kind == GridKind.Room ? _draft.GetShapes(neighbor) : [_draft.RectOf(neighbor)];
            if (!parts.Any(part => theirs.Any(other => part.SharesWall(other))))
            {
                return false;
            }
        }

        return true;

        bool IsOther(int x, int y) => (x < 0 || y < 0 || x >= rect.W || y >= rect.H) && IsSolid(x, y);

        // Counts a cell the outline gives up as solid where it would leave a one-unit slit beside another room; the cells an interlock gives up go
        // to the pushing room
        bool IsSolid(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < rect.W && y < rect.H)
            {
                return mask[x, y];
            }

            int? owner = _draft.Grid.GetOwner(new Cell(rect.U + x, rect.V + y));
            return owner is not null && _draft.Grid[owner.Value].Kind != GridKind.Court && (_draft.Grid[owner.Value].Owner ?? owner.Value) != room.Id;
        }
    }

    private void Apply(GridRoom room, bool[,] mask)
    {
        var parts = Decompose(mask, room.Rect);
        var main = parts.MaxBy(part => part.W * part.H);
        var extras = _draft.Shapes[room.Id].Skip(1).ToList();
        _draft.Grid.Shrink(room.Id, main);
        _draft.Shapes[room.Id] = extras.Prepend(main).ToList();
        foreach (var part in parts.Where(part => part != main))
        {
            _draft.AddArm(room.Id, part, part.W >= part.H);
        }

        _draft.Shaped.Add(room.Id);
    }

    private bool[,]? DrawOutline(IReadOnlyList<RoomFootprint> footprints, int width, int height, Func<bool[,], bool> isAcceptable)
    {
        double aspect = width / (double)height;
        var fitting = footprints.Where(footprint => 2 * Math.Max(footprint.Width, footprint.Height) <= Math.Max(width, height)).ToArray();
        var pool = fitting.Length > 0 ? fitting : footprints;
        for (int attempt = 0; attempt < 48; attempt++)
        {
            var footprint = _draft.Random.Choice(pool);
            int turn = _draft.Random.Range(0, 8);
            bool isTurned = turn % 2 == 1;
            (int fw, int fh) = isTurned ? (footprint.Height, footprint.Width) : (footprint.Width, footprint.Height);
            double ratio = fw / (double)fh / aspect;
            if (ratio is < 0.5 or > 2.0)
            {
                continue;
            }

            var cells = footprint.Cells.Select(cell => Turn(cell, footprint.Width, footprint.Height, turn)).ToHashSet();
            bool[,] mask = new bool[width, height];
            int covered = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    mask[x, y] = cells.Contains(new Cell(x * fw / width, y * fh / height));
                    covered += mask[x, y] ? 1 : 0;
                }
            }

            if (covered < width * height / 2 || covered == width * height)
            {
                continue;
            }

            if (HasOneUnitFeature(mask))
            {
                continue;
            }

            if (!isAcceptable(mask))
            {
                continue;
            }

            return mask;
        }

        return null;
    }

    private static bool HasOneUnitFeature(bool[,] mask)
    {
        int width = mask.GetLength(0);
        int height = mask.GetLength(1);
        for (int axis = 0; axis < 2; axis++)
        {
            int lines = axis == 0 ? height : width;
            int length = axis == 0 ? width : height;
            for (int line = 0; line < lines; line++)
            {
                int run = 0;
                bool? value = null;
                for (int index = 0; index <= length; index++)
                {
                    bool? cell = index < length ? axis == 0 ? mask[index, line] : mask[line, index] : null;
                    if (cell == value)
                    {
                        run++;
                        continue;
                    }

                    if (value is not null && run == 1)
                    {
                        return true;
                    }

                    value = cell;
                    run = 1;
                }
            }
        }

        return false;
    }

    // Turns a cell by one of eight symmetries, a quarter turn at each odd index and a mirror past the fourth
    private static Cell Turn(Cell cell, int width, int height, int turn)
    {
        (int x, int y) = turn >= 4 ? (width - 1 - cell.U, cell.V) : (cell.U, cell.V);
        return (turn % 4) switch
        {
            0 => new Cell(x, y),
            1 => new Cell(height - 1 - y, x),
            2 => new Cell(width - 1 - x, height - 1 - y),
            _ => new Cell(y, width - 1 - x)
        };
    }

    private static List<UnitRect> Decompose(bool[,] mask, UnitRect at)
    {
        var rects = new List<UnitRect>();
        var open = new Dictionary<(int From, int To), int>();
        for (int y = 0; y <= mask.GetLength(1); y++)
        {
            var runs = new HashSet<(int From, int To)>();
            for (int x = 0; y < mask.GetLength(1) && x < mask.GetLength(0); x++)
            {
                if (!mask[x, y])
                {
                    continue;
                }

                int from = x;
                while (x + 1 < mask.GetLength(0) && mask[x + 1, y])
                {
                    x++;
                }

                runs.Add((from, x + 1));
            }

            foreach (var run in open.Keys.Where(run => !runs.Contains(run)).ToArray())
            {
                rects.Add(new UnitRect(at.U + run.From, at.V + open[run], run.To - run.From, y - open[run]));
                open.Remove(run);
            }

            foreach (var run in runs.Where(run => !open.ContainsKey(run)))
            {
                open[run] = y;
            }
        }

        return rects;
    }

    private static bool[,] MaskOf(IReadOnlyCollection<Cell> cells, out UnitRect box)
    {
        int minU = cells.Min(cell => cell.U);
        int minV = cells.Min(cell => cell.V);
        box = new UnitRect(minU, minV, cells.Max(cell => cell.U) - minU + 1, cells.Max(cell => cell.V) - minV + 1);
        bool[,] mask = new bool[box.W, box.H];
        foreach (var cell in cells)
        {
            mask[cell.U - minU, cell.V - minV] = true;
        }

        return mask;
    }

    // Pulls a hall's free edges in by a unit between its door bands
    public void ShapeHalls()
    {
        // Keep an office hub straight between its pods
        foreach (var hall in _draft.Grid.Rooms.Where(room => room.Kind == GridKind.Hall && !_draft.OfficeWing.Contains(room.Id)).ToArray())
        {
            var rect = hall.Rect;
            bool alongU = hall.AlongU;
            int width = alongU ? rect.H : rect.W;
            if (width < 5)
            {
                continue;
            }

            (int low, int high) = alongU ? (rect.U, rect.U + rect.W) : (rect.V, rect.V + rect.H);
            var pulls = new List<(Side Side, int Low, int High)>();
            foreach (var side in alongU ? (Side[])[Side.North, Side.South] : [Side.West, Side.East])
            {
                int run = 0;
                for (int at = low; at <= high; at++)
                {
                    bool isFree = at < high;
                    for (int outward = 1; isFree && outward <= 2; outward++)
                    {
                        var beyond = side switch
                        {
                            Side.North => new Cell(at, rect.V - outward),
                            Side.South => new Cell(at, rect.V + rect.H - 1 + outward),
                            Side.West => new Cell(rect.U - outward, at),
                            _ => new Cell(rect.U + rect.W - 1 + outward, at)
                        };

                        isFree = _draft.Grid.GetOwner(beyond) is null;
                    }

                    if (isFree)
                    {
                        run++;
                        continue;
                    }

                    if (run >= 8)
                    {
                        pulls.Add((side, at - run + 2, at - 2));
                    }

                    run = 0;
                }
            }

            if (pulls.Count == 0)
            {
                continue;
            }

            bool pullsNear = pulls.Any(pull => pull.Side is Side.North or Side.West);
            bool pullsFar = pulls.Any(pull => pull.Side is Side.South or Side.East);
            var core = alongU
                ? new UnitRect(rect.U, rect.V + (pullsNear ? 1 : 0), rect.W, rect.H - (pullsNear ? 1 : 0) - (pullsFar ? 1 : 0))
                : new UnitRect(rect.U + (pullsNear ? 1 : 0), rect.V, rect.W - (pullsNear ? 1 : 0) - (pullsFar ? 1 : 0), rect.H);

            _draft.Grid.Shrink(hall.Id, core);
            _draft.Shapes[hall.Id] = [core];
            foreach (var side in alongU ? (Side[])[Side.North, Side.South] : [Side.West, Side.East])
            {
                if (side is Side.North or Side.West ? !pullsNear : !pullsFar)
                {
                    continue;
                }

                var cuts = pulls.Where(pull => pull.Side == side).OrderBy(pull => pull.Low).ToArray();
                int cursor = low;
                foreach ((_, int cutLow, int cutHigh) in cuts.Append((side, high, high)))
                {
                    if (cutLow > cursor)
                    {
                        var band = rect.Inside(side, cutLow - cursor, 1, cursor);

                        _draft.AddArm(hall.Id, band, alongU);
                    }

                    cursor = Math.Max(cursor, cutHigh);
                }
            }
        }
    }
}
