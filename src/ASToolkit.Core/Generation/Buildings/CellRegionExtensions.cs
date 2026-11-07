using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the geometry of the cell regions building wings draw, such as their connected pieces, thickness, bounds, holes and the walls two
/// regions share.
/// </summary>
internal static class CellRegionExtensions
{
    private static readonly (int U, int V)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    extension(Cell cell)
    {
        public Cell[] GetAdjacent() =>
            [cell with { U = cell.U - 1 }, cell with { U = cell.U + 1 }, cell with { V = cell.V - 1 }, cell with { V = cell.V + 1 }];
    }

    extension(UnitRect rect)
    {
        public HashSet<Cell> ToCells()
        {
            var cells = new HashSet<Cell>();
            for (int u = rect.U; u < rect.U + rect.W; u++)
            {
                for (int v = rect.V; v < rect.V + rect.H; v++)
                {
                    cells.Add(new Cell(u, v));
                }
            }

            return cells;
        }
    }

    extension(IEnumerable<int> values)
    {
        public List<(int Low, int High)> GetRuns()
        {
            var runs = new List<(int Low, int High)>();
            foreach (int value in values.Distinct().Order())
            {
                if (runs.Count > 0 && runs[^1].High == value)
                {
                    runs[^1] = (runs[^1].Low, value + 1);
                }
                else
                {
                    runs.Add((value, value + 1));
                }
            }

            return runs;
        }
    }

    extension(HashSet<Cell> cells)
    {
        public UnitRect GetBounds()
        {
            (int u0, int v0, int u1, int v1) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                (u0, v0, u1, v1) = (Math.Min(u0, cell.U), Math.Min(v0, cell.V), Math.Max(u1, cell.U), Math.Max(v1, cell.V));
            }

            return new UnitRect(u0, v0, u1 - u0 + 1, v1 - v0 + 1);
        }

        public bool IsConnected() => cells.Count > 0 && Flood(cells, cells.First()).Count == cells.Count;

        public List<HashSet<Cell>> GetPieces()
        {
            var left = new HashSet<Cell>(cells);
            var pieces = new List<HashSet<Cell>>();
            while (left.Count > 0)
            {
                var piece = Flood(left, left.First());
                left.ExceptWith(piece);
                pieces.Add(piece);
            }

            return pieces;
        }

        public HashSet<Cell> GetFilled()
        {
            var box = cells.GetBounds();
            (int u0, int v0, int u1, int v1) = (box.U - 1, box.V - 1, box.U + box.W, box.V + box.H);
            var outside = new HashSet<Cell>();
            var stack = new Stack<Cell>([new Cell(u0, v0)]);
            while (stack.Count > 0)
            {
                var cell = stack.Pop();
                if (cell.U < u0 || cell.U > u1 || cell.V < v0 || cell.V > v1 || cells.Contains(cell) || !outside.Add(cell))
                {
                    continue;
                }

                foreach (var step in Steps)
                {
                    stack.Push(new Cell(cell.U + step.U, cell.V + step.V));
                }
            }

            return box.ToCells().Where(cell => !outside.Contains(cell)).ToHashSet();
        }

        // Returns whether every row and column run of the cells is at least the given length
        public bool IsThick(int least)
        {
            foreach (var cell in cells)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    (int du, int dv) = (1 - axis, axis);
                    if (cells.Contains(new Cell(cell.U - du, cell.V - dv)))
                    {
                        continue;
                    }

                    for (int k = 1; k < least; k++)
                    {
                        if (!cells.Contains(new Cell(cell.U + k * du, cell.V + k * dv)))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        // Returns the cells no k by k square inside the cells covers
        public HashSet<Cell> GetNarrow(int k)
        {
            var covered = new HashSet<Cell>();
            foreach (var cell in cells)
            {
                var square = new UnitRect(cell.U, cell.V, k, k).ToCells();
                if (square.IsSubsetOf(cells))
                {
                    covered.UnionWith(square);
                }
            }

            return cells.Where(cell => !covered.Contains(cell)).ToHashSet();
        }

        public int CountOneUnitSteps() => cells
            .SelectMany(cell => Steps
                .Where(step => !cells.Contains(new Cell(cell.U + step.U, cell.V + step.V)))
                .Select(step => (Key: step.U != 0 ? (true, cell.U + (step.U > 0 ? 1 : 0), step.U) : (false, cell.V + (step.V > 0 ? 1 : 0), step.V), At: step.U != 0 ? cell.V : cell.U)))
            .GroupBy(edge => edge.Key, edge => edge.At)
            .Sum(edge => edge.GetRuns().Count(run => run.High - run.Low == 1));

        // Returns every cell within the reach of a cell along both axes
        public HashSet<Cell> GetSurroundings(int reach) =>
            cells.SelectMany(cell => new UnitRect(cell.U - reach, cell.V - reach, 2 * reach + 1, 2 * reach + 1).ToCells()).ToHashSet();

        public WallRun GetLongestWall(HashSet<Cell> other) => Steps
            .SelectMany(step => cells
                .Where(cell => other.Contains(new Cell(cell.U + step.U, cell.V + step.V)))
                .GroupBy(cell => step.U != 0 ? cell.U : cell.V)
                .SelectMany(line => line.Select(cell => step.U != 0 ? cell.V : cell.U).GetRuns().Select(run => new WallRun(line.Key, run.Low, run.High, step.U == 0))))
            .Aggregate(new WallRun(0, 0, 0, false), (best, run) => run.Length > best.Length ? run : best);

        // Covers the cells with rectangles in row order, each as wide along u as it goes and then as tall as that width allows
        public UnitRect[] ToRects()
        {
            var left = new HashSet<Cell>(cells);
            var rects = new List<UnitRect>();
            foreach (var start in cells.OrderBy(cell => cell.V).ThenBy(cell => cell.U))
            {
                if (!left.Contains(start))
                {
                    continue;
                }

                int w = 1;
                while (left.Contains(new Cell(start.U + w, start.V)))
                {
                    w++;
                }

                int h = 1;
                while (Enumerable.Range(start.U, w).All(u => left.Contains(new Cell(u, start.V + h))))
                {
                    h++;
                }

                var rect = new UnitRect(start.U, start.V, w, h);
                left.ExceptWith(rect.Units);
                rects.Add(rect);
            }

            return rects.ToArray();
        }
    }

    private static HashSet<Cell> Flood(HashSet<Cell> cells, Cell start)
    {
        var reached = new HashSet<Cell> { start };
        var todo = new Stack<Cell>([start]);
        while (todo.Count > 0)
        {
            var cell = todo.Pop();
            foreach ((int du, int dv) in Steps)
            {
                var next = new Cell(cell.U + du, cell.V + dv);
                if (cells.Contains(next) && reached.Add(next))
                {
                    todo.Push(next);
                }
            }
        }

        return reached;
    }
}

internal readonly record struct WallRun(int Line, int Low, int High, bool AlongU)
{
    public int Length => High - Low;
}
