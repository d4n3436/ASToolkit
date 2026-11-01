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
    /// Represents the wall side of each direction, in the order of the gaps.
    /// </summary>
    public static readonly WallSides[] Sides = [WallSides.LowerBack, WallSides.UpperBack, WallSides.UpperSlash, WallSides.LowerSlash];

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
    public static WallGaps Measure(Lattice lattice, IReadOnlySet<Cell> cells, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float x, float y)
    {
        (float u, float v) = lattice.ToLattice(x, y);
        float step = lattice.CellStep;
        int reach = (int)MathF.Ceiling(Far / step) + 1;
        return new WallGaps(Walk(WallSides.LowerBack), Walk(WallSides.UpperBack), Walk(WallSides.UpperSlash), Walk(WallSides.LowerSlash));

        float Walk(WallSides side)
        {
            var hit = edges.CastRay(u, v, side, cells.Contains, reach);
            return hit is null ? Far : Math.Min(hit.Value.Distance * step, Far);
        }
    }
}
