using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents how far a point of a room stands from the room's walls along the four lattice directions, in map pixels.
/// </summary>
/// <param name="MinusU">The distance toward lower u.</param>
/// <param name="PlusU">The distance toward higher u.</param>
/// <param name="MinusV">The distance toward lower v.</param>
/// <param name="PlusV">The distance toward higher v.</param>
public readonly record struct WallGaps(float MinusU, float PlusU, float MinusV, float PlusV)
{
    /// <summary>
    /// The distance beyond which a wall no longer counts.
    /// </summary>
    public const float Far = 200;

    /// <summary>
    /// Gets the distances in the order lower u, higher u, lower v, higher v.
    /// </summary>
    public float this[int direction] => direction switch { 0 => MinusU, 1 => PlusU, 2 => MinusV, _ => PlusV };

    /// <summary>
    /// Measures the distances from a map point to the walls or the edge of a room, each at most <see cref="Far"/>.
    /// </summary>
    /// <param name="lattice">The lattice of the cells.</param>
    /// <param name="cells">The cells of the room.</param>
    /// <param name="edges">The wall and door edges of the level.</param>
    /// <param name="x">The map x.</param>
    /// <param name="y">The map y.</param>
    public static WallGaps Measure(Lattice lattice, IReadOnlySet<Cell> cells, IReadOnlyDictionary<EdgeKey, char> edges, float x, float y)
    {
        (float u, float v) = lattice.ToLattice(x, y);
        var cell = new Cell((int)MathF.Floor(u), (int)MathF.Floor(v));
        float step = lattice.CellStep;
        return new WallGaps(
            Walk(cell, -1, 0, u - cell.U),
            Walk(cell, 1, 0, cell.U + 1 - u),
            Walk(cell, 0, -1, v - cell.V),
            Walk(cell, 0, 1, cell.V + 1 - v));

        float Walk(Cell from, int du, int dv, float first)
        {
            float distance = first * step;
            var at = from;
            while (distance < Far)
            {
                var next = new Cell(at.U + du, at.V + dv);
                var edge = du > 0 ? new EdgeKey('u', next.U, next.V) : du < 0 ? new EdgeKey('u', at.U, at.V) : dv > 0 ? new EdgeKey('v', next.U, next.V) : new EdgeKey('v', at.U, at.V);
                if (!cells.Contains(next) || edges.ContainsKey(edge))
                {
                    return distance;
                }

                distance += step;
                at = next;
            }

            return Far;
        }
    }
}
