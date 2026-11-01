using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents the walkable grid a route is planned on, twice as fine as the level's cells.
/// </summary>
public sealed class RouteGrid
{
    /// <summary>
    /// Gets the fine lattice.
    /// </summary>
    public required Lattice Lattice { get; init; }

    /// <summary>
    /// Gets the walkable cells, without the cells covered by solid objects.
    /// </summary>
    public required HashSet<Cell> Cells { get; init; }

    /// <summary>
    /// Gets the edges by kind.
    /// </summary>
    public required Dictionary<EdgeKey, EdgeKind> Edges { get; init; }

    /// <summary>
    /// Gets the fence frames that hold an electric fence.
    /// </summary>
    public required HashSet<EdgeKey> FenceEdges { get; init; }

    /// <summary>
    /// Gets the cells removed because solid objects cover them.
    /// </summary>
    public required HashSet<Cell> BlockedCells { get; init; }
}
