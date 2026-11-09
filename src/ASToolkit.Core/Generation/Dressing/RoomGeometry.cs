using System.Drawing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Levels;
using static ASToolkit.Core.Generation.Dressing.FurnitureGrammar;

namespace ASToolkit.Core.Generation.Dressing;

internal sealed class RoomGeometry
{
    private readonly FineLayout _layout;

    public RoomGeometry(FineLayout layout, IEnumerable<Cell> cells, IReadOnlySet<Cell> reserved)
    {
        _layout = layout;
        Cells = cells.ToHashSet();
        Faces = Cells.ToDictionary(cell => cell, GetFaces);
        Kept = Cells.Where(reserved.Contains).ToHashSet();

        foreach (var cell in Cells)
        {
            foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (Cells.Contains(next) || !layout.Cells.ContainsKey(next) || IsClosed(edge))
                    continue;

                Kept.Add(cell);
                Openings.Add(cell);
                foreach (var (inside, _) in EdgeKey.GetNeighbors(cell))
                {
                    Openings.Add(inside);
                }
            }
        }

        var queue = new Queue<Cell>(Cells.Where(cell => Faces[cell] != 0));
        foreach (var cell in queue)
        {
            WallSteps[cell] = 0;
        }

        while (queue.TryDequeue(out var cell))
        {
            foreach (var (next, _) in EdgeKey.GetNeighbors(cell))
            {
                if (Cells.Contains(next) && WallSteps.TryAdd(next, WallSteps[cell] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        int minU = Cells.Min(cell => cell.U) - 1, maxU = Cells.Max(cell => cell.U) + 1, minV = Cells.Min(cell => cell.V) - 1, maxV = Cells.Max(cell => cell.V) + 1;
        var outside = new HashSet<Cell> { new(minU, minV) };
        var open = new Queue<Cell>(outside);
        while (open.TryDequeue(out var cell))
        {
            foreach (var (next, _) in EdgeKey.GetNeighbors(cell))
            {
                if (next.U >= minU && next.U <= maxU && next.V >= minV && next.V <= maxV && !Cells.Contains(next) && outside.Add(next))
                {
                    open.Enqueue(next);
                }
            }
        }

        Pillars = Enumerable.Range(minU, maxU - minU + 1)
            .SelectMany(u => Enumerable.Range(minV, maxV - minV + 1).Select(v => new Cell(u, v)))
            .Where(cell => !Cells.Contains(cell) && !outside.Contains(cell))
            .ToHashSet();

        Interior = Cells.Where(cell => Faces[cell] == 0 && new Cell[] { new(cell.U + 1, cell.V), new(cell.U - 1, cell.V), new(cell.U, cell.V + 1), new(cell.U, cell.V - 1) }.All(Cells.Contains)).ToArray();

        foreach (int face in new[] { PlusU, MinusV, MinusU, PlusV })
        {
            var run = new List<Cell>();
            foreach (var cell in Cells.Where(cell => (Faces[cell] & face) != 0).OrderBy(cell => Across(cell, face)).ThenBy(cell => Along(cell, face)))
            {
                if (run.Count > 0 && (Along(cell, face) != Along(run[^1], face) + 1 || Across(cell, face) != Across(run[^1], face)))
                {
                    Walls.Add(new WallLine(face, run.ToArray()));
                    run.Clear();
                }

                run.Add(cell);
            }

            if (run.Count > 0)
            {
                Walls.Add(new WallLine(face, run.ToArray()));
            }
        }
    }

    public HashSet<Cell> Cells { get; }

    public int HalfCells => Cells.Count * 4;

    public Dictionary<Cell, int> Faces { get; }

    public Dictionary<Cell, int> WallSteps { get; } = [];

    public Cell[] Interior { get; }

    // Openings are the cells of each doorway and the cells just inside it; kept cells must stay joined past the furniture
    public HashSet<Cell> Openings { get; } = [];

    public HashSet<Cell> Kept { get; }

    public HashSet<Cell> Pillars { get; }

    public static Point GetNormal(int face) => face switch
    {
        PlusU => new Point(1, 0),
        MinusV => new Point(0, -1),
        MinusU => new Point(-1, 0),
        _ => new Point(0, 1)
    };

    public static Cell GetBeyond(Cell cell, int face) => new(cell.U + GetNormal(face).X, cell.V + GetNormal(face).Y);

    public List<WallLine> Walls { get; } = [];

    public static int Along(Cell cell, int face) => face is PlusU or MinusU ? cell.V : cell.U;

    public static int Across(Cell cell, int face) => face is PlusU or MinusU ? cell.U : cell.V;

    public static PointF FromWall(Cell wall, int face, float distance, float along) => face switch
    {
        PlusU => new PointF(wall.U + 1 - distance, along),
        MinusV => new PointF(along, wall.V + distance),
        MinusU => new PointF(wall.U + distance, along),
        _ => new PointF(along, wall.V + 1 - distance)
    };

    // The map reads a cell back through its world position
    public Cell GetCell(PointF point)
    {
        var world = _layout.Lattice.ToWorld(point.X, point.Y);
        return _layout.Lattice.GetCell(world);
    }

    private bool IsClosed(EdgeKey edge) => _layout.Edges.TryGetValue(edge, out var kind) && kind.IsClosed;

    private int GetFaces(Cell cell)
    {
        int faces = 0;
        foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
        {
            faces |= !IsClosed(edge) ? 0 : next.U > cell.U ? PlusU : next.V < cell.V ? MinusV : next.U < cell.U ? MinusU : PlusV;
        }

        return faces;
    }
}

internal readonly record struct WallLine(int Face, Cell[] Cells);