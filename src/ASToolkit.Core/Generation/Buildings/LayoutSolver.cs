using System.Drawing;
using System.Runtime.CompilerServices;
using ASToolkit.Core.Levels;
using Google.OrTools.Sat;

namespace ASToolkit.Core.Generation.Buildings;

// Lays rooms with CP-SAT, each in one of its shapes and turns, joined rooms meeting on a door segment of each and the problem's distances, touches,
// door sides, beyond pairs and obstacles held as constraints, taking the first layout found from random hints
internal sealed class LayoutSolver<T>
    where T : notnull
{
    private static readonly ConditionalWeakTable<RoomShape, Variant[]> TurnedShapes = new();

    private const int Reach = 400;
    private const double Seconds = 0.5;

    private readonly LayoutProblem<T> _problem;
    private readonly Random _random;
    private readonly CpModel _model = new();
    private readonly Dictionary<T, Node> _nodes = [];
    private readonly List<(BoolVar Literal, bool Value)> _hints = [];
    private readonly Dictionary<T, List<BoolVar>> _contacts = [];

    public LayoutSolver(LayoutProblem<T> problem, int seed)
    {
        _problem = problem;
        _random = new Random(seed);
    }

    public Dictionary<T, PlacedRoom<T>>? Solve()
    {
        var rooms = _problem.Rooms.Keys.ToArray();
        foreach (var room in rooms)
        {
            _nodes[room] = AddNode(room, _nodes.Count == 0);
        }

        var noOverlap = _model.AddNoOverlap2D();
        foreach (var slot in _nodes.Values.SelectMany(node => node.Slots))
        {
            var uEnd = _model.NewIntVar(-Reach * 2, Reach * 2, "");
            var vEnd = _model.NewIntVar(-Reach * 2, Reach * 2, "");
            _model.Add(uEnd == slot.UMax);
            _model.Add(vEnd == slot.VMax);
            noOverlap.AddRectangle(_model.NewIntervalVar(slot.UMin, slot.W, uEnd, ""), _model.NewIntervalVar(slot.VMin, slot.H, vEnd, ""));
        }

        if (_problem.Band is not null)
        {
            var band = _problem.Band.Value;
            var across = _model.NewIntVar(-Reach * 4, Reach * 4, "");
            var down = _model.NewIntVar(-Reach * 4, Reach * 4, "");
            foreach (var slot in _nodes.Values.SelectMany(node => node.Slots))
            {
                _model.Add(slot.UMin + slot.VMin >= across);
                _model.Add(slot.UMax + slot.VMax <= across + band.Across);
                _model.Add(slot.VMin - slot.UMax >= down);
                _model.Add(slot.VMax - slot.UMin <= down + band.Down);
            }
        }

        foreach (var room in rooms)
        {
            _contacts[room] = [];
        }

        var joined = _problem.Joins.SelectMany(join => new[] { join, (join.B, join.A) }).ToHashSet();
        var touches = _problem.TouchPairs.SelectMany(pair => new[] { pair, (pair.B, pair.A) }).ToHashSet();
        var mayTouch = _problem.MayTouchPairs.SelectMany(pair => new[] { pair, (pair.B, pair.A) }).ToHashSet();
        for (int i = 0; i < rooms.Length; i++)
        {
            for (int j = i + 1; j < rooms.Length; j++)
            {
                var pair = (rooms[i], rooms[j]);
                bool flush = joined.Contains(pair) || touches.Contains(pair) || mayTouch.Contains(pair);
                int gap = flush ? Math.Max(_problem.MinimumDistance, 2) : _problem.MinimumDistance;
                if (gap > 0)
                {
                    var contact = KeepApart(_nodes[rooms[i]].Slots, _nodes[rooms[j]].Slots, gap, flush);
                    if (contact is not null)
                    {
                        _contacts[rooms[i]].Add(contact);
                        _contacts[rooms[j]].Add(contact);
                    }
                }
            }
        }

        if (_problem.LeastContacts > 0)
        {
            foreach (var room in rooms)
            {
                _model.Add(LinearExpr.Sum(_contacts[room]) >= _problem.LeastContacts);
            }
        }

        foreach (var (a, b) in _problem.TouchPairs)
        {
            Touch(_nodes[a], _nodes[b], _problem.TouchLengths.TryGetValue((a, b), out int length) ? length : _problem.TouchLength);
        }

        foreach (var (a, b) in _problem.Joins)
        {
            Join(_nodes[a], _nodes[b], _problem.DoorSides.GetValueOrDefault((a, b)), _problem.DoorSides.GetValueOrDefault((b, a)));
        }

        foreach (var (anchor, room) in _problem.BeyondPairs)
        {
            StandBeyond(anchor, room, joined.Contains((anchor, room)));
        }

        foreach (var (anchor, room) in _problem.BeyondPairs.Concat(_problem.ClearPairs))
        {
            var anchorNode = _nodes[anchor];
            var obstacles = _problem.Obstacles
                .Where(obstacle => EqualityComparer<T>.Default.Equals(obstacle.Anchor, anchor))
                .SelectMany(obstacle => obstacle.Rects)
                .Select(rect => new Slot(anchorNode.U, anchorNode.V, rect.U, rect.V, LinearExpr.Constant(rect.W), LinearExpr.Constant(rect.H)))
                .ToArray();

            if (obstacles.Length > 0)
            {
                KeepApart(_nodes[room].Slots, obstacles, Math.Max(_problem.MinimumDistance, 1), false);
            }
        }

        foreach ((var literal, bool value) in _hints)
        {
            _model.AddHint(literal, value);
        }

        // Skip presolve, probing, symmetry detection and, unless the problem keeps it, phase saving
        var solver = new CpSolver
        {
            StringParameters = "num_workers:1 linearization_level:0 cp_model_presolve:false cp_model_probing_level:0 symmetry_level:0 " + (_problem.SavesPhases ? "" : "use_phase_saving:false ") +
                $"random_seed:{_random.Next(1_000_000)} max_deterministic_time:{Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        };

        var status = solver.Solve(_model);
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
        {
            return null;
        }

        return rooms.ToDictionary(room => room, room =>
        {
            var node = _nodes[room];
            var variant = node.Variants[Array.FindIndex(node.Choices, solver.BooleanValue)];
            var position = new Point((int)solver.Value(node.U), (int)solver.Value(node.V));
            return new PlacedRoom<T>(room, variant.Turn, variant.Cells.Select(cell => new Cell(cell.U + position.X, cell.V + position.Y)).ToHashSet(),
                _problem.Rooms[room].IsCorridor);
        });
    }

    private Node AddNode(T room, bool isPinned)
    {
        bool readsSides = _problem.DoorSides.Keys.Any(key => EqualityComparer<T>.Default.Equals(key.Room, room));
        var variants = _problem.Rooms[room].Shapes
            .SelectMany(shape => TurnedShapes.GetValue(shape, turned => turned.Turns.Distinct().Select(turn => Turn(turned, turn)).ToArray()))
            .GroupBy(variant => readsSides ? variant.SidedKey : variant.PlainKey)
            .Select(group => group.First())
            .ToArray();

        var choices = variants.Select(_ => _model.NewBoolVar("")).ToArray();
        _model.AddExactlyOne(choices);
        bool placed = _problem.Hints.TryGetValue(room, out var hint);
        int unturned = Array.FindIndex(variants, variant => variant.Turn == ShapeTurn.Identity);
        int hinted = placed && unturned >= 0 ? unturned : _random.Next(choices.Length);
        for (int index = 0; index < choices.Length; index++)
        {
            _hints.Add((choices[index], index == hinted));
        }

        var u = isPinned ? _model.NewConstant(0) : _model.NewIntVar(-Reach, Reach, "");
        var v = isPinned ? _model.NewConstant(0) : _model.NewIntVar(-Reach, Reach, "");
        if (placed && !isPinned)
        {
            _model.AddHint(u, hint.X);
            _model.AddHint(v, hint.Y);
        }

        int count = variants.Max(variant => variant.Rects.Length);
        var slots = new List<Slot>();
        for (int k = 0; k < count; k++)
        {
            // Give a variant with fewer rectangles empty ones at its first rectangle's corner
            var pick = variants.Select(variant => k < variant.Rects.Length ? variant.Rects[k] : new UnitRect(variant.Rects[0].U, variant.Rects[0].V, 0, 0)).ToArray();
            if (variants.Length == 1)
            {
                slots.Add(new Slot(u, v, pick[0].U, pick[0].V, LinearExpr.Constant(pick[0].W), LinearExpr.Constant(pick[0].H)));
                continue;
            }

            var su = _model.NewIntVar(-Reach * 2, Reach * 2, "");
            var sv = _model.NewIntVar(-Reach * 2, Reach * 2, "");
            var sw = _model.NewIntVar(0, Reach, "");
            var sh = _model.NewIntVar(0, Reach, "");
            _model.Add(su == u + LinearExpr.WeightedSum(choices, pick.Select(rect => (long)rect.U)));
            _model.Add(sv == v + LinearExpr.WeightedSum(choices, pick.Select(rect => (long)rect.V)));
            _model.Add(sw == LinearExpr.WeightedSum(choices, pick.Select(rect => (long)rect.W)));
            _model.Add(sh == LinearExpr.WeightedSum(choices, pick.Select(rect => (long)rect.H)));
            slots.Add(new Slot(su, sv, 0, 0, sw, sh));
        }

        return new Node(u, v, choices, variants, slots.ToArray());
    }

    // Keeps every rectangle of one set the gap from every rectangle of the other, or flush against it when the pair may touch, and returns the
    // pair's contact literal when contacts are counted
    private BoolVar? KeepApart(Slot[] first, Slot[] second, int gap, bool flush)
    {
        var contact = flush && _problem.LeastContacts > 0 ? _model.NewBoolVar("") : null;
        var longs = new List<ILiteral>();
        foreach (var a in first)
        {
            foreach (var b in second)
            {
                var options = new List<ILiteral>();
                foreach (var (low, high, across) in new[]
                {
                    (a.UMax, b.UMin, (a.VMin, a.VMax, b.VMin, b.VMax)), (b.UMax, a.UMin, (a.VMin, a.VMax, b.VMin, b.VMax)),
                    (a.VMax, b.VMin, (a.UMin, a.UMax, b.UMin, b.UMax)), (b.VMax, a.VMin, (a.UMin, a.UMax, b.UMin, b.UMax))
                })
                {
                    var far = _model.NewBoolVar("");
                    _model.Add(low + gap <= high).OnlyEnforceIf(far);
                    options.Add(far);
                    if (flush)
                    {
                        // Stand flush only where the two rectangles meet along the wall, never one unit apart past its end
                        var against = _model.NewBoolVar("");
                        _model.Add(low == high).OnlyEnforceIf(against);
                        _model.Add(across.Item1 <= across.Item4).OnlyEnforceIf(against);
                        _model.Add(across.Item3 <= across.Item2).OnlyEnforceIf(against);
                        options.Add(against);
                        if (contact is not null)
                        {
                            var along = _model.NewBoolVar("");
                            _model.Add(low == high).OnlyEnforceIf(along);
                            AddOverlap(across.Item1, across.Item2, across.Item3, across.Item4, 5, along);
                            options.Add(along);
                            longs.Add(along);
                        }
                    }
                }

                _model.AddBoolOr(options);
            }
        }

        if (contact is not null)
        {
            longs.Add(contact.Not());
            _model.AddBoolOr(longs);
        }

        return contact;
    }

    private void Touch(Node a, Node b, int length)
    {
        var options = new List<ILiteral>();
        foreach (var ra in a.Slots)
        {
            foreach (var rb in b.Slots)
            {
                foreach (var (low, high, alongA, alongB) in new[]
                {
                    (ra.UMax, rb.UMin, (ra.VMin, ra.VMax), (rb.VMin, rb.VMax)), (rb.UMax, ra.UMin, (ra.VMin, ra.VMax), (rb.VMin, rb.VMax)),
                    (ra.VMax, rb.VMin, (ra.UMin, ra.UMax), (rb.UMin, rb.UMax)), (rb.VMax, ra.VMin, (ra.UMin, ra.UMax), (rb.UMin, rb.UMax))
                })
                {
                    var option = _model.NewBoolVar("");
                    _model.Add(low == high).OnlyEnforceIf(option);
                    AddOverlap(alongA.Item1, alongA.Item2, alongB.Item1, alongB.Item2, length, option);
                    options.Add(option);
                }
            }
        }

        _model.AddBoolOr(options);
    }

    // Overlaps the spans [a0, a1) and [b0, b1) by at least the length when the literal holds, each span at least that long
    private void AddOverlap(LinearExpr a0, LinearExpr a1, LinearExpr b0, LinearExpr b1, int length, ILiteral literal)
    {
        _model.Add(a0 + length <= b1).OnlyEnforceIf(literal);
        _model.Add(b0 + length <= a1).OnlyEnforceIf(literal);
        _model.Add(a0 + length <= a1).OnlyEnforceIf(literal);
        _model.Add(b0 + length <= b1).OnlyEnforceIf(literal);
    }

    // Stands a door of each room on one wall segment, facing each other, each on its given sides in its shape's frame
    private void Join(Node a, Node b, DoorSides sidesOfA, DoorSides sidesOfB)
    {
        var u = _model.NewIntVar(-Reach * 2, Reach * 2, "");
        var v = _model.NewIntVar(-Reach * 2, Reach * 2, "");
        var length = _model.NewIntVar(0, Reach, "");
        var facing = _model.NewIntVar(0, 3, "");
        foreach ((var node, var sides, bool flip) in new[] { (a, sidesOfA, false), (b, sidesOfB, true) })
        {
            var options = new List<ILiteral>();
            for (int index = 0; index < node.Variants.Length; index++)
            {
                foreach (var line in node.Variants[index].Lines.Where(line => sides == DoorSides.None || (sides & SideOf(line.OwnFacing)) != 0))
                {
                    var option = _model.NewBoolVar("");
                    _model.AddImplication(option, node.Choices[index]);
                    // Fix the door's wall line and slide its start from Low to High along the line
                    var (fixedAt, origin, along, alongOrigin) = line.Facing < 2 ? (u, node.U, v, node.V) : (v, node.V, u, node.U);
                    _model.Add(fixedAt == origin + line.At).OnlyEnforceIf(option);
                    _model.Add(along >= alongOrigin + line.Low).OnlyEnforceIf(option);
                    _model.Add(along <= alongOrigin + line.High).OnlyEnforceIf(option);
                    _model.Add(length == line.Length).OnlyEnforceIf(option);
                    _model.Add(facing == (flip ? line.Facing ^ 1 : line.Facing)).OnlyEnforceIf(option);
                    options.Add(option);
                }
            }

            _model.AddExactlyOne(options);
            int hinted = _random.Next(options.Count);
            for (int index = 0; index < options.Count; index++)
            {
                _hints.Add(((BoolVar)options[index], index == hinted));
            }
        }
    }

    // Stands a room past the door end of an unturned anchor, the margin past it unless the room opens onto the anchor
    private void StandBeyond(T anchor, T room, bool opensOnto)
    {
        var sides = _problem.DoorSides
            .Where(pair => EqualityComparer<T>.Default.Equals(pair.Key.Room, anchor))
            .Aggregate(DoorSides.None, (all, pair) => all | pair.Value);

        int margin = opensOnto ? 0 : _problem.BeyondMargin;
        var options = new List<ILiteral>();
        foreach (var side in new[] { DoorSides.MaxX, DoorSides.MinX, DoorSides.MaxY, DoorSides.MinY }.Where(side => (sides & side) != 0))
        {
            var option = _model.NewBoolVar("");
            foreach (var slot in _nodes[room].Slots)
            {
                foreach (var end in _nodes[anchor].Slots)
                {
                    var rule = side switch
                    {
                        DoorSides.MaxX => slot.UMin >= end.UMax + margin,
                        DoorSides.MinX => slot.UMax <= end.UMin - margin,
                        DoorSides.MaxY => slot.VMin >= end.VMax + margin,
                        _ => slot.VMax <= end.VMin - margin
                    };

                    _model.Add(rule).OnlyEnforceIf(option);
                }
            }

            options.Add(option);
        }

        if (options.Count > 0)
        {
            _model.AddBoolOr(options);
        }
    }

    private static DoorSides SideOf(int facing) => facing switch
    {
        0 => DoorSides.MaxX,
        1 => DoorSides.MinX,
        2 => DoorSides.MaxY,
        _ => DoorSides.MinY
    };

    // Turns a shape and its doors, each door keeping the side it faces in the shape's own frame and its doors merged into sliding runs
    private static Variant Turn(RoomShape shape, ShapeTurn turn)
    {
        var cells = shape.Cells.Select(cell => turn.Apply(cell)).ToHashSet();
        var doors = new List<(int U, int V, int Length, int Facing, int OwnFacing)>();
        foreach (var door in shape.Doors)
        {
            var p = turn.Apply(door.From);
            var q = turn.Apply(door.To);
            (int u0, int v0, int u1, int v1) = (Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y));
            doors.Add((u0, v0, Math.Max(u1 - u0, v1 - v0), FacingOf(cells, u0, v0, u1),
                FacingOf(shape.Cells, Math.Min(door.From.X, door.To.X), Math.Min(door.From.Y, door.To.Y), Math.Max(door.From.X, door.To.X))));
        }

        var lines = new List<Line>();
        foreach (var group in doors.GroupBy(door => (door.Facing, door.OwnFacing, door.Length, At: door.Facing < 2 ? door.U : door.V)))
        {
            int[] starts = group.Select(door => group.Key.Facing < 2 ? door.V : door.U).Distinct().Order().ToArray();
            int low = starts[0];
            for (int i = 1; i <= starts.Length; i++)
            {
                if (i == starts.Length || starts[i] != starts[i - 1] + 1)
                {
                    lines.Add(new Line(group.Key.At, low, starts[i - 1], group.Key.Length, group.Key.Facing, group.Key.OwnFacing));
                    if (i < starts.Length)
                    {
                        low = starts[i];
                    }
                }
            }
        }

        return new Variant(shape, turn, cells, cells.ToRects(), lines);
    }

    // Returns the way a door segment faces out of the cells, 0 to 3 for +u, -u, +v and -v
    private static int FacingOf(HashSet<Cell> cells, int u0, int v0, int u1) => u0 == u1
        ? cells.Contains(new Cell(u0 - 1, v0)) ? 0 : 1
        : cells.Contains(new Cell(u0, v0 - 1)) ? 2 : 3;

    private sealed record Node(IntVar U, IntVar V, BoolVar[] Choices, Variant[] Variants, Slot[] Slots);

    private sealed record Variant(RoomShape Shape, ShapeTurn Turn, HashSet<Cell> Cells, UnitRect[] Rects, List<Line> Lines)
    {
        public string SidedKey => field ??= Key(true);

        public string PlainKey => field ??= Key(false);

        // Names the shape and doors a variant places wherever it stands, with the sides its doors face in its shape only where door sides are read
        private string Key(bool withOwnSides)
        {
            int u0 = Rects.Min(rect => rect.U), v0 = Rects.Min(rect => rect.V);
            var rects = Rects.Select(rect => (rect.U - u0, rect.V - v0, rect.W, rect.H)).Order();
            var lines = Lines
                .Select(line => line.Facing < 2
                    ? line with { At = line.At - u0, Low = line.Low - v0, High = line.High - v0 }
                    : line with { At = line.At - v0, Low = line.Low - u0, High = line.High - u0 })
                .Select(line => withOwnSides ? line : line with { OwnFacing = 0 })
                .Order();

            return string.Join(";", rects) + "|" + string.Join(";", lines);
        }
    }

    private sealed record Slot(LinearExpr U, LinearExpr V, int OffsetU, int OffsetV, LinearExpr W, LinearExpr H)
    {
        public LinearExpr UMin => U + OffsetU;

        public LinearExpr VMin => V + OffsetV;

        public LinearExpr UMax => U + OffsetU + W;

        public LinearExpr VMax => V + OffsetV + H;
    }

    // Holds a run of door starts on one wall line, the door this long and facing one way after the turn and another in the shape's frame
    private readonly record struct Line(int At, int Low, int High, int Length, int Facing, int OwnFacing) : IComparable<Line>
    {
        public int CompareTo(Line other) =>
            (At, Low, High, Length, Facing, OwnFacing).CompareTo((other.At, other.Low, other.High, other.Length, other.Facing, other.OwnFacing));
    }
}
