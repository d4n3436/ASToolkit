using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

internal sealed class DepotWing
{
    private const int LargestDepot = 420;
    private readonly BlockSolver _solver;
    private static readonly DoorRule SolverDoors = new(3, 1);
    private const int EntryLengthMin = 12;
    private const int EntryLengthMax = 14;
    private const int SmallestDepot = 110;
    private const int DockDepotLeast = 11;
    private const int DockJogMost = 4;

    private static readonly (int U, int V)[] Steps4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly GrammarRandom _random;
    private readonly List<DepotRoom> _rooms = [];
    private LabWing _kind;

    public DepotWing(GrammarRandom random)
    {
        _random = random;
        _solver = new BlockSolver(random);
    }

    public static int GetLeastSlots(LabWing kind) => kind switch
    {
        LabWing.ServiceCorridor => 8,
        LabWing.Dock => 5,
        _ => 0
    };

    // Draws the wing in its own frame with the entry strip's outer end on v = 0, or returns null when the draw fails
    public DepotWingLayout? Draw(LabWing kind, int slots, int nested, int entryWidth)
    {
        _rooms.Clear();
        _kind = kind;
        return _kind switch
        {
            LabWing.ServiceCorridor => DrawCorridor(slots, nested, entryWidth),
            LabWing.Dock => DrawDock(slots, nested, entryWidth),
            _ => DrawSections(slots, nested, entryWidth)
        };
    }

    private DepotWingLayout? DrawSections(int slots, int nested, int entryWidth)
    {
        bool passage = _random.Next() < 0.75;
        int countA = Math.Clamp(slots - 4, 3, 5);
        int countB = Math.Clamp(slots - countA - 1, 3, 5);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            _rooms.Clear();
            bool lConnector = attempt < 2 && _random.Next() < 0.6;
            var placed = ComposeBySolver(countA, countB, Math.Clamp(_random.Inclusive(5, 6), 3, Math.Max(3, entryWidth)), lConnector);
            if (placed is null)
            {
                continue;
            }

            var layout = Subdivide(placed, countA, countB, passage, nested + Math.Max(0, slots - 1 - countA - countB));
            if (layout is not null)
            {
                return layout;
            }
        }

        return null;
    }

    private Dictionary<string, HashSet<Cell>>? ComposeBySolver(int countA, int countB, int entryWidth, bool lConnector)
    {
        var level = new LayoutProblem<string> { MinimumDistance = 2 };
        ShapeTurn[] turns = [ShapeTurn.Identity, ShapeTurn.MirrorY];

        int court = _random.Inclusive(8, 10);
        level.AddRoom("entry", [EntryStrip(entryWidth, _random.Inclusive(EntryLengthMin, EntryLengthMax))]);
        level.AddRoom("A", Sections(countA, Side.East, 2, 2, true));
        level.Join("entry", "A");
        level.DoorSides[("entry", "A")] = DoorSides.MaxY;
        level.AddRoom("B", Sections(countB, Side.West, 2, 1, false));
        level.AddRoom("arm", [Corridor(_random.Inclusive(4, 5), court)], isCorridor: true);
        // Span the court with a short crossing's passage and office together, or run an L's passage on under B and up into B's underside through a
        // tall office
        var links = lConnector
            ? Enumerable.Range(court + 8, 7).Select(length => Passage(length, Enumerable.Range(court + 2, length - court - 4).ToArray())).ToArray()
            : new[] { court - 5, court - 6 }.Where(length => length >= 3).Select(length => Passage(length, [])).ToArray();

        var offices = lConnector
            ? Enumerable.Range(6, 5).Select(height => new RoomShape(RoomShape.Rectangle(5, height), SolverDoors, [ShapeTurn.Identity])).ToArray()
            : [new RoomShape(RoomShape.Rectangle(5, 6), SolverDoors, RoomShape.AllTurns)];

        level.AddRoom("link", links);
        level.AddRoom("office", offices);
        level.Join("A", "arm");
        level.Join("arm", "B");
        level.Join("A", "link");
        level.Join("link", "office");
        level.Join("office", "B");
        level.DoorSides[("A", "arm")] = DoorSides.MaxX;
        level.DoorSides[("B", "arm")] = DoorSides.MinX;
        level.DoorSides[("A", "link")] = DoorSides.MaxX;
        level.DoorSides[("B", "office")] = lConnector ? DoorSides.MinY : DoorSides.MinX;

        foreach (string room in level.Rooms.Keys.Where(room => room != "entry"))
        {
            level.BeyondPairs.Add(("entry", room));
        }

        var layout = _solver.Solve(level);
        if (layout is null)
        {
            return null;
        }

        var parts = layout.ToDictionary(room => room.Key, room => room.Value.Cells);
        int foot = parts["entry"].Min(cell => cell.V);
        int left = parts.Values.SelectMany(cells => cells).Min(cell => cell.U);
        return parts.ToDictionary(pair => pair.Key, pair => pair.Value.Select(cell => new Cell(cell.U - left, cell.V - foot)).ToHashSet());

        IReadOnlyList<RoomShape> Sections(int depots, Side? flat, double weightL, double weightT, bool takesEntry) => Enumerable.Range(0, 4)
            .Select(_ =>
            {
                var kind = _random.Weighted<EnvelopeKind>([(EnvelopeKind.Plain, 10 - weightL - weightT), (EnvelopeKind.L, weightL), (EnvelopeKind.T, weightT)]);
                int w = _random.Inclusive(27, 36), h = _random.Inclusive(24, 32);
                while (w * h > 330 * depots)
                {
                    h--;
                }

                if (kind != EnvelopeKind.Plain)
                {
                    (w, h) = (w * 6 / 5, h * 6 / 5);
                }

                return Shape(Envelope(w, h, flat, kind), takesEntry);
            })
            .ToArray();

        RoomShape Shape(HashSet<Cell> cells, bool takesEntry) =>
            new(cells, takesEntry ? SolverDoors.Place(cells).Concat(CenteredDoors(cells, entryWidth)).ToArray() : SolverDoors.Place(cells), turns);
    }

    private DepotWingLayout? Subdivide(Dictionary<string, HashSet<Cell>> placed, int countA, int countB, bool passage, int left)
    {
        var doors = new List<(int, int)>();
        var jammed = new List<(int, int)>();
        var shortcuts = new List<(int, int)>();
        int entry = Add(DepotRole.Entry, 0, placed["entry"]);
        var a = Run(placed["A"], countA, passage, 0, placed["entry"], placed["arm"]);
        if (a is null)
        {
            return null;
        }

        doors.Add((entry, a.Route[0]));
        AddRun(a.Route, doors);
        jammed.AddRange(a.Jammed);
        var run = new List<int> { entry };
        run.AddRange(a.Route);
        var court = new HashSet<Cell>();
        var b = Run(placed["B"], countB, false, 1, placed["arm"], null);
        if (b is null)
        {
            return null;
        }

        _rooms[a.Route[^1]].Cells.UnionWith(placed["arm"]);
        jammed.AddRange(b.Jammed);
        doors.Add((a.Route[^1], b.Route[0]));
        AddRun(b.Route, doors);
        run.AddRange(b.Route);
        int source = a.Route.Concat(new[] { a.Passage }).Where(id => id >= 0).MaxBy(id => _rooms[id].Cells.GetLongestWall(placed["link"]).Length);
        int target = b.Route.MaxBy(id => _rooms[id].Cells.GetLongestWall(placed["office"]).Length);
        AddCrossing(source, target, placed["link"], placed["office"], doors);
        // Take as the court every free unit between section A's east edge and section B's west edge on rows both reach
        var taken = Taken();
        var eastOfA = placed["A"].GroupBy(cell => cell.V).ToDictionary(row => row.Key, row => row.Max(cell => cell.U));
        var westOfB = placed["B"].GroupBy(cell => cell.V).ToDictionary(row => row.Key, row => row.Min(cell => cell.U));
        court.UnionWith(eastOfA.Keys
            .Where(westOfB.ContainsKey)
            .SelectMany(v => Enumerable.Range(eastOfA[v] + 1, Math.Max(0, westOfB[v] - eastOfA[v] - 1)).Select(u => new Cell(u, v)))
            .Where(cell => !taken.Contains(cell)));
        left -= 2;

        return FinishSections(a, run, doors, jammed, shortcuts, court, entry, left);
    }

    private SectionPlan? Run(HashSet<Cell> envelope, int count, bool passage, int section, HashSet<Cell> wayIn, HashSet<Cell>? wayOut)
    {
        int entrance = envelope.GetLongestWall(wayIn).Length;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var parted = Partition(envelope, count, passage);
            if (parted is null)
            {
                continue;
            }

            var (regions, strip) = parted.Value;
            if (strip is not null)
            {
                regions.Add(strip);
            }

            int inner = strip is null ? -1 : regions.Count - 1;
            var adjacency = Adjacency(regions);
            // Enter the first section through one depot holding the entrance's whole end
            var paths = Paths(adjacency, regions.Count, index => index != inner && (section == 0 ? regions[index].GetLongestWall(wayIn).Length >= entrance : Touches(regions[index], wayIn)),
                index => index != inner && (wayOut is null || Touches(regions[index], wayOut)), inner);

            if (wayOut is null)
            {
                paths = paths.Where(path => path.Take(path.Count - 2).Any(index => adjacency[path[^1]].Contains(index))).ToList();
            }

            if (paths.Count == 0)
            {
                continue;
            }

            var path = _random.Choice(paths);
            var ids = path.Select(index => Add(index == inner ? DepotRole.Passage : DepotRole.Run, section, regions[index])).ToList();
            var skipped = new List<(int, int)>();
            for (int i = 0; i < path.Count; i++)
            {
                for (int j = i + 2; j < path.Count; j++)
                {
                    if (adjacency[path[i]].Contains(path[j]))
                    {
                        skipped.Add((ids[i], ids[j]));
                    }
                }
            }

            var back = wayOut is null ? skipped.Where(pair => pair.Item2 == ids[^1]).ToList() : [];
            var jammed = _random.Shuffled(skipped.Where(pair => !back.Contains(pair))).Take(_random.Inclusive(1, 2)).ToList();
            int passageId = inner < 0 ? -1 : ids[path.IndexOf(inner)];
            return new SectionPlan { Route = ids, Jammed = jammed, Passage = passageId };
        }

        return null;
        
        bool Touches(HashSet<Cell> region, HashSet<Cell> other) => region.GetLongestWall(other).Length >= 3;
    }

    // Returns a passage three high open at its west end and on its top at the given offsets, at its east end when it has none on top
    private static RoomShape Passage(int length, int[] tops) => new(RoomShape.Rectangle(length, 3),
        tops.Select(at => new ShapeDoor(new Point(at, 3), new Point(at + 3, 3)))
            .Append(new ShapeDoor(new Point(0, 0), new Point(0, 3)))
            .Concat(tops.Length == 0 ? [new ShapeDoor(new Point(length, 0), new Point(length, 3))] : Array.Empty<ShapeDoor>())
            .ToList(), [ShapeTurn.Identity]);

    private static RoomShape EntryStrip(int width, int length) =>
        new(RoomShape.Rectangle(width, length), [new ShapeDoor(new Point(0, length), new Point(width, length))], [ShapeTurn.Identity]);

    private static IEnumerable<ShapeDoor> CenteredDoors(HashSet<Cell> cells, int length) => RoomShape.Facings
        .SelectMany(facing => RoomShape.Runs(cells, facing).Where(run => run.High - run.Low >= length + 2).Select(run =>
        {
            int start = run.Low + (run.High - run.Low - length) / 2;
            return facing.U != 0
                ? new ShapeDoor(new Point(run.At, start), new Point(run.At, start + length))
                : new ShapeDoor(new Point(start, run.At), new Point(start + length, run.At));
        }));

    private static RoomShape Corridor(int width, int length) => new(RoomShape.Rectangle(width, length),
        Enumerable.Range(0, width - 2)
            .SelectMany(at => new[] { new ShapeDoor(new Point(at, 0), new Point(at + 3, 0)), new ShapeDoor(new Point(at, length), new Point(at + 3, length)) })
            .ToList(), RoomShape.AllTurns);

    private void AddCrossing(int source, int target, HashSet<Cell> link, HashSet<Cell> office, List<(int, int)> doors)
    {
        int linkId = Add(DepotRole.Link, -1, link);
        int officeId = Add(DepotRole.Office, -1, office);
        doors.Add((source, linkId));
        doors.Add((linkId, officeId));
        doors.Add((officeId, target));
    }

    private DepotWingLayout? DrawCorridor(int slots, int nested, int entryWidth)
    {
        int k = Math.Clamp((slots - 2) / 2, 3, 4);
        for (int attempt = 0; attempt < 60; attempt++)
        {
            _rooms.Clear();
            // Shift the far row's walls one way by less than the end depots can give
            int[] widths = Enumerable.Range(0, k).Select(_ => _random.Inclusive(13, 16)).ToArray();
            int w = widths.Sum();
            int near = _random.Inclusive(11, 14);
            int far = _random.Inclusive(11, 14);
            int band = _random.Inclusive(5, 6);
            int h = near + band + far;
            var envelope = Corners(w, h, null, _random.Choice([0, 1, 2]), shallowOnly: true);
            int[] cuts = Enumerable.Range(1, k - 1).Select(index => widths.Take(index).Sum()).ToArray();
            var nearRow = Row(envelope, 0, near, cuts, w, k);
            int toward = _random.Next() < 0.5 ? -1 : 1;
            int shift = toward * _random.Inclusive(3, Math.Min(6, (toward < 0 ? widths[0] : widths[^1]) - 10));
            int[] shifted = cuts.Select(cut => cut + shift).ToArray();
            var farRow = Row(envelope, near + band, h, shifted, w, k);
            if (nearRow is null || farRow is null)
            {
                continue;
            }

            var bandCells = envelope.Where(cell => cell.V >= near && cell.V < near + band).ToHashSet();
            int end = bandCells.Max(cell => cell.U);
            var throat = bandCells.Where(cell => cell.U > end - 7).ToHashSet();
            var corridor = bandCells.Where(cell => !throat.Contains(cell)).ToHashSet();
            var nearIds = nearRow.Select(cells => Add(DepotRole.Run, 0, cells)).ToList();
            int throatId = Add(DepotRole.Passage, 0, throat);
            var farIds = farRow.Select(cells => Add(DepotRole.Run, 0, cells)).ToList();
            var route = nearIds.Append(throatId).Concat(Enumerable.Reverse(farIds)).ToList();
            if (!IsWalkable(route))
            {
                continue;
            }

            var targets = Enumerable.Range(0, k)
                .Select(index => index % 2 == 0 ? nearIds[index] : farIds[index])
                .Where(id => corridor.GetLongestWall(_rooms[id].Cells).Length >= 3)
                .ToList();

            if (targets.Count < 3 || targets[0] != nearIds[0])
            {
                continue;
            }

            var doors = new List<(int, int)>();
            int corridorId = Add(DepotRole.Corridor, -1, corridor);
            int entry = PlaceEntry(route[0], entryWidth, int.MaxValue, doors);
            if (entry < 0)
            {
                continue;
            }

            AddRun(route, doors);
            foreach (int target in targets.Skip(1))
            {
                doors.Add((target, corridorId));
            }

            var depots = route.Where(id => _rooms[id].Role == DepotRole.Run).ToList();
            var layout = Furnish(route.Prepend(entry).ToList(), doors, [(corridorId, nearIds[0])], [], _rooms[entry].Cells.GetSurroundings(6),
                nested - 1 + Math.Max(0, slots - 1 - 2 * k), depots, 0, depots.Skip(1).ToArray(), false);
            if (layout is not null)
            {
                return layout;
            }
        }

        return null;
    }

    private List<HashSet<Cell>>? Row(HashSet<Cell> envelope, int low, int high, int[] cuts, int w, int k)
    {
        int previous = 0;
        foreach (int cut in cuts.Append(w))
        {
            if (cut - previous < 10)
            {
                return null;
            }

            previous = cut;
        }

        var steps = cuts
            .Select(_ => _random.Next() < 0.6 ? (At: _random.Inclusive(low + 5, high - 6), Shift: (_random.Next() < 0.5 ? -1 : 1) * _random.Inclusive(3, 4)) : (At: high, Shift: 0))
            .ToArray();

        var regions = Enumerable.Range(0, k).Select(_ => new HashSet<Cell>()).ToList();
        foreach (var cell in envelope.Where(cell => cell.V >= low && cell.V < high))
        {
            int index = k - 1;
            for (int i = 0; i < cuts.Length; i++)
            {
                if (cell.U < cuts[i] + (cell.V >= steps[i].At ? steps[i].Shift : 0))
                {
                    index = i;
                    break;
                }
            }

            regions[index].Add(cell);
        }

        return regions.All(region => region.IsConnected() && region.IsThick(5) && region.Count >= SmallestDepot) ? regions : null;
    }

    private DepotWingLayout? DrawDock(int slots, int nested, int entryWidth)
    {
        int k = Math.Clamp(slots - 2, 3, 4);
        for (int attempt = 0; attempt < 60; attempt++)
        {
            _rooms.Clear();
            int dockHeight = _random.Inclusive(9, 11);
            int depth = _random.Inclusive(14, 18);
            int h = dockHeight + depth;
            // Split the dock's length among its depots, none so wide that the depot, deepened by the dock's jog, passes the largest depot
            int w = _random.Inclusive(13, 16) * k;
            int most = Math.Max(DockDepotLeast, LargestDepot / (depth + DockJogMost));
            int[] widths = Enumerable.Repeat(DockDepotLeast, k).ToArray();
            for (int left = w - DockDepotLeast * k; left > 0; left--)
            {
                int[] open = Enumerable.Range(0, k).Where(index => widths[index] < most).ToArray();
                widths[_random.Choice(open)]++;
            }

            var envelope = Corners(w, h, Side.North, _random.Choice([0, 1, 2]), shallowOnly: false);
            var dock = envelope.Where(cell => cell.V < dockHeight).ToHashSet();
            int[] cuts = Enumerable.Range(1, k - 1).Select(index => widths.Take(index).Sum()).ToArray();
            var row = Row(envelope, dockHeight, h, cuts, w, k);
            if (row is null)
            {
                continue;
            }

            // Jog the dock's back wall only on a wall between two depots
            if (_random.Next() < 0.6)
            {
                int jog = _random.Choice(cuts);
                int jogDepth = _random.Inclusive(3, DockJogMost);
                bool leftSide = _random.Next() < 0.5;
                var moved = dock.Where(cell => cell.V >= dockHeight - jogDepth && (leftSide ? cell.U < jog : cell.U >= jog)).ToList();
                foreach (var cell in moved)
                {
                    dock.Remove(cell);
                    row.First(region => region.Contains(new Cell(cell.U, dockHeight))).Add(cell);
                }
            }

            if (row.Any(region => region.GetNarrow(4).Count > 0 || region.CountOneUnitSteps() > 0) || dock.GetNarrow(4).Count > 0 || dock.CountOneUnitSteps() > 0)
            {
                continue;
            }

            int dockId = Add(DepotRole.Run, 0, dock);
            var ids = row.Select(region => Add(DepotRole.Run, 0, region)).Reverse().ToList();
            var route = ids.Prepend(dockId).ToList();
            if (!IsWalkable(route) || dock.GetLongestWall(_rooms[route[^1]].Cells).Length < 3)
            {
                continue;
            }

            var doors = new List<(int, int)>();
            int entry = PlaceEntry(route[0], entryWidth, w / 4, doors);
            if (entry < 0)
            {
                continue;
            }

            AddRun(route, doors);
            var jammed = route.Skip(2).Take(route.Count - 3).Where(id => _rooms[dockId].Cells.GetLongestWall(_rooms[id].Cells).Length >= 3).Take(2).Select(id => (dockId, id)).ToList();
            var banned = _rooms[entry].Cells.GetSurroundings(6);
            banned.UnionWith(Box(-12, -14, w / 3 + 20, 14));
            var layout = Furnish(route.Prepend(entry).ToList(), doors, jammed, [(route[^1], dockId)], banned, nested - 1 + Math.Max(0, slots - 2 - k), ids, 2,
                ids.Where(id => _rooms[id].Role == DepotRole.Run).ToArray(), false, dockId);
            if (layout is not null)
            {
                return layout;
            }
        }

        return null;
    }

    // Lays the entry strip against the block with its door under the first depot's foot within maxU, and moves the wing so the strip's outer end
    // stands on v = 0
    private int PlaceEntry(int first, int entryWidth, int maxU, List<(int, int)> doors)
    {
        var block = Solid(first);
        var firstCells = _rooms[first].Cells;
        int width = Math.Clamp(_random.Inclusive(5, 6), 3, Math.Max(3, entryWidth));
        // Open the strip's whole width onto the first depot's foot, centered on each straight run of it, or anywhere within maxU when given
        var openings = firstCells
            .Where(cell => !block.Contains(new Cell(cell.U, cell.V - 1)))
            .GroupBy(cell => cell.V)
            .SelectMany(row => row.Select(cell => cell.U).GetRuns().Where(run => run.High - run.Low >= width).SelectMany(run =>
                (maxU == int.MaxValue
                    ? [run.Low + (run.High - run.Low - width) / 2]
                    : Enumerable.Range(run.Low, Math.Min(run.High - width, maxU) - run.Low + 1))
                .Select(start => new ShapeDoor(new Point(start, row.Key), new Point(start + width, row.Key)))))
            .ToList();

        var lengths = Enumerable.Range(EntryLengthMin, EntryLengthMax - EntryLengthMin + 1);
        var cells = SolveAgainstBlock(block, openings, lengths.Select(length => EntryStrip(width, length)).ToArray(), [], true);
        if (cells is null)
        {
            return -1;
        }

        int foot = cells.Min(cell => cell.V);
        foreach (var room in _rooms)
        {
            room.Cells = room.Cells.Select(cell => new Cell(cell.U, cell.V - foot)).ToHashSet();
        }

        int entry = Add(DepotRole.Entry, 0, cells.Select(cell => cell with { V = cell.V - foot }).ToHashSet());
        doors.Add((entry, first));
        return entry;
    }

    private int Outside(int host, int w, int h, HashSet<Cell> banned, DepotRole role)
    {
        var block = Solid(host);
        var hostCells = _rooms[host].Cells;

        var openings = hostCells
            .SelectMany(cell => new[]
            {
                IsOpen(cell, 0, -1) ? new ShapeDoor(new Point(cell.U, cell.V), new Point(cell.U + 3, cell.V)) : default,
                IsOpen(cell, 0, 1) ? new ShapeDoor(new Point(cell.U, cell.V + 1), new Point(cell.U + 3, cell.V + 1)) : default,
                IsOpen(cell, -1, 0) ? new ShapeDoor(new Point(cell.U, cell.V), new Point(cell.U, cell.V + 3)) : default,
                IsOpen(cell, 1, 0) ? new ShapeDoor(new Point(cell.U + 1, cell.V), new Point(cell.U + 1, cell.V + 3)) : default
            })
            .Where(door => door != default)
            .ToList();

        var shapes = new[] { (w, h), (h, w) }
            .Distinct()
            .Select(size => new RoomShape(RoomShape.Rectangle(size.Item1, size.Item2), SolverDoors, [ShapeTurn.Identity]))
            .ToList();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var cells = SolveAgainstBlock(block, openings, shapes, banned.Concat(Taken()).Where(cell => !block.Contains(cell)).ToHashSet(), false);
            if (cells is not null && !HasSlit(cells, block))
            {
                return Add(role, -1, cells);
            }
        }

        return -1;

        bool IsOpen(Cell cell, int du, int dv) => Enumerable.Range(0, 3)
            .All(k => hostCells.Contains(new Cell(cell.U + k * dv * dv, cell.V + k * du * du)) && !block.Contains(new Cell(cell.U + k * dv * dv + du, cell.V + k * du * du + dv)));
    }

    private HashSet<Cell> Solid(int host) => Taken().GetPieces().First(part => part.Contains(_rooms[host].Cells.First())).GetFilled();

    private HashSet<Cell>? SolveAgainstBlock(HashSet<Cell> block, IReadOnlyList<ShapeDoor> openings, IReadOnlyList<RoomShape> shapes, HashSet<Cell> banned, bool isBeyond) =>
        _solver.SolveAgainst(block, openings, shapes, banned, isBeyond, 2);

    private static bool HasSlit(HashSet<Cell> cells, HashSet<Cell> block) => cells
        .SelectMany(cell => Steps4.Select(step => (Next: new Cell(cell.U + step.U, cell.V + step.V), Beyond: new Cell(cell.U + 2 * step.U, cell.V + 2 * step.V))))
        .Any(pair => !cells.Contains(pair.Next) && !block.Contains(pair.Next) && block.Contains(pair.Beyond));

    private bool IsWalkable(List<int> route) => route.Zip(route.Skip(1)).All(pair => _rooms[pair.First].Cells.GetLongestWall(_rooms[pair.Second].Cells).Length >= 3);

    private static void AddRun(List<int> route, List<(int, int)> doors)
    {
        for (int index = 1; index < route.Count; index++)
        {
            doors.Add((route[index - 1], route[index]));
        }
    }

    private int Islands(List<int> depots, int count, List<(int, int)> doors)
    {
        int made = 0;
        foreach (int depot in depots.OrderByDescending(id => _rooms[id].Cells.Count).Take(count))
        {
            var cells = _rooms[depot].Cells;
            var spots = cells
                .Where(cell => Enumerable.Range(-3, 10).All(i => Enumerable.Range(-3, 10).All(j => cells.Contains(new Cell(cell.U + i, cell.V + j)))))
                .ToList();

            if (spots.Count == 0)
            {
                continue;
            }

            var spot = _random.Choice(spots);
            var hole = Box(spot.U, spot.V, 4, 4);
            cells.ExceptWith(hole);
            int island = Add(DepotRole.Island, -1, hole);
            doors.Add((depot, island));
            made++;
        }

        return made;
    }

    private void Gaps(List<int> run, List<(int, int)> doors, List<(int, int)> jammed, List<(int, int)> shortcuts)
    {
        var needed = doors.Concat(jammed).Concat(shortcuts).ToList();
        for (int index = 1; index < run.Count; index++)
        {
            var (a, b) = (_rooms[run[index - 1]], _rooms[run[index]]);
            if (a.Role is not (DepotRole.Run or DepotRole.Passage) || b.Role is not (DepotRole.Run or DepotRole.Passage) || a.Section != b.Section)
            {
                continue;
            }

            // Run a throat's gap the whole wall between two depots on the route, its door at one end, cutting it from whichever of the two keeps no
            // part under four wide
            foreach (var (from, to) in _random.Shuffled([(a, b), (b, a)]).Where(pair => pair.Item1.Role == DepotRole.Run))
            {
                var wall = from.Cells.GetLongestWall(to.Cells);
                if (wall.Length < 7)
                {
                    break;
                }

                int width = _random.Inclusive(3, 4);
                int at = _random.Next() < 0.5 ? wall.Low : wall.High - width;
                if (Cut(from, to, wall, at, width, 3, needed))
                {
                    break;
                }
            }
        }

        foreach ((int hostId, int smallId) in doors.Where(pair => _rooms[pair.Item2].Role is DepotRole.Breaker or DepotRole.Charges or DepotRole.Nook or DepotRole.Office
            && _rooms[pair.Item2].Section < 0 && !IsCrossing(pair.Item2)))
        {
            if (_random.Next() > 0.85)
            {
                continue;
            }

            var small = _rooms[smallId];
            var wall = small.Cells.GetLongestWall(_rooms[hostId].Cells);
            if (wall.Length < 7)
            {
                continue;
            }

            Cut(small, _rooms[hostId], wall, (wall.Low + wall.High) / 2 - 1, 3, 2, needed, 2);
        }
    }

    // Keeps a cut only when its gap stays on the straight wall and leaves no short gap piece, no new narrow part and a door-wide wall for every
    // door
    private bool Cut(DepotRoom from, DepotRoom to, WallRun wall, int at, int width, int piece, List<(int, int)> needed, int depth = 1)
    {
        var face = from.Cells
            .Where(cell => Across(cell) == wall.Line && Along(cell) >= wall.Low && Along(cell) < wall.High
                && Steps4.Any(step => to.Cells.Contains(new Cell(cell.U + step.U, cell.V + step.V))))
            .ToHashSet();

        var neck = face.Where(cell => Along(cell) >= at && Along(cell) < at + width).ToHashSet();
        if (neck.Count != width)
        {
            return false;
        }

        var cut = face.Where(cell => !neck.Contains(cell)).ToHashSet();
        // Stand the room further back on a deeper cut, its neck a stem as long as the gap is deep
        var inward = Steps4.First(step => face.Any(cell => to.Cells.Contains(new Cell(cell.U + step.U, cell.V + step.V))));
        for (int row = 1; row < depth; row++)
        {
            cut.UnionWith(face.Where(cut.Contains).Select(cell => new Cell(cell.U - row * inward.U, cell.V - row * inward.V)).Where(from.Cells.Contains).ToList());
            neck.UnionWith(face.Where(neck.Contains).Select(cell => new Cell(cell.U - row * inward.U, cell.V - row * inward.V)).Where(from.Cells.Contains).ToList());
        }

        if (cut.GetPieces().Any(part => part.Count < piece))
        {
            return false;
        }

        var rest = from.Cells.Where(cell => !cut.Contains(cell)).ToHashSet();
        if (cut.Count == 0 || from.Neck is not null || !rest.IsConnected() || rest.GetNarrow(4).Except(neck).Except(from.Cells.GetNarrow(4)).Any()
            || rest.GetNarrow(3).Except(neck).Any())
        {
            return false;
        }

        var before = from.Cells;
        from.Cells = rest;
        if (needed.Any(pair => _rooms[pair.Item1].Cells.GetLongestWall(_rooms[pair.Item2].Cells).Length < 3))
        {
            from.Cells = before;
            return false;
        }

        from.Neck = neck.GetBounds();
        return true;
        
        int Along(Cell cell) => wall.AlongU ? cell.U : cell.V;
        
        int Across(Cell cell) => wall.AlongU ? cell.V : cell.U;
    }

    private bool IsCrossing(int id) => _rooms[id].Role == DepotRole.Office && _rooms.Any(room => room.Role == DepotRole.Link);

    private DepotWingLayout? FinishSections(SectionPlan a, List<int> run, List<(int, int)> doors, List<(int, int)> jammed, List<(int, int)> shortcuts, HashSet<Cell> court,
        int entry, int left)
    {
        var depots = run.Skip(1).Where(room => _rooms[room].Role == DepotRole.Run).ToList();
        var banned = new HashSet<Cell>(court);
        banned.UnionWith(_rooms[entry].Cells.GetSurroundings(6));
        var sectionB = _rooms.Where(room => room.Section == 1).Select(room => room.Id).ToList();
        var hosts = sectionB.Count > 0 ? sectionB : depots;
        return Furnish(run, doors, jammed, shortcuts, banned, left, depots, 1, hosts.Concat(depots).Distinct().ToArray(), true, -1, a);
    }

    private DepotWingLayout? Furnish(List<int> run, List<(int, int)> doors, List<(int, int)> jammed, List<(int, int)> shortcuts, HashSet<Cell> banned,
        int left, List<int> islandHosts, int keptBack, IReadOnlyList<int> nookHosts, bool hasCharges, int dock = -1, SectionPlan? section = null)
    {
        int last = run[^1];
        int breaker = Outside(last, 7, 6, banned, DepotRole.Breaker);
        if (breaker < 0)
        {
            return null;
        }

        doors.Add((last, breaker));
        run.Add(breaker);

        if (section is not null && section.Passage >= 0 && left > 0)
        {
            int store = Outside(section.Passage, 5, 5, banned, DepotRole.Site);
            if (store >= 0)
            {
                doors.Add((section.Passage, store));
                left--;
            }
        }

        left -= Islands(islandHosts, Math.Min(2, Math.Max(0, left - keptBack)), doors);
        for (int count = dock < 0 ? 0 : Math.Max(0, Math.Min(3, left)); count > 0; count--)
        {
            int office = Outside(dock, _random.Inclusive(5, 6), _random.Inclusive(5, 7), banned, DepotRole.Office);
            if (office >= 0)
            {
                doors.Add((dock, office));
                left--;
            }
        }

        foreach (int host in _random.Shuffled(nookHosts))
        {
            if (left <= 0)
            {
                break;
            }

            bool charges = hasCharges && _rooms.All(room => room.Role != DepotRole.Charges);
            int nook = Outside(host, _random.Inclusive(5, 8), _random.Inclusive(5, 7), banned, charges ? DepotRole.Charges : DepotRole.Nook);
            if (nook >= 0)
            {
                doors.Add((host, nook));
                left--;
            }
        }

        return Finish(run, doors, jammed, shortcuts);
    }

    private DepotWingLayout Finish(List<int> run, List<(int, int)> doors, List<(int, int)> jammed, List<(int, int)> shortcuts)
    {
        Gaps(run, doors, jammed, shortcuts);
        return new DepotWingLayout
        {
            Kind = _kind,
            Rooms = _rooms.ToArray(),
            Run = run,
            Doors = doors.Distinct().ToArray(),
            Jammed = jammed,
            Shortcuts = shortcuts
        };
    }

    private HashSet<Cell> Envelope(int w, int h, Side? flat, EnvelopeKind kind)
    {
        var away = Corners(flat);
        if (kind == EnvelopeKind.L)
        {
            (int cu, int cv) = _random.Choice(away);
            int a = _random.Inclusive(w * 38 / 100, w / 2);
            int b = _random.Inclusive(h * 38 / 100, h / 2);
            var cells = Box(0, 0, w, h);
            cells.ExceptWith(Box(cu == 0 ? 0 : w - a, cv == 0 ? 0 : h - b, a, b));
            return cells;
        }

        if (kind == EnvelopeKind.T)
        {
            int a = _random.Inclusive(w * 3 / 10, w * 42 / 100);
            int b = _random.Inclusive(h / 4, h / 3);
            var cells = Box(0, 0, w, h);
            foreach ((int cu, int cv) in away)
            {
                cells.ExceptWith(Box(cu == 0 ? 0 : w - a, cv == 0 ? 0 : h - b, a, b));
            }

            return cells;
        }

        return Corners(w, h, flat, _random.Choice([0, 1, 1, 2]), shallowOnly: false);
    }

    private HashSet<Cell> Corners(int w, int h, Side? flat, int count, bool shallowOnly)
    {
        var cells = Box(0, 0, w, h);
        foreach ((int cu, int cv) in _random.Shuffled(Corners(flat)).Take(count))
        {
            (int a, int b) = shallowOnly || _random.Next() < 0.5 ? (_random.Inclusive(w / 4, w / 2), _random.Inclusive(3, 5)) : (_random.Inclusive(4, 7), _random.Inclusive(4, 7));
            if (shallowOnly)
            {
                b = 3;
            }

            cells.ExceptWith(Box(cu == 0 ? 0 : w - a, cv == 0 ? 0 : h - b, a, b));
        }

        return cells;
    }

    private static (int U, int V)[] Corners(Side? flat) =>
        new[] { (0, 0), (0, 1), (1, 0), (1, 1) }
            .Where(corner => !(flat == Side.East && corner.Item1 == 1) && !(flat == Side.West && corner.Item1 == 0) && !(flat == Side.North && corner.Item2 == 0))
            .ToArray();

    private List<HashSet<Cell>>? Split(HashSet<Cell> region, bool strip)
    {
        var box = region.GetBounds();
        (int u0, int v0, int u1, int v1) = (box.U, box.V, box.U + box.W - 1, box.V + box.H - 1);
        bool vertical = u1 - u0 >= v1 - v0;
        if (_random.Next() < 0.2)
        {
            vertical = !vertical;
        }

        var cells = region.Select(T).ToHashSet();
        var turned = cells.GetBounds();
        (int a0, int b0, int a1, int b1) = (turned.U, turned.V, turned.U + turned.W - 1, turned.V + turned.H - 1);
        if (a1 - a0 < 14)
        {
            return null;
        }

        int a = _random.Inclusive(a0 + 7, a1 - 6);
        var steps = new List<(int At, int Shift)>();

        if (b1 - b0 >= 12 && _random.Next() < 0.85)
        {
            int first = Shift(a, 7);
            if (first != 0)
            {
                steps.Add((_random.Inclusive(b0 + 5, b1 - 5), first));
            }

            if (steps.Count == 1 && b1 - b0 >= 22 && _random.Next() < 0.35)
            {
                int second = _random.Inclusive(b0 + 5, b1 - 5);
                int shift = Shift(a + first, 6);
                if (Math.Abs(second - steps[0].At) >= 6 && shift != 0)
                {
                    steps.Add((second, shift));
                }
            }
        }

        int width = strip ? _random.Inclusive(5, 6) : 0;
        int span = strip ? Math.Min(b1 - b0 + 1, _random.Inclusive(8, 12)) : 0;
        int from = _random.Next() < 0.5 ? b0 : b1 - span + 1;
        var low = new HashSet<Cell>();
        var middle = new HashSet<Cell>();
        var high = new HashSet<Cell>();
        foreach (var cell in cells)
        {
            int cut = a;
            foreach ((int at, int shift) in steps)
            {
                cut += cell.V >= at ? shift : 0;
            }

            (cell.U < cut ? low : cell.U < cut + width && cell.V >= from && cell.V < from + span ? middle : high).Add(cell);
        }

        var parts = strip ? new List<HashSet<Cell>> { low, high, middle } : [low, high];
        if (Math.Min(low.Count, high.Count) < SmallestDepot || parts.Any(part => !part.IsThick(5) || !part.IsConnected()))
        {
            return null;
        }

        return parts.Select(part => part.Select(T).ToHashSet()).ToList();

        // Keeps a step five units or more from the region's sides
        int Shift(int at, int most)
        {
            int toward = _random.Next() < 0.5 ? -1 : 1;
            int room = toward < 0 ? at - (a0 + 5) : a1 - 5 - (strip ? 6 : 0) - at;
            return room >= 4 ? toward * _random.Inclusive(4, Math.Min(most, room)) : 0;
        }

        Cell T(Cell cell) => vertical ? cell : new Cell(cell.V, cell.U);
    }

    private (List<HashSet<Cell>> Regions, HashSet<Cell>? Strip)? Partition(HashSet<Cell> envelope, int count, bool passage)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var regions = new List<HashSet<Cell>> { envelope };
            HashSet<Cell>? strip = null;
            bool ok = true;
            while (regions.Count < count)
            {
                bool wantStrip = passage && strip is null && regions.Count == count - 1;
                List<HashSet<Cell>>? parts = null;
                HashSet<Cell>? source = null;
                foreach (var region in wantStrip ? regions.OrderByDescending(region => region.Count).ToList() : [regions.MaxBy(region => region.Count)!])
                {
                    for (int tries = 0; tries < 30 && parts is null; tries++)
                    {
                        parts = Split(region, wantStrip);
                    }

                    if (parts is not null)
                    {
                        source = region;
                        break;
                    }
                }

                if (parts is null || source is null)
                {
                    ok = false;
                    break;
                }

                regions.Remove(source);
                regions.Add(parts[0]);
                regions.Add(parts[1]);
                if (wantStrip)
                {
                    strip = parts[2];
                }
            }

            if (ok && regions.All(region => region.Count <= LargestDepot))
            {
                return (regions, strip);
            }
        }

        return null;
    }

    private static List<HashSet<int>> Adjacency(List<HashSet<Cell>> regions) => regions
        .Select((region, i) => Enumerable.Range(0, regions.Count).Where(j => j != i && region.GetLongestWall(regions[j]).Length >= 3).ToHashSet())
        .ToList();

    private static List<List<int>> Paths(List<HashSet<int>> adjacency, int count, Func<int, bool> isStart, Func<int, bool> isEnd, int inner)
    {
        var found = new List<List<int>>();
        var stack = new Stack<List<int>>();
        foreach (int start in Enumerable.Range(0, count).Where(isStart))
        {
            stack.Push([start]);
        }

        while (stack.Count > 0 && found.Count < 400)
        {
            var path = stack.Pop();
            if (path.Count == count)
            {
                if (isEnd(path[^1]) && (inner < 0 || (path.IndexOf(inner) > 0 && path[^1] != inner)))
                {
                    found.Add(path);
                }

                continue;
            }

            foreach (int next in adjacency[path[^1]].Where(next => !path.Contains(next)))
            {
                stack.Push(path.Append(next).ToList());
            }
        }

        return found;
    }

    private int Add(DepotRole role, int section, HashSet<Cell> cells)
    {
        var room = new DepotRoom { Id = _rooms.Count, Role = role, Section = section, Cells = cells };
        _rooms.Add(room);
        return room.Id;
    }

    private HashSet<Cell> Taken() => _rooms.SelectMany(room => room.Cells).ToHashSet();

    private static HashSet<Cell> Box(int u, int v, int w, int h) => new UnitRect(u, v, w, h).ToCells();

    private enum EnvelopeKind
    {
        Plain,
        L,
        T
    }

    private sealed class SectionPlan
    {
        public required List<int> Route { get; init; }

        public required List<(int, int)> Jammed { get; init; }

        public required int Passage { get; init; }
    }
}

internal enum DepotRole
{
    Entry,
    Run,
    Passage,
    Breaker,
    Charges,
    Site,
    Link,
    Office,
    Nook,
    Island,
    Corridor
}

internal sealed class DepotRoom
{
    public required int Id { get; init; }

    public required DepotRole Role { get; init; }

    public required int Section { get; init; }

    public required HashSet<Cell> Cells { get; set; }

    public UnitRect? Neck { get; set; }
}

internal sealed class DepotWingLayout
{
    public required LabWing Kind { get; init; }

    public required IReadOnlyList<DepotRoom> Rooms { get; init; }

    public required IReadOnlyList<int> Run { get; init; }

    public required IReadOnlyList<(int A, int B)> Doors { get; init; }

    public required IReadOnlyList<(int A, int B)> Jammed { get; init; }

    public required IReadOnlyList<(int A, int B)> Shortcuts { get; init; }
}
