using ASToolkit.Core.Levels;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents a gameplay element and where it sits on the tour.
/// </summary>
public sealed class RouteElement
{
    /// <summary>
    /// Gets or sets the element kind.
    /// </summary>
    public required ElementKind Kind { get; set; }

    /// <summary>
    /// Gets the object ID.
    /// </summary>
    public required KnownObject Id { get; init; }

    /// <summary>
    /// Gets the rounded x position.
    /// </summary>
    public required int X { get; init; }

    /// <summary>
    /// Gets the rounded y position.
    /// </summary>
    public required int Y { get; init; }

    /// <summary>
    /// Gets the reachable cell the element is snapped to.
    /// </summary>
    public required Cell Cell { get; init; }

    /// <summary>
    /// Gets a value indicating whether the element stands more than 30 pixels off the walkable floor.
    /// </summary>
    public required bool Hidden { get; init; }

    /// <summary>
    /// Gets the pickup category, or the category of a crate's content.
    /// </summary>
    public ItemCategory? Category { get; init; }

    /// <summary>
    /// Gets the pickup inside a crate, or 0.
    /// </summary>
    public KnownObject CrateItem { get; init; }

    /// <summary>
    /// Gets the program of a spawner.
    /// </summary>
    public SpawnerProgram? Program { get; init; }

    /// <summary>
    /// Gets or sets the share of the tour walked when the player first gets near the element.
    /// </summary>
    public float Progress { get; set; }

    /// <summary>
    /// Gets or sets the walking distance from the tour, in pixels.
    /// </summary>
    public int BranchDepth { get; set; }

    /// <summary>
    /// Gets or sets the walking distance from the start, in pixels.
    /// </summary>
    public int Distance { get; set; }

    /// <summary>
    /// Gets or sets the progress of a spawner's trigger, or null.
    /// </summary>
    public float? TriggerProgress { get; set; }

    /// <summary>
    /// Gets or sets the branch depth of a spawner's trigger, or null.
    /// </summary>
    public int? TriggerBranchDepth { get; set; }

    /// <summary>
    /// Gets or sets the straight distance from a spawner to its trigger, or null.
    /// </summary>
    public int? TriggerDistance { get; set; }
}
