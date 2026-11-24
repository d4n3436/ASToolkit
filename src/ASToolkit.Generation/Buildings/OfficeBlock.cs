using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Rooms;

namespace ASToolkit.Generation.Buildings;

internal sealed class OfficeBlock
{
    private const int SmallMax = 70;
    private const int LargeMin = 120;
    private const int MaxRoom = 420;
    private const int DoorWall = 5;
    private const int StartMin = 200;
    private const int BreakerMin = 220;
    private const int SuiteMin = 280;
    private const int HostMin = 240;
    private const int Draws = 8;
    private const float MinOpenedShare = 0.5f;
    private const float MaxOpenedShare = 0.7f;

    private readonly GrammarRandom _random;
    private readonly RegionSplitter _splitter;

    public OfficeBlock(GrammarRandom random)
    {
        _random = random;
        _splitter = new RegionSplitter(random, LargeMin, MaxRoom, 6, 0.4f, 0);
    }

    public int Redraws { get; private set; }

    public OfficeLayout? Draw()
    {
        for (int draw = 0; draw < Draws; draw++)
        {
            if (TryDrawLayout(out var layout))
            {
                return layout;
            }

            Redraws++;
        }

        return null;
    }

    private bool TryDrawLayout([NotNullWhen(true)] out OfficeLayout? layout)
    {
        layout = null;
        int count = _random.Inclusive(15, 18);
        float area = count * _random.Uniform(220, 260);
        int width = (int)MathF.Round(MathF.Sqrt(area * _random.Uniform(1.0f, 1.4f)));
        var envelope = DrawEnvelope(width, (int)MathF.Round(area / width));
        var regions = _splitter.Partition(envelope, count, false).Regions;
        if (regions.Count == 0)
        {
            return false;
        }

        var adjacency = RegionSplitter.GetAdjacency(regions, DoorWall);
        float floor = regions.Sum(region => region.Count);
        int runLength = _random.Inclusive(4, 6) + 2;
        var runs = _splitter.FindPaths(adjacency, runLength, CanStartRun, CanHoldBreaker, -1).Where(LeavesOpenedPartJoined).ToList();
        layout = runs.Count == 0 ? null : BuildLayout(regions, adjacency, _random.Choice(runs));
        return layout is not null;

        bool CanStartRun(int room) => TouchesOutside(regions[room], envelope) && regions[room].Count >= StartMin;

        bool CanHoldBreaker(int room) => regions[room].Count >= BreakerMin;

        bool LeavesOpenedPartJoined(List<int> run)
        {
            var opened = Enumerable.Range(0, regions.Count).Except(run).ToHashSet();
            float share = opened.Sum(room => regions[room].Count) / floor;
            return share is >= MinOpenedShare and <= MaxOpenedShare && MeasureHops(opened, adjacency, opened.First()).Count == opened.Count;
        }
    }

    private HashSet<Cell> DrawEnvelope(int width, int height)
    {
        var cells = new UnitRect(0, 0, width, height).ToCells();
        foreach (var corner in _random.Shuffled((Cell[])[new Cell(0, 0), new Cell(0, 1), new Cell(1, 0), new Cell(1, 1)]).Take(_random.Choice([0, 1, 1, 2])))
        {
            int w = _random.Inclusive(width / 5, width * 2 / 5);
            int h = _random.Inclusive(height / 5, height * 2 / 5);
            cells.ExceptWith(new UnitRect(corner.U == 0 ? 0 : width - w, corner.V == 0 ? 0 : height - h, w, h).ToCells());
        }

        return cells;
    }

    private static bool TouchesOutside(HashSet<Cell> region, HashSet<Cell> envelope) => region.Any(cell => cell.GetAdjacent().Any(next => !envelope.Contains(next)));

    private static Dictionary<int, int> MeasureHops(IEnumerable<int> rooms, List<HashSet<int>> adjacency, int from)
    {
        var members = rooms.ToHashSet();
        return members.ToDictionary(room => room, room => adjacency[room].Where(members.Contains).ToList()).MeasureHops([from]);
    }

    private static bool IsHallShaped(UnitRect box) => Math.Min(box.W, box.H) <= 11 && Math.Max(box.W, box.H) >= 2.4f * Math.Min(box.W, box.H);

    private OfficeRow DrawOfficeRow(int offices)
    {
        int depth = _random.Inclusive(7, 8);
        return new OfficeRow(Enumerable.Range(0, offices).Select(_ => _random.Inclusive(5, SmallMax / depth)).ToArray(), depth);
    }

    private OfficeLayout? BuildLayout(List<HashSet<Cell>> regions, List<HashSet<int>> adjacency, List<int> path)
    {
        var rest = Enumerable.Range(0, regions.Count).Except(path).ToList();
        var openers = path.Skip(1).SkipLast(1).SelectMany(room => adjacency[room].Where(rest.Contains).Select(other => new RoomPair(room, other))).ToList();
        if (openers.Count == 0)
        {
            return null;
        }

        var opener = _random.Choice(openers);
        var hops = MeasureHops(rest, adjacency, path.Contains(opener.A) ? opener.B : opener.A);
        int suiteAt = rest.Where(room => regions[room].Count >= SuiteMin).OrderByDescending(hops.GetValueOrDefault).Cast<int?>().FirstOrDefault() ?? -1;
        var hosts = _random.Shuffled(rest.Where(room => room != suiteAt && regions[room].Count >= HostMin)).Take(_random.Inclusive(1, 2)).ToHashSet();
        int gateAt = _random.Next() < 0.7f ? path[_random.Inclusive(2, path.Count - 3)] : -1;
        var rows = new Dictionary<int, OfficeRow>
        {
            [path[0]] = DrawOfficeRow(2),
            [path[^1]] = DrawOfficeRow(_random.Inclusive(2, 3))
        };

        foreach (int host in hosts)
        {
            rows[host] = DrawOfficeRow(_random.Inclusive(2, 4));
        }

        if (suiteAt >= 0)
        {
            rows[suiteAt] = DrawOfficeRow(_random.Inclusive(3, 4));
        }

        var buildingCells = regions.SelectMany(region => region).ToHashSet();
        var rooms = new List<OfficeRoom>();
        int[] hostOf = new int[regions.Count];
        var rowOf = new List<int>[regions.Count];
        for (int i = 0; i < regions.Count; i++)
        {
            int at = path.IndexOf(i);
            var joined = new[] { at > 0 ? path[at - 1] : -1, at >= 0 && at + 1 < path.Count ? path[at + 1] : -1, opener.A == i ? opener.B : opener.B == i ? opener.A : -1 }
                .Where(room => room >= 0)
                .Select(room => regions[room])
                .ToArray();

            var (host, offices) = rows.TryGetValue(i, out var row) ? CutRow(regions[i], row.Widths, row.Depth, buildingCells, joined) : (regions[i], []);
            int phase = at == 0 ? 0 : at > 0 ? 1 : 2;
            var type = at == 0 || (at < 0 && IsHallShaped(regions[i].GetBounds()))
                ? RoomType.Hall
                : i == gateAt || i == suiteAt || hosts.Contains(i) ? RoomType.Office : _random.Choice((RoomType[])[RoomType.Office, RoomType.Office, RoomType.Lab, RoomType.Storage]);

            hostOf[i] = rooms.Count;
            rooms.Add(new OfficeRoom(host, type, phase));
            rowOf[i] = offices.Select(office =>
            {
                rooms.Add(new OfficeRoom(office, _random.Choice((RoomType[])[RoomType.Office, RoomType.Office, RoomType.Depot]), phase));
                return rooms.Count - 1;
            }).ToList();
        }

        var doors = new HashSet<RoomPair> { new(hostOf[opener.A], hostOf[opener.B]) };
        var run = path.Select(index => hostOf[index]).ToList();
        int breakerAt = path[^1];
        if (rowOf[breakerAt].Count == 0)
        {
            return null;
        }

        int breaker = rowOf[breakerAt].MaxBy(office => rooms[office].Cells.Count);
        foreach (int office in rowOf[breakerAt])
        {
            rooms[office] = rooms[office] with { Phase = office == breaker ? 1 : 2, Type = office == breaker ? RoomType.Office : rooms[office].Type };
            doors.Add(new RoomPair(run[^1], office));
        }

        run.Add(breaker);
        for (int i = 0; i + 1 < run.Count; i++)
        {
            doors.Add(new RoomPair(run[i], run[i + 1]));
        }

        foreach (int office in rowOf[path[0]])
        {
            doors.Add(new RoomPair(run[0], office));
        }

        var suite = suiteAt < 0 ? [] : rowOf[suiteAt].Prepend(hostOf[suiteAt]).ToList();
        for (int i = 0; i + 1 < suite.Count; i++)
        {
            doors.Add(new RoomPair(suite[i], suite[i + 1]));
        }

        var closed = suite.Skip(1).ToHashSet();
        for (int a = 0; a < rooms.Count; a++)
        {
            for (int b = a + 1; b < rooms.Count; b++)
            {
                if (rooms[a].Phase == 2 && rooms[b].Phase == 2 && !closed.Contains(a) && !closed.Contains(b) && rooms[a].Cells.GetLongestWall(rooms[b].Cells).Length >= DoorWall)
                {
                    doors.Add(new RoomPair(a, b));
                }
            }
        }

        int[] unreached = Enumerable.Range(0, rooms.Count).Except(Reach(doors, run[0])).ToArray();
        foreach (int room in unreached)
        {
            int? from = run.Skip(1).Where(step => rooms[step].Cells.GetLongestWall(rooms[room].Cells).Length >= DoorWall).Cast<int?>().FirstOrDefault();
            if (from is not null)
            {
                doors.Add(new RoomPair(from.Value, room));
            }
        }

        var reached = Reach(doors, run[0]);
        if (!unreached.All(reached.Contains))
        {
            return null;
        }

        CutSlits(rooms, doors);
        var closets = new List<(UnitRect Rect, int Host)>();
        foreach (int[] closetHosts in suite.Take(2).Select(room => new[] { room }).Append([run[^2], run[^3]]))
        {
            foreach (int host in closetHosts)
            {
                if (PlaceCloset(rooms, doors, host, closets))
                {
                    break;
                }
            }
        }

        var depths = Depths(doors, breaker);
        int end = Enumerable.Range(0, rooms.Count)
            .Where(room => rooms[room].Phase == 2 && rooms[room].Cells.Count >= LargeMin && depths.ContainsKey(room))
            .MaxBy(room => depths[room]);

        return new OfficeLayout(rooms, doors, run, closets, end);
    }

    private void CutSlits(List<OfficeRoom> rooms, HashSet<RoomPair> doors)
    {
        var walls = Enumerable.Range(0, rooms.Count)
            .SelectMany(a => Enumerable.Range(a + 1, rooms.Count - a - 1).Select(b => (a, b)))
            .Where(pair => rooms[pair.a].Cells.GetLongestWall(rooms[pair.b].Cells).Length >= 8)
            .ToArray();

        foreach ((int a, int b) in _random.Shuffled(walls).Take(Math.Max(3, walls.Length / 3)))
        {
            bool hasDoor = doors.Contains(new RoomPair(a, b));
            (int host, int other) = rooms[a].Cells.Count >= rooms[b].Cells.Count ? (a, b) : (b, a);
            var cells = rooms[host].Cells;
            var wall = cells.GetLongestWall(rooms[other].Cells);
            int middle = (wall.Low + wall.High) / 2;
            var step = wall.AlongU
                ? rooms[other].Cells.Contains(new Cell(wall.Low, wall.Line + 1)) ? new Cell(0, 1) : new Cell(0, -1)
                : rooms[other].Cells.Contains(new Cell(wall.Line + 1, wall.Low)) ? new Cell(1, 0) : new Cell(-1, 0);
            var slit = Enumerable.Range(wall.Low, wall.High - wall.Low)
                .Where(at => !hasDoor || at < middle - DoorWall / 2 || at >= middle - DoorWall / 2 + DoorWall)
                .Select(at => wall.AlongU ? new Cell(at, wall.Line) : new Cell(wall.Line, at))
                .ToArray();

            var rest = cells.Except(slit).ToHashSet();
            bool isSmall = cells.Count <= SmallMax;
            if (slit.Length == 0 || slit.Any(cell => !cells.Contains(new Cell(cell.U - step.U, cell.V - step.V))) || !rest.IsConnected() || !rest.IsThick(4)
                || rest.Count < (isSmall ? 28 : LargeMin)
                || doors.Where(door => door.A == host || door.B == host).Any(door => rest.GetLongestWall(rooms[door.A == host ? door.B : door.A].Cells).Length < DoorWall))
            {
                continue;
            }

            rooms[host] = rooms[host] with { Cells = rest };
        }
    }

    private bool PlaceCloset(List<OfficeRoom> rooms, HashSet<RoomPair> doors, int host, List<(UnitRect Rect, int Host)> closets)
    {
        var box = rooms[host].Cells.GetBounds();
        (int u0, int v0, int u1, int v1) = (box.U, box.V, box.U + box.W, box.V + box.H);
        var inside = _random.Shuffled((Point[])[new Point(u0, v0), new Point(u1 - 3, v0), new Point(u0, v1 - 3), new Point(u1 - 3, v1 - 3)]);
        var outside = Enumerable.Range(u0 - 3, u1 - u0 + 3).SelectMany(u => new[] { new Point(u, v0 - 3), new Point(u, v1) })
            .Concat(Enumerable.Range(v0 - 3, v1 - v0 + 3).SelectMany(v => new[] { new Point(u0 - 3, v), new Point(u1, v) }));

        var taken = rooms.SelectMany(room => room.Cells).Concat(closets.SelectMany(closet => closet.Rect.Units)).ToHashSet();
        int[] partners = doors.Where(door => door.A == host || door.B == host).Select(door => door.A == host ? door.B : door.A).ToArray();
        foreach (var corner in inside.Concat(_random.Shuffled(outside)))
        {
            var rect = new UnitRect(corner.X, corner.Y, 3, 3);
            var cells = rect.ToCells();
            if (cells.IsSubsetOf(rooms[host].Cells))
            {
                var rest = rooms[host].Cells.Except(cells).ToHashSet();
                if (!rest.IsConnected() || !rest.IsThick(4) || rest.Count < LargeMin || partners.Any(other => rest.GetLongestWall(rooms[other].Cells).Length < DoorWall))
                {
                    continue;
                }

                rooms[host] = rooms[host] with { Cells = rest };
            }
            else if (cells.Overlaps(taken) || cells.Sum(cell => cell.GetAdjacent().Count(rooms[host].Cells.Contains)) < 3)
            {
                continue;
            }

            closets.Add((rect, host));
            return true;
        }

        return false;
    }

    private (HashSet<Cell>, List<HashSet<Cell>>) CutRow(HashSet<Cell> block, int[] widths, int depth, HashSet<Cell> buildingCells, HashSet<Cell>[] joined)
    {
        int u0 = block.Min(cell => cell.U), v0 = block.Min(cell => cell.V), u1 = block.Max(cell => cell.U) + 1, v1 = block.Max(cell => cell.V) + 1;
        var bounds = new UnitRect(u0, v0, u1 - u0, v1 - v0);
        var sides = new (Side Side, bool Along, int Line)[] { (Side.North, true, v0 - 1), (Side.South, true, v1), (Side.West, false, u0 - 1), (Side.East, false, u1) }
            .OrderBy(side => (side.Along ? Enumerable.Range(u0, u1 - u0).Count(u => buildingCells.Contains(new Cell(u, side.Line)))
                : Enumerable.Range(v0, v1 - v0).Count(v => buildingCells.Contains(new Cell(side.Line, v)))) + _random.Next());

        foreach ((var side, bool along, int _) in sides)
        {
            int length = along ? u1 - u0 : v1 - v0, free = length - widths.Sum();
            if (free < 0)
            {
                continue;
            }

            int[] spots = free >= 10 ? [0, free, _random.Inclusive(4, free - 4)] : [0, free];
            foreach (int at in _random.Shuffled(spots))
            {
                var offices = new List<HashSet<Cell>>();
                int start = (along ? u0 : v0) + at;
                foreach (int width in widths)
                {
                    offices.Add(bounds.Inside(side, width, depth, start).ToCells());
                    start += width;
                }

                var host = block.Except(offices.SelectMany(office => office)).ToHashSet();
                if (offices.All(office => office.IsSubsetOf(block) && host.GetLongestWall(office).Length >= DoorWall) && host.Count >= LargeMin && host.IsConnected() && joined.All(room => host.GetLongestWall(room).Length >= DoorWall))
                {
                    return (host, offices);
                }
            }
        }

        return (block, []);
    }

    private static HashSet<int> Reach(HashSet<RoomPair> doors, int from) => Depths(doors, from).Keys.ToHashSet();

    private static Dictionary<int, int> Depths(HashSet<RoomPair> doors, int from) => doors
        .SelectMany(door => new[] { (door.A, door.B), (door.B, door.A) })
        .GroupBy(door => door.Item1, door => door.Item2)
        .ToDictionary(group => group.Key, group => group.ToList())
        .MeasureHops([from]);
}
