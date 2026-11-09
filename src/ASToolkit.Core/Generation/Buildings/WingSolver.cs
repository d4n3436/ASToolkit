using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

internal sealed class WingSolver
{
    public const int Anchor = 0;
    private const int FirstCorridor = 1000;

    public static readonly DoorRule Doors = new(3, 1);
    public static readonly DoorRule CornerDoors = new(3, 0);

    private readonly bool _isBeyond;
    private readonly List<int> _rooms = [];
    private readonly Dictionary<int, (DoorSides? SideA, DoorSides? SideB)> _joinSides = [];
    private readonly Dictionary<int, IReadOnlyList<RoomShape>> _templates = [];
    private readonly List<(int A, int B, int[] Throat)> _joins = [];
    private readonly List<(int A, int B, int Length)> _touches = [];
    private readonly List<(int A, int B)> _mayTouch = [];

    // Stands every room past the base room's door end, or only clear of the obstacles
    public WingSolver(bool isBeyond)
    {
        _isBeyond = isBeyond;
    }

    public void SetTemplates(int room, IReadOnlyList<RoomShape> templates)
    {
        if (!_templates.ContainsKey(room))
        {
            _rooms.Add(room);
        }

        _templates[room] = templates;
    }

    // Joins two rooms through a door, or through a throat of one of the given lengths owned by the second
    public void Join(int a, int b, int[] throatLengths) => _joins.Add((a, b, throatLengths));

    // Opens a join's door only on the given sides of each room's templates, either side free when null
    public void SetSides(int a, int b, DoorSides? sidesOfA, DoorSides? sidesOfB)
    {
        int join = FindJoin(a, b);
        if (join < 0)
        {
            throw new InvalidOperationException($"Cannot set the door sides of rooms {a} and {b} without a join.");
        }

        _joinSides[join] = (sidesOfA, sidesOfB);
    }

    private int FindJoin(int a, int b) => _joins.FindLastIndex(join => join.A == a && join.B == b);

    public void Touch(int a, int b, int length = 0) => _touches.Add((a, b, length));

    public void MayTouch(int a, int b) => _mayTouch.Add((a, b));

    public Dictionary<int, WingPlace> Lay(UnitRect baseRect, IReadOnlyList<UnitRect> obstacles, int seed)
    {
        var problem = Describe(baseRect, out var throatOwners);
        foreach (var rect in obstacles)
        {
            problem.Obstacles.Add((Anchor, [new UnitRect(rect.U - baseRect.U, rect.V - baseRect.V, rect.W, rect.H)]));
        }

        var layout = new LayoutSolver<int>(problem, seed).Solve();
        if (layout.Count == 0)
        {
            return [];
        }

        // Move the layout onto the base room, laid first, unturned, at the origin
        var rects = new Dictionary<int, UnitRect[]>();
        var throats = new Dictionary<int, UnitRect>();
        foreach (var placed in layout.Values.Where(room => room.Room != Anchor))
        {
            var cells = placed.Cells.Select(cell => new Cell(cell.U + baseRect.U, cell.V + baseRect.V)).ToHashSet();
            if (placed.IsCorridor)
            {
                throats[throatOwners[placed.Room]] = Rectangulate(cells)[0];
                continue;
            }

            rects[placed.Room] = Rectangulate(cells);
        }

        return rects.ToDictionary(entry => entry.Key, entry => new WingPlace(entry.Value, throats.TryGetValue(entry.Key, out var throat) ? throat : null));
    }

    // Returns a box shape, its doors flush with the corners when a side is too short to stand them off
    public static RoomShape Box(int width, int length) =>
        new(RoomShape.Rectangle(Math.Max(3, width), Math.Max(3, length)), Math.Min(width, length) < 5 ? CornerDoors : Doors, RoomShape.AllTurns);

    private LayoutProblem<int> Describe(UnitRect baseRect, out Dictionary<int, int> throatOwners)
    {
        var problem = new LayoutProblem<int> { MinimumDistance = 2, TouchLength = 2, BeyondMargin = 2 };

        // Keep the base room at its own size, its doors a unit off its corners or flush on a side under five units, opening only on its +u end
        var ends = DoorOffsets(baseRect.H)
            .SelectMany(at => new[] { new ShapeDoor(new Point(0, at), new Point(0, at + 3)), new ShapeDoor(new Point(baseRect.W, at), new Point(baseRect.W, at + 3)) });

        var sides = DoorOffsets(baseRect.W)
            .SelectMany(at => new[] { new ShapeDoor(new Point(at, 0), new Point(at + 3, 0)), new ShapeDoor(new Point(at, baseRect.H), new Point(at + 3, baseRect.H)) });

        problem.AddRoom(Anchor, [new RoomShape(RoomShape.Rectangle(baseRect.W, baseRect.H), ends.Concat(sides).ToArray(), [ShapeTurn.Identity])]);
        foreach (int room in _rooms)
        {
            problem.AddRoom(room, _templates[room]);
            (_isBeyond ? problem.BeyondPairs : problem.ClearPairs).Add((Anchor, room));
        }

        var throatTemplates = new Dictionary<int, RoomShape>();

        throatOwners = [];
        int corridor = FirstCorridor;
        for (int index = 0; index < _joins.Count; index++)
        {
            (int a, int b, int[] throat) = _joins[index];
            // Key a join's door sides against each room's graph neighbor, the corridor when the join runs through a throat
            int neighborOfA = throat.Length == 0 ? b : corridor;
            int neighborOfB = throat.Length == 0 ? a : corridor;
            var joinSides = _joinSides.GetValueOrDefault(index);
            if (a == Anchor)
            {
                problem.DoorSides[(Anchor, neighborOfA)] = joinSides.SideA ?? DoorSides.MaxX;
            }
            else if (joinSides.SideA is not null)
            {
                problem.DoorSides[(a, neighborOfA)] = joinSides.SideA.Value;
            }

            if (joinSides.SideB is not null)
            {
                problem.DoorSides[(b, neighborOfB)] = joinSides.SideB.Value;
            }

            if (throat.Length == 0)
            {
                problem.Join(a, b);
                continue;
            }

            problem.AddRoom(corridor, throat.Select(ThroatTemplate).ToArray(), isCorridor: true);
            if (!_isBeyond)
            {
                problem.ClearPairs.Add((Anchor, corridor));
            }

            throatOwners[corridor] = b;
            problem.Join(a, corridor);
            problem.Join(corridor, b);
            corridor++;
        }

        foreach ((int a, int b, int length) in _touches)
        {
            problem.TouchPairs.Add((a, b));
            if (length > 0)
            {
                problem.TouchLengths[(a, b)] = length;
            }
        }

        problem.MayTouchPairs.AddRange(_mayTouch);

        return problem;

        RoomShape ThroatTemplate(int length) => throatTemplates.TryGetValue(length, out var known) ? known : throatTemplates[length] =
            new RoomShape(RoomShape.Rectangle(3, length), [new ShapeDoor(new Point(0, 0), new Point(3, 0)), new ShapeDoor(new Point(0, length), new Point(3, length))], RoomShape.AllTurns);
    }

    // Returns the offsets a three-unit door may take along a side, a unit off the corners on sides of five units or more
    private static IEnumerable<int> DoorOffsets(int span) => span >= 5 ? Enumerable.Range(1, span - 4) : Enumerable.Range(0, Math.Max(0, span - 2));

    // Splits cells into rects, the largest first
    private static UnitRect[] Rectangulate(HashSet<Cell> cells)
    {
        var left = new HashSet<Cell>(cells);
        var rects = new List<UnitRect>();
        while (left.Count > 0)
        {
            var best = default(UnitRect);
            foreach (var cell in left)
            {
                int width = 0;
                while (left.Contains(cell with { U = cell.U + width }))
                {
                    width++;
                }

                for (int height = 1, span = width; span > 0; height++)
                {
                    int row = 0;
                    while (row < span && left.Contains(new Cell(cell.U + row, cell.V + height - 1)))
                    {
                        row++;
                    }

                    span = row;
                    if (span > 0 && span * height > best.Area)
                    {
                        best = new UnitRect(cell.U, cell.V, span, height);
                    }

                    if (span == 0)
                    {
                        break;
                    }
                }
            }

            rects.Add(best);
            left.ExceptWith(best.Units);
        }

        return rects.ToArray();
    }
}

// Holds a wing room in place, its rects the largest first and the throat it is entered through
internal readonly record struct WingPlace(UnitRect[] Rects, UnitRect? Throat);
