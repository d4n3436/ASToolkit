using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

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

    /// <summary>
    /// Gets every placed object of the map in the order the map stores them.
    /// </summary>
    public required IReadOnlyList<MergedMapSprite> StoredSprites { get; init; }

    /// <summary>
    /// Gets the width and height of the map from its header, or an empty size if the model doesn't have one.
    /// </summary>
    public SizeF MapSize { get; init; }

    /// <summary>
    /// Gets the top-left corner of the screen on the map when the level starts, from its header.
    /// </summary>
    public Vector2 Camera { get; init; }

    /// <summary>
    /// Gets the environment the map starts with.
    /// </summary>
    public MapEnvironment Environment { get; init; }

    /// <summary>
    /// Gets the gamma of the ambient light the map starts with.
    /// </summary>
    public Gamma Ambient { get; init; }

    /// <summary>
    /// Gets the groups of the map, each a list of sprite handles.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> Groups { get; init; } = [];

    /// <summary>
    /// Gets the version of the map file's format.
    /// </summary>
    public int Version { get; init; }
}
