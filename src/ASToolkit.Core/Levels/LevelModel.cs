using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents a level reduced to floor cells, wall edges and dressing on its wall set's lattice.
/// </summary>
public sealed class LevelModel
{
    /// <summary>
    /// Gets the level name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the wall set of the level.
    /// </summary>
    public required WallSet WallSet { get; init; }

    /// <summary>
    /// Gets the lattice of the cells.
    /// </summary>
    public required Lattice Lattice { get; init; }

    /// <summary>
    /// Gets the floor cells.
    /// </summary>
    public required IReadOnlySet<Cell> Cells { get; init; }

    /// <summary>
    /// Gets the wall edges by kind.
    /// </summary>
    public required IReadOnlyDictionary<EdgeKey, EdgeKind> Edges { get; init; }

    /// <summary>
    /// Gets the dressing objects.
    /// </summary>
    public required IReadOnlyList<DecorObject> Objects { get; init; }

    /// <summary>
    /// Gets every placed object of the map.
    /// </summary>
    public required IReadOnlyList<MergedMapSprite> Sprites { get; init; }
}
