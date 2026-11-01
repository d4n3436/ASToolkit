using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents how the objects of one ID stand by other objects and walls in the original levels of a theme.
/// </summary>
public sealed class ObjectRelations
{
    /// <summary>
    /// Gets the object ID of the relations.
    /// </summary>
    public required KnownObject Id { get; init; }

    /// <summary>
    /// Gets the theme of the levels the relations come from.
    /// </summary>
    public required LevelTheme Theme { get; init; }

    /// <summary>
    /// Gets the number of objects the relations were measured on.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>
    /// Gets the share of the objects with another object of the same ID within reach.
    /// </summary>
    public required float GroupShare { get; init; }

    /// <summary>
    /// Gets the share of the objects against a wall.
    /// </summary>
    public required float WallShare { get; init; }

    /// <summary>
    /// Gets the share of random floor points against a wall in the same levels.
    /// </summary>
    public required float RandomWallShare { get; init; }

    /// <summary>
    /// Gets the anchor objects of the relations, the most common first.
    /// </summary>
    public required IReadOnlyList<ObjectAnchor> Anchors { get; init; }
}
