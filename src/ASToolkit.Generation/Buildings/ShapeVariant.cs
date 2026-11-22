using System.Drawing;
using System.Runtime.CompilerServices;
using ASToolkit.Analysis.Levels;

namespace ASToolkit.Generation.Buildings;

internal readonly struct ShapeVariant
{
    private static readonly ConditionalWeakTable<RoomShape, ShapeVariant[]> TurnedVariants = new();

    private readonly UnitRect[] _originRects;
    private readonly DoorLine[] _originLines;
    private readonly DoorLine[] _originLinesAnySide;

    private ShapeVariant(ShapeTurn turn, HashSet<Cell> cells, DoorLine[] lines)
    {
        Turn = turn;
        Cells = cells;
        Rects = cells.ToRects();
        Lines = lines;
        var origin = new Point(-Rects.Min(rect => rect.U), -Rects.Min(rect => rect.V));
        _originRects = Rects.Select(rect => rect with { U = rect.U + origin.X, V = rect.V + origin.Y }).OrderBy(rect => rect.U).ThenBy(rect => rect.V).ThenBy(rect => rect.W).ThenBy(rect => rect.H).ToArray();
        _originLines = Lines.Select(line => line.Offset(origin)).Order().ToArray();
        _originLinesAnySide = _originLines.Select(line => line with { OwnFacing = 0 }).Order().ToArray();
    }

    public ShapeTurn Turn { get; }

    public HashSet<Cell> Cells { get; }

    public UnitRect[] Rects { get; }

    public DoorLine[] Lines { get; }

    public static ShapeVariant[] GetTurnedVariants(RoomShape shape) => TurnedVariants.GetValue(shape, turned => turned.Turns.Distinct().Select(turn => TurnShape(turned, turn)).ToArray());

    private static ShapeVariant TurnShape(RoomShape shape, ShapeTurn turn)
    {
        var cells = shape.Cells.Select(cell => turn.Apply(cell)).ToHashSet();
        var doors = new List<DoorLine>();
        foreach (var door in shape.Doors)
        {
            var p = turn.Apply(door.From);
            var q = turn.Apply(door.To);
            (int u0, int v0, int u1, int v1) = (Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y));
            int facing = GetOutwardFacing(cells, u0, v0, u1);
            int ownFacing = GetOutwardFacing(shape.Cells, Math.Min(door.From.X, door.To.X), Math.Min(door.From.Y, door.To.Y), Math.Max(door.From.X, door.To.X));
            int at = facing < 2 ? u0 : v0;
            int start = facing < 2 ? v0 : u0;
            doors.Add(new DoorLine(at, start, start, Math.Max(u1 - u0, v1 - v0), facing, ownFacing));
        }

        var lines = doors
            .GroupBy(door => (door.Facing, door.OwnFacing, door.Length, door.At))
            .SelectMany(group => group.Select(door => door.Low).GetRuns().Select(run => group.First() with { Low = run.Low, High = run.High - 1 }))
            .ToArray();

        return new ShapeVariant(turn, cells, lines);
    }

    private static int GetOutwardFacing(HashSet<Cell> cells, int u0, int v0, int u1) => u0 == u1
        ? cells.Contains(new Cell(u0 - 1, v0)) ? 0 : 1
        : cells.Contains(new Cell(u0, v0 - 1)) ? 2 : 3;

    public bool HasSameOutlineAndDoors(ShapeVariant other, bool comparesOwnSides) =>
        _originRects.AsSpan().SequenceEqual(other._originRects) &&
        (comparesOwnSides ? _originLines.AsSpan().SequenceEqual(other._originLines) : _originLinesAnySide.AsSpan().SequenceEqual(other._originLinesAnySide));
}
