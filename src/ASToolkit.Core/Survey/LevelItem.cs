using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents an item of a level.
/// </summary>
public sealed class LevelItem
{
    /// <summary>
    /// Gets the item ID.
    /// </summary>
    public required KnownObject Id { get; init; }

    /// <summary>
    /// Gets the category, or null for an item outside every category.
    /// </summary>
    public ItemCategory? Category { get; init; }

    /// <summary>
    /// Gets the place the item lies in.
    /// </summary>
    public required int Place { get; init; }

    /// <summary>
    /// Gets how far along the route, in map pixels, the route passes the item.
    /// </summary>
    public required int Route { get; init; }

    /// <summary>
    /// Gets the walk from the route to the item in map pixels.
    /// </summary>
    public required int OffRoute { get; init; }

    /// <summary>
    /// Gets a value indicating whether the item lies in a crate.
    /// </summary>
    public required bool InCrate { get; init; }

    /// <summary>
    /// Gets the nearby fight before the item, or null.
    /// </summary>
    public int? After { get; init; }

    /// <summary>
    /// Gets the nearby fight after the item, or null.
    /// </summary>
    public int? Before { get; init; }

    /// <summary>
    /// Gets the position in map pixels.
    /// </summary>
    public required Point Position { get; init; }
}
