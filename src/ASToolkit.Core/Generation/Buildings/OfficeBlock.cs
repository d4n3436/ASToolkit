using System.Drawing;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

internal enum OfficePart
{
    Start,
    Run,
    Gate,
    Breaker,
    Hall,
    Big,
    Medium,
    Host,
    Suite
}

internal sealed record OfficeRoom(HashSet<Cell> Cells, RoomType Type, int Phase);

internal sealed record OfficeLayout(List<OfficeRoom> Rooms, HashSet<RoomPair> Doors, List<int> Run, List<(UnitRect Rect, int Host)> Closets, int End);

// Draws the office block as a level graph laid by the layout solver, the start hall wrapping its offices, a dark run of four to six rooms to the
// breaker's room and the rooms the breaker opens joined in a tree, every room touching two others inside a band of the screen
internal sealed class OfficeBlock
{
    private const int SmallMax = 70;
    private const int LargeMin = 120;
    private const int DoorWall = 5;
    private const float BandFill = 0.5f;
    private const int Draws = 8;
    private static readonly DoorRule Doors = new(DoorWall, 0);

    private readonly GrammarRandom _random;
    private readonly List<(OfficePart Part, RoomShape Shape, int[] Row, int Depth)> _parts = [];
    private readonly List<int> _run = [];
    private readonly List<int> _after = [];

    public OfficeBlock(GrammarRandom random) => _random = random;

    public int Redraws { get; private set; }

    public OfficeLayout? Draw()
    {
        for (int draw = 0; draw < Draws; draw++)
        {
            var problem = DrawGraph();
            var placed = new LayoutSolver<int>(problem, _random.Range(0, int.MaxValue)).Solve();
            var layout = placed.Count == 0 ? null : Finish(problem, placed);
            if (layout is not null)
            {
                return layout;
            }

            Redraws++;
        }

        return null;
    }

    private LayoutProblem<int> DrawGraph()
    {
        _parts.Clear();
        _run.Clear();
        _after.Clear();
        _run.Add(Add(OfficePart.Start, Block(2)));
        int count = _random.Inclusive(4, 6);
        int gate = _random.Next() < 0.7f ? _random.Inclusive(2, count - 1) : -1;
        for (int index = 1; index <= count; index++)
        {
            _run.Add(index == gate
                ? Add(OfficePart.Gate, RoomShape.Rectangle(_random.Inclusive(5, 9), _random.Inclusive(7, 8)))
                : Add(OfficePart.Run, _random.Next() < 0.25f ? Hall() : _random.Next() < 0.3f ? Ell(_random.Inclusive(120, 192)) : Box(_random.Inclusive(120, 208), 1.1f, 1.9f)));
        }

        _run.Add(Add(OfficePart.Breaker, Block(_random.Inclusive(2, 3))));
        for (int index = _random.Inclusive(2, 3); index > 0; index--)
        {
            _after.Add(Add(OfficePart.Hall, Hall()));
        }

        _after.Add(Add(OfficePart.Big, Box(_random.Inclusive(256, 416), 1.2f, 1.8f)));
        _after.Add(Add(OfficePart.Big, Box(_random.Inclusive(256, 416), 1.2f, 1.8f)));
        for (int index = _random.Inclusive(2, 3); index > 0; index--)
        {
            _after.Add(Add(OfficePart.Medium, _random.Next() < 0.5f ? Ell(_random.Inclusive(128, 192)) : Box(_random.Inclusive(120, 192), 1.1f, 1.9f)));
        }

        for (int index = _random.Inclusive(1, 2); index > 0; index--)
        {
            _after.Add(Add(OfficePart.Host, Block(_random.Inclusive(2, 4))));
        }

        _after.Add(Add(OfficePart.Suite, Block(_random.Inclusive(3, 4))));
        int area = _parts.Sum(part => part.Shape.Cells.Count);
        float down = MathF.Sqrt(2.0f * area / (BandFill * 0.98f));
        var problem = new LayoutProblem<int> { MinimumDistance = 2, MinContacts = 2, SavesPhases = true, Band = ((int)(0.98f * down), (int)down) };
        for (int index = 0; index < _parts.Count; index++)
        {
            problem.AddRoom(index, [_parts[index].Shape]);
            for (int other = index + 1; other < _parts.Count; other++)
            {
                problem.MayTouchPairs.Add((index, other));
            }
        }

        for (int index = 0; index + 1 < _run.Count; index++)
        {
            problem.Join(_run[index], _run[index + 1]);
        }

        int[] order = _random.Shuffled(_after);
        for (int index = 1; index < order.Length; index++)
        {
            problem.Join(order[index], order[_random.Range(0, index)]);
        }

        // Open one run room onto them; the doors on every wall they share make their loops
        problem.Join(_run[_random.Inclusive(1, count)], _random.Choice(order));

        Hint(problem);
        return problem;
    }

    // Places each room unturned flush against a joined room already placed, where it shares most wall and keeps the building compact on screen, as
    // the solver's starting point
    private void Hint(LayoutProblem<int> problem)
    {
        var boxes = new Dictionary<int, Rectangle> { [0] = Bound(_parts[0].Shape.Cells, 0, 0) };
        var next = problem.Joins.SelectMany(join => new[] { join, (join.B, join.A) }).ToLookup(join => join.Item1, join => join.Item2);
        var queue = new Queue<int>([0]);
        var order = new List<int>();
        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            order.Add(room);
            foreach (int other in next[room].Where(other => !order.Contains(other) && !queue.Contains(other)))
            {
                queue.Enqueue(other);
            }
        }

        foreach (int room in order.Skip(1))
        {
            int w = _parts[room].Shape.Width, h = _parts[room].Shape.Height;
            Rectangle? best = null;
            float bestScore = float.MinValue;
            foreach (var host in next[room].Where(boxes.ContainsKey).Select(other => boxes[other]))
            {
                foreach (var spot in new[]
                {
                    new Point(host.Left - w, host.Top), new Point(host.Right, host.Top), new Point(host.Left, host.Top - h), new Point(host.Left, host.Bottom),
                    new Point(host.Left - w, host.Bottom - h), new Point(host.Right, host.Bottom - h), new Point(host.Right - w, host.Top - h), new Point(host.Right - w, host.Bottom)
                })
                {
                    var box = new Rectangle(spot, new Size(w, h));
                    if (boxes.Values.Any(other => other.IntersectsWith(box)))
                    {
                        continue;
                    }

                    int contact = boxes.Values.Sum(other => Contact(box, other));
                    int across = boxes.Values.Append(box).Max(other => other.Right + other.Bottom) - boxes.Values.Append(box).Min(other => other.Left + other.Top);
                    int down = boxes.Values.Append(box).Max(other => other.Bottom - other.Left) - boxes.Values.Append(box).Min(other => other.Top - other.Right);
                    float score = contact - 0.02f * across * down + _random.Next();
                    if (score > bestScore)
                    {
                        (best, bestScore) = (box, score);
                    }
                }
            }

            if (best is not null)
            {
                boxes[room] = best.Value;
                problem.Hints[room] = best.Value.Location;
            }
        }
    }

    private static Rectangle Bound(HashSet<Cell> cells, int u, int v) =>
        new(u, v, cells.Max(cell => cell.U) + 1, cells.Max(cell => cell.V) + 1);

    private static int Contact(Rectangle a, Rectangle b) =>
        a.Right == b.Left || b.Right == a.Left ? Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top))
        : a.Bottom == b.Top || b.Bottom == a.Top ? Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) : 0;

    private int Add(OfficePart part, HashSet<Cell> cells) => Add(part, cells, [], 0);

    private int Add(OfficePart part, HashSet<Cell> cells, int[] row, int depth)
    {
        _parts.Add((part, new RoomShape(cells, Doors, RoomShape.AllTurns), row, depth));
        return _parts.Count - 1;
    }

    private int Add(OfficePart part, (HashSet<Cell> Cells, int[] Row, int Depth) block) => Add(part, block.Cells, block.Row, block.Depth);

    private HashSet<Cell> Box(float area, float low, float high)
    {
        int width = (int)MathF.Sqrt(area * _random.Uniform(low, high));
        return RoomShape.Rectangle(width, Math.Max(9, (int)(area / width)));
    }

    private HashSet<Cell> Hall()
    {
        int depth = _random.Inclusive(8, 11);
        return RoomShape.Rectangle(depth, _random.Inclusive((int)(2.6f * depth), 4 * depth));
    }

    private HashSet<Cell> Ell(float area)
    {
        var box = Box(area * 1.3f, 1.1f, 1.7f);
        int w = box.Max(cell => cell.U) + 1, h = box.Max(cell => cell.V) + 1;
        int cutW = _random.Inclusive(w / 3, w / 2), cutH = _random.Inclusive(h / 3, h / 2);
        return RoomShape.Polygon(new Point(0, 0), new Point(w, 0), new Point(w, h - cutH), new Point(w - cutW, h - cutH), new Point(w - cutW, h), new Point(0, h));
    }

    // Returns a room with a row of small offices along one side, laid as one block and cut after the solve
    private (HashSet<Cell>, int[], int) Block(int offices)
    {
        int depth = _random.Inclusive(7, 8);
        int[] widths = Enumerable.Range(0, offices).Select(_ => _random.Inclusive(5, SmallMax / depth)).ToArray();
        return (RoomShape.Rectangle(widths.Sum() + _random.Inclusive(0, 9) + _random.Inclusive(0, 6), depth + _random.Inclusive(8, 14)), widths, depth);
    }

    private OfficeLayout? Finish(LayoutProblem<int> problem, Dictionary<int, PlacedRoom<int>> placed)
    {
        var rooms = new List<OfficeRoom>();
        int[] hostOf = new int[_parts.Count];
        var rowOf = new List<int>[_parts.Count];
        var joined = problem.Joins.SelectMany(join => new[] { join, (join.B, join.A) }).ToLookup(join => join.Item1, join => join.Item2);
        for (int index = 0; index < _parts.Count; index++)
        {
            (var part, _, int[] row, int depth) = _parts[index];
            var cells = placed[index].Cells;
            var others = placed.Values.Where(room => room.Room != index).SelectMany(room => room.Cells).ToHashSet();
            var (host, offices) = row.Length == 0 ? (cells, []) : CutRow(cells, row, depth, others, joined[index].Select(other => placed[other].Cells).ToArray());
            int phase = part switch
            {
                OfficePart.Start => 0,
                OfficePart.Run or OfficePart.Gate or OfficePart.Breaker => 1,
                _ => 2
            };
            hostOf[index] = rooms.Count;
            rooms.Add(new OfficeRoom(host, part switch
            {
                OfficePart.Start or OfficePart.Hall => RoomType.Hall,
                OfficePart.Gate or OfficePart.Suite or OfficePart.Host => RoomType.Office,
                _ => _random.Choice((RoomType[])[RoomType.Office, RoomType.Office, RoomType.Lab, RoomType.Storage])
            }, phase));

            rowOf[index] = offices.Select(office =>
            {
                rooms.Add(new OfficeRoom(office, _random.Choice((RoomType[])[RoomType.Office, RoomType.Office, RoomType.Depot]), phase));
                return rooms.Count - 1;
            }).ToList();
        }

        var large = Enumerable.Range(0, _parts.Count).Where(index => _parts[index].Part is OfficePart.Big or OfficePart.Medium or OfficePart.Hall).Select(index => hostOf[index]);
        Notch(rooms, large.ToArray(), problem.Joins.Select(join => (hostOf[join.A], hostOf[join.B])).ToArray());
        FillPockets(rooms);
        var doors = new HashSet<RoomPair>();
        var run = _run.Select(index => hostOf[index]).ToList();
        int breakerAt = _run[^1];
        if (rowOf[breakerAt].Count == 0)
        {
            return null;
        }

        // Take the largest office of the breaker room's row for the breaker and the others for locked rooms of the last phase off the run
        int breaker = rowOf[breakerAt].MaxBy(office => rooms[office].Cells.Count);
        foreach (int office in rowOf[breakerAt])
        {
            rooms[office] = rooms[office] with { Phase = office == breaker ? 1 : 2, Type = office == breaker ? RoomType.Office : rooms[office].Type };
            doors.Add(new RoomPair(run[^1], office));
        }

        run.Add(breaker);
        for (int index = 0; index + 1 < run.Count; index++)
        {
            doors.Add(new RoomPair(run[index], run[index + 1]));
        }

        foreach (int office in rowOf[_run[0]])
        {
            doors.Add(new RoomPair(run[0], office));
        }

        int suiteAt = _after[^1];
        var suite = rowOf[suiteAt].Prepend(hostOf[suiteAt]).ToList();
        for (int index = 0; index + 1 < suite.Count; index++)
        {
            doors.Add(new RoomPair(suite[index], suite[index + 1]));
        }

        foreach ((int a, int b) in problem.Joins.Where(join => _after.Contains(join.B) && (_after.Contains(join.A) || _run.Contains(join.A))))
        {
            doors.Add(new RoomPair(hostOf[a], hostOf[b]));
        }

        // Lay a door on every wall a door wide between rooms the breaker opens, but between the suite's offices
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

        // Open each room the doors leave unreached off a dark run room it shares a wall with, never off the start hall
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
        foreach (int[] hosts in suite.Take(2).Select(room => new[] { room }).Append([run[^2], run[^3]]))
        {
            hosts.Any(host => PlaceCloset(rooms, doors, host, closets));
        }

        var depths = Depths(doors, breaker);
        int end = Enumerable.Range(0, rooms.Count)
            .Where(room => rooms[room].Phase == 2 && rooms[room].Cells.Count >= LargeMin && depths.ContainsKey(room))
            .MaxBy(room => depths[room]);

        return new OfficeLayout(rooms, doors, run, closets, end);
    }

    // Cuts a corner a third to a half of each side off three or four large rooms where the rest stays large, six wide and a door-wide wall from
    // each room it is joined to
    private void Notch(List<OfficeRoom> rooms, int[] candidates, (int A, int B)[] joins)
    {
        int most = _random.Inclusive(3, 4), done = 0;
        foreach (int room in _random.Shuffled(candidates))
        {
            if (done >= most)
            {
                break;
            }

            var box = rooms[room].Cells.GetBounds();
            (int w, int h) = (_random.Inclusive(box.W / 3, box.W / 2), _random.Inclusive(box.H / 3, box.H / 2));
            var corner = new UnitRect(_random.Next() < 0.5f ? box.U : box.U + box.W - w, _random.Next() < 0.5f ? box.V : box.V + box.H - h, w, h);
            var rest = rooms[room].Cells.Where(cell => !corner.Contains(cell)).ToHashSet();
            if (w >= 4 && h >= 4 && rest.Count >= LargeMin && rest.IsThick(6) && rest.IsConnected()
                && joins.Where(join => join.A == room || join.B == room).All(join => rest.GetLongestWall(rooms[join.A == room ? join.B : join.A].Cells).Length >= DoorWall))
            {
                rooms[room] = rooms[room] with { Cells = rest };
                done++;
            }
        }
    }

    // Cuts a one-unit slit from the larger room along about a third of the walls of eight units or more, keeping a door-wide stub at the middle of
    // a wall that holds a door, the room left connected, four wide and of its size class and every door of it on a door-wide wall
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

    // Places a closet three units a side in a corner inside the host where the host stays large, four wide and a door-wide wall from each room it
    // opens onto, else outside it on free floor touching it along three unit edges or more
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

    // Cuts the row along the block side touching least of the other rooms, in a corner or inset, keeping the host a door-wide wall to each joined
    // room
    private (HashSet<Cell>, List<HashSet<Cell>>) CutRow(HashSet<Cell> block, int[] widths, int depth, HashSet<Cell> others, HashSet<Cell>[] joined)
    {
        int u0 = block.Min(cell => cell.U), v0 = block.Min(cell => cell.V), u1 = block.Max(cell => cell.U) + 1, v1 = block.Max(cell => cell.V) + 1;
        var bounds = new UnitRect(u0, v0, u1 - u0, v1 - v0);
        var sides = new (Side Side, bool Along, int Line)[] { (Side.North, true, v0 - 1), (Side.South, true, v1), (Side.West, false, u0 - 1), (Side.East, false, u1) }
            .OrderBy(side => (side.Along ? Enumerable.Range(u0, u1 - u0).Count(u => others.Contains(new Cell(u, side.Line)))
                : Enumerable.Range(v0, v1 - v0).Count(v => others.Contains(new Cell(side.Line, v)))) + _random.Next());

        foreach ((var side, bool along, int _) in sides)
        {
            int length = along ? u1 - u0 : v1 - v0, free = length - widths.Sum();
            if (free < 0)
            {
                continue;
            }

            int[] spots = free >= 10 ? new[] { 0, free, _random.Inclusive(4, free - 4) } : new[] { 0, free };
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
                if (host.Count >= LargeMin && host.IsConnected() && joined.All(room => host.GetLongestWall(room).Length >= DoorWall))
                {
                    return (host, offices);
                }
            }
        }

        return (block, []);
    }

    // Fills the building's enclosed pockets and its notches three to six units wide, a pocket the size of a room becoming one and the rest going to
    // the neighbor whose outline gains at most one notch
    private static void FillPockets(List<OfficeRoom> rooms)
    {
        var owner = new Dictionary<Cell, int>();
        for (int index = 0; index < rooms.Count; index++)
        {
            foreach (var cell in rooms[index].Cells)
            {
                owner[cell] = index;
            }
        }

        int u0 = owner.Keys.Min(cell => cell.U) - 1, v0 = owner.Keys.Min(cell => cell.V) - 1, u1 = owner.Keys.Max(cell => cell.U) + 2, v1 = owner.Keys.Max(cell => cell.V) + 2;
        var outside = new HashSet<Cell> { new(u0, v0) };
        var stack = new Stack<Cell>(outside);
        while (stack.Count > 0)
        {
            foreach (var next in stack.Pop().GetAdjacent().Where(next => next.U >= u0 && next.U < u1 && next.V >= v0 && next.V < v1 && !owner.ContainsKey(next) && outside.Add(next)))
            {
                stack.Push(next);
            }
        }

        var pockets = new UnitRect(u0, v0, u1 - u0, v1 - v0).ToCells().Where(cell => !owner.ContainsKey(cell) && !outside.Contains(cell)).ToHashSet();
        foreach (var cell in outside)
        {
            foreach ((int du, int dv) in new[] { (1, 0), (0, 1) })
            {
                int low = Enumerable.Range(1, 6).FirstOrDefault(k => owner.ContainsKey(new Cell(cell.U - k * du, cell.V - k * dv)));
                int high = Enumerable.Range(1, 6).FirstOrDefault(k => owner.ContainsKey(new Cell(cell.U + k * du, cell.V + k * dv)));
                if (low > 0 && high > 0 && low + high - 1 is >= 3 and <= 6
                    && owner[new Cell(cell.U - low * du, cell.V - low * dv)] != owner[new Cell(cell.U + high * du, cell.V + high * dv)])
                {
                    pockets.Add(cell);
                }
            }
        }

        foreach (var pocket in pockets.GetPieces())
        {
            int w = pocket.Max(cell => cell.U) - pocket.Min(cell => cell.U) + 1, h = pocket.Max(cell => cell.V) - pocket.Min(cell => cell.V) + 1;
            bool isBox = pocket.Count == w * h && Math.Min(w, h) >= 4;
            if ((isBox && pocket.Count is >= 28 and <= SmallMax) || (pocket.Count >= LargeMin && pocket.IsThick(6) && Corners(pocket) <= 6))
            {
                rooms.Add(new OfficeRoom(pocket, pocket.Count <= SmallMax ? RoomType.Depot : RoomType.Storage, 2));
                continue;
            }

            var shared = pocket.SelectMany(cell => cell.GetAdjacent()).Where(owner.ContainsKey).GroupBy(cell => owner[cell]).ToDictionary(group => group.Key, group => group.Count());
            int[] hosts = shared.Keys.Where(room => rooms[room].Cells.Count > SmallMax).ToArray();
            int? best = hosts
                .Where(room => Corners(rooms[room].Cells.Union(pocket).ToHashSet()) <= Corners(rooms[room].Cells) + 2)
                .OrderByDescending(room => shared[room])
                .Cast<int?>()
                .FirstOrDefault() ?? hosts.OrderByDescending(room => shared[room]).Cast<int?>().FirstOrDefault();
            if (best is not null)
            {
                rooms[best.Value].Cells.UnionWith(pocket);
            }
        }
    }

    private static int Corners(HashSet<Cell> cells)
    {
        var points = new Dictionary<Cell, int>();
        foreach (var cell in cells)
        {
            foreach (var point in new[] { cell, cell with { U = cell.U + 1 }, cell with { V = cell.V + 1 }, new Cell(cell.U + 1, cell.V + 1) })
            {
                points[point] = points.GetValueOrDefault(point) + 1;
            }
        }

        return points.Sum(pair => pair.Value switch
        {
            1 or 3 => 1,
            2 when cells.Contains(new Cell(pair.Key.U - 1, pair.Key.V - 1)) == cells.Contains(pair.Key) => 2,
            _ => 0
        });
    }

    private static HashSet<int> Reach(HashSet<RoomPair> doors, int from) => Depths(doors, from).Keys.ToHashSet();

    private static Dictionary<int, int> Depths(HashSet<RoomPair> doors, int from) => doors
        .SelectMany(door => new[] { (door.A, door.B), (door.B, door.A) })
        .GroupBy(door => door.Item1, door => door.Item2)
        .ToDictionary(group => group.Key, group => group.ToList())
        .MeasureHops([from]);
}
