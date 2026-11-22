namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the walls around lattice points and cells of a map of edges.
/// </summary>
public static class EdgeMapExtensions
{
    /// <summary>
    /// Represents the slack of comparisons between distances to walls, in cells.
    /// </summary>
    public const float WallSlack = 0.05f;

    private const float NearReach = 1.5f;
    private const int NearSearch = 2;
    private const int ClearanceSearch = 3;

    extension(IReadOnlyDictionary<EdgeKey, EdgeKind> edges)
    {
        /// <summary>
        /// Returns the map distance straight down the screen from a map point to the first wall that blocks walking, or the reach if there isn't a nearer wall.
        /// </summary>
        /// <param name="lattice">The lattice of the cells.</param>
        /// <param name="x">The x coordinate of the map point.</param>
        /// <param name="y">The y coordinate of the map point.</param>
        /// <param name="reach">The distance returned if there isn't a nearer wall.</param>
        /// <returns>The distance.</returns>
        public float MeasureDrop(Lattice lattice, float x, float y, float reach)
        {
            // Step between the cell boundaries the ray crosses, where u falls and v rises one unit each period
            (float u, float v) = lattice.ToLattice(x, y);
            float period = 2 * lattice.StepY;
            float toU = (u - MathF.Floor(u)) * period;
            float toV = (MathF.Floor(v) + 1 - v) * period;
            var cell = new Cell((int)MathF.Floor(u), (int)MathF.Floor(v));
            while (true)
            {
                int dropU = (int)MathF.Floor(toU) + 1;
                int dropV = (int)MathF.Floor(toV) + 1;
                int drop = Math.Min(dropU, dropV);
                if (drop >= reach)
                    return reach;

                var next = new Cell(dropU == drop ? cell.U - 1 : cell.U, dropV == drop ? cell.V + 1 : cell.V);
                bool blocked = dropU != dropV
                    ? IsBlocked(cell, next)
                    : (IsBlocked(cell, new Cell(next.U, cell.V)) || IsBlocked(new Cell(next.U, cell.V), next))
                      && (IsBlocked(cell, new Cell(cell.U, next.V)) || IsBlocked(new Cell(cell.U, next.V), next));

                if (blocked)
                    return drop;

                toU += dropU == drop ? period : 0;
                toV += dropV == drop ? period : 0;
                cell = next;
            }

            bool IsBlocked(Cell from, Cell to)
            {
                foreach (var (next, edge) in EdgeKey.GetNeighbors(from))
                {
                    if (next == to)
                        return edges.TryGetValue(edge, out var kind) && kind.BlocksWalking;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns the nearest edge showing a wall face within one and a half cells of a lattice point, with the side it stands on, or null if there isn't one.
        /// </summary>
        /// <param name="u">The u coordinate of the lattice point.</param>
        /// <param name="v">The v coordinate of the lattice point.</param>
        /// <returns>The side and the distance of the edge, or null if there isn't one.</returns>
        public (WallSides Side, float Distance)? FindNearestWall(float u, float v) =>
            FindNearest(edges, u, v, NearSearch, NearReach, static kind => kind.ShowsWall);

        /// <summary>
        /// Returns the nearest edge of any kind within one and a half cells of a lattice point, with the side it stands on, or null if there isn't one.
        /// </summary>
        /// <param name="u">The u coordinate of the lattice point.</param>
        /// <param name="v">The v coordinate of the lattice point.</param>
        /// <returns>The side and the distance of the edge, or null if there isn't one.</returns>
        public (WallSides Side, float Distance)? FindNearestEdge(float u, float v) =>
            FindNearest(edges, u, v, NearSearch, NearReach, static _ => true);

        /// <summary>
        /// Returns the distance in cells from a lattice point to the nearest edge other than the end of a double door within three cells,
        /// or infinity if there isn't one.
        /// </summary>
        /// <param name="u">The u coordinate of the lattice point.</param>
        /// <param name="v">The v coordinate of the lattice point.</param>
        /// <returns>The distance in cells.</returns>
        public float MeasureClearance(float u, float v) =>
            FindNearest(edges, u, v, ClearanceSearch, float.PositiveInfinity, static kind => kind != EdgeKind.DoubleDoorEnd)?.Distance ?? float.PositiveInfinity;

        /// <summary>
        /// Returns the sides of a cell that are walled, where a side counts as walled when its edge passes a test or the cell beyond it isn't floor.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="isFloor">Whether a cell is floor.</param>
        /// <param name="isWall">Whether an edge kind counts as a wall.</param>
        /// <returns>The walled sides.</returns>
        public WallSides GetWalledSides(Cell cell, Func<Cell, bool> isFloor, Func<EdgeKind, bool> isWall)
        {
            var sides = WallSides.None;
            var neighbors = EdgeKey.GetNeighbors(cell);
            for (int i = 0; i < 4; i++)
            {
                var (neighbor, edge) = neighbors[i];
                if (edges.TryGetValue(edge, out var kind) ? isWall(kind) : !isFloor(neighbor))
                {
                    sides |= NeighborSides[i];
                }
            }

            return sides;
        }

        /// <summary>
        /// Returns the distance in cells from a lattice point to the first edge or floor border on one side, with that edge and its kind
        /// (the kind is none at a bare border), or null if there isn't one within a number of cells.
        /// </summary>
        /// <param name="u">The u coordinate of the lattice point.</param>
        /// <param name="v">The v coordinate of the lattice point.</param>
        /// <param name="side">The side of the cell to cast toward.</param>
        /// <param name="isFloor">Whether a cell is floor.</param>
        /// <param name="maxCells">The farthest distance in cells to look.</param>
        /// <returns>The distance, the edge and its kind, or null if there isn't an edge or border within the given number of cells.</returns>
        public (float Distance, EdgeKey Edge, EdgeKind Kind)? CastRay(float u, float v, WallSides side, Func<Cell, bool> isFloor, int maxCells)
        {
            var at = new Cell((int)MathF.Floor(u), (int)MathF.Floor(v));
            (int du, int dv) = side switch
            {
                WallSides.UpperBack => (1, 0),
                WallSides.LowerBack => (-1, 0),
                WallSides.LowerSlash => (0, 1),
                _ => (0, -1)
            };

            float distance = du > 0 ? at.U + 1 - u : du < 0 ? u - at.U : dv > 0 ? at.V + 1 - v : v - at.V;
            for (int step = 0; step < maxCells; step++)
            {
                var next = new Cell(at.U + du, at.V + dv);
                var edge = du > 0 ? new EdgeKey('u', next.U, next.V) : du < 0 ? new EdgeKey('u', at.U, at.V) : dv > 0 ? new EdgeKey('v', next.U, next.V) : new EdgeKey('v', at.U, at.V);
                if (edges.TryGetValue(edge, out var kind))
                {
                    return (distance, edge, kind);
                }

                if (!isFloor(next))
                {
                    return (distance, edge, EdgeKind.None);
                }

                distance += 1;
                at = next;
            }

            return null;
        }
    }

    private static readonly WallSides[] NeighborSides = [WallSides.UpperBack, WallSides.LowerBack, WallSides.LowerSlash, WallSides.UpperSlash];

    private static (WallSides Side, float Distance)? FindNearest(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v, int search, float reach,
        Func<EdgeKind, bool> counts)
    {
        int cu = (int)MathF.Floor(u);
        int cv = (int)MathF.Floor(v);
        float best = reach;
        WallSides? side = null;
        for (int a = cu - search; a <= cu + search + 1; a++)
        {
            for (int b = cv - search; b <= cv + search + 1; b++)
            {
                // Skip edges at least the reach away along either axis
                if (MathF.Abs(u - a) < reach && b < v + reach && b + 1 > v - reach && edges.TryGetValue(new EdgeKey('u', a, b), out var uKind) && counts(uKind))
                {
                    float dv = Math.Max(0, Math.Max(b - v, v - (b + 1)));
                    float distance = MathF.Sqrt((u - a) * (u - a) + dv * dv);
                    if (distance < best)
                    {
                        (best, side) = (distance, u >= a ? WallSides.LowerBack : WallSides.UpperBack);
                    }
                }

                if (MathF.Abs(v - b) < reach && a < u + reach && a + 1 > u - reach && edges.TryGetValue(new EdgeKey('v', a, b), out var vKind) && counts(vKind))
                {
                    float du = Math.Max(0, Math.Max(a - u, u - (a + 1)));
                    float distance = MathF.Sqrt((v - b) * (v - b) + du * du);
                    if (distance < best)
                    {
                        (best, side) = (distance, v >= b ? WallSides.UpperSlash : WallSides.LowerSlash);
                    }
                }
            }
        }

        return side.HasValue ? (side.Value, best) : null;
    }
}
