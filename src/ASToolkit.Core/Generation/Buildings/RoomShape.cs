using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

internal sealed class RoomShape
{
    public static readonly IReadOnlyList<ShapeTurn> AllTurns = Enum.GetValues<ShapeTurn>();

    public RoomShape(HashSet<Cell> cells, IReadOnlyList<ShapeDoor> doors, IReadOnlyList<ShapeTurn> turns)
    {
        Cells = cells;
        Doors = doors;
        Turns = turns;
    }

    public RoomShape(HashSet<Cell> cells, DoorRule rule, IReadOnlyList<ShapeTurn> turns) : this(cells, rule.Place(cells), turns)
    {
    }

    public HashSet<Cell> Cells { get; }

    public IReadOnlyList<ShapeDoor> Doors { get; }

    public IReadOnlyList<ShapeTurn> Turns { get; }

    public int Width => Cells.Max(cell => cell.U) - Cells.Min(cell => cell.U) + 1;

    public int Height => Cells.Max(cell => cell.V) - Cells.Min(cell => cell.V) + 1;

    public bool IsBox => Cells.Count == Width * Height;

    public int LongestSide => Cell.Steps.Max(facing => Runs(Cells, facing).Max(run => run.High - run.Low));

    public static HashSet<Cell> Rectangle(int width, int height) => new UnitRect(0, 0, width, height).ToCells();

    public static HashSet<Cell> Polygon(params Point[] corners)
    {
        var cells = new HashSet<Cell>();
        for (int u = corners.Min(point => point.X); u < corners.Max(point => point.X); u++)
        {
            for (int v = corners.Min(point => point.Y); v < corners.Max(point => point.Y); v++)
            {
                bool inside = false;
                for (int index = 0; index < corners.Length; index++)
                {
                    var p = corners[index];
                    var q = corners[(index + 1) % corners.Length];
                    if (p.X == q.X && p.Y > v != q.Y > v && u < p.X)
                    {
                        inside = !inside;
                    }
                }

                if (inside)
                {
                    cells.Add(new Cell(u, v));
                }
            }
        }

        return cells;
    }

    // Returns the straight outline runs facing one way, each its wall line and the span along it
    internal static List<(int At, int Low, int High)> Runs(HashSet<Cell> cells, Cell facing)
    {
        var edges = cells
            .Where(cell => !cells.Contains(cell + facing))
            .Select(cell => facing.U != 0 ? (At: cell.U + Math.Max(0, facing.U), Along: cell.V) : (At: cell.V + Math.Max(0, facing.V), Along: cell.U));

        return edges
            .GroupBy(edge => edge.At)
            .SelectMany(line => line.Select(edge => edge.Along).GetRuns().Select(run => (line.Key, run.Low, run.High)))
            .ToList();
    }
}

internal readonly record struct ShapeDoor(Point From, Point To);

// Holds a rule for a shape's doors, every position of a door this long along each outline run at least the corner distance from its ends
internal readonly record struct DoorRule(int Length, int CornerDistance)
{
    public IReadOnlyList<ShapeDoor> Place(HashSet<Cell> cells)
    {
        var doors = new List<ShapeDoor>();
        foreach (var facing in Cell.Steps)
        {
            foreach ((int at, int low, int high) in RoomShape.Runs(cells, facing))
            {
                for (int start = low + CornerDistance; start + Length <= high - CornerDistance; start++)
                {
                    doors.Add(facing.U != 0
                        ? new ShapeDoor(new Point(at, start), new Point(at, start + Length))
                        : new ShapeDoor(new Point(start, at), new Point(start + Length, at)));
                }
            }
        }

        return doors;
    }
}

internal enum ShapeTurn
{
    Identity,
    Rotate90,
    Rotate180,
    Rotate270,
    MirrorX,
    MirrorY,
    Diagonal13,
    Diagonal24
}

// Specifies the sides of a shape, in the shape's own frame, a join may put its door on
[Flags]
internal enum DoorSides
{
    None = 0,
    MinX = 1,
    MaxX = 2,
    MinY = 4,
    MaxY = 8,
    All = MinX | MaxX | MinY | MaxY
}

internal static class ShapeTurnExtensions
{
    extension(ShapeTurn turn)
    {
        public Point Apply(Point point) => turn switch
        {
            ShapeTurn.Identity => point,
            ShapeTurn.Rotate90 => new Point(point.Y, -point.X),
            ShapeTurn.Rotate180 => new Point(-point.X, -point.Y),
            ShapeTurn.Rotate270 => new Point(-point.Y, point.X),
            ShapeTurn.MirrorX => point with { Y = -point.Y },
            ShapeTurn.MirrorY => point with { X = -point.X },
            ShapeTurn.Diagonal13 => new Point(point.Y, point.X),
            _ => new Point(-point.Y, -point.X)
        };

        // Turns a unit cell about the origin to the cell the turned square covers
        public Cell Apply(Cell cell)
        {
            var a = turn.Apply(new Point(cell.U, cell.V));
            var b = turn.Apply(new Point(cell.U + 1, cell.V + 1));
            return new Cell(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        }
    }
}
