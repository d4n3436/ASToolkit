namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the edge between two neighboring cells. A 'v' edge (A, B) separates cells (A, B-1) and (A, B);
/// a 'u' edge (A, B) separates cells (A-1, B) and (A, B).
/// </summary>
/// <param name="Axis">The edge axis, 'u' or 'v'.</param>
/// <param name="A">The first coordinate.</param>
/// <param name="B">The second coordinate.</param>
public readonly record struct EdgeKey(char Axis, int A, int B)
{
    /// <summary>
    /// Gets the cells on both sides of the edge.
    /// </summary>
    public (Cell First, Cell Second) Cells => Axis == 'v' ? (new Cell(A, B - 1), new Cell(A, B)) : (new Cell(A - 1, B), new Cell(A, B));

    /// <summary>
    /// Returns the four cells next to a cell with the edge crossed to reach each.
    /// </summary>
    public static Neighborhood GetNeighbors(Cell c)
    {
        var neighbors = default(Neighborhood);
        neighbors[0] = (c with { U = c.U + 1 }, new EdgeKey('u', c.U + 1, c.V));
        neighbors[1] = (c with { U = c.U - 1 }, new EdgeKey('u', c.U, c.V));
        neighbors[2] = (c with { V = c.V + 1 }, new EdgeKey('v', c.U, c.V + 1));
        neighbors[3] = (c with { V = c.V - 1 }, new EdgeKey('v', c.U, c.V));
        return neighbors;
    }
}
