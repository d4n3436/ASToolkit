using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents an area of a layout.
/// </summary>
public sealed class PlanRegion
{
    /// <summary>
    /// Gets the region ID, its index in the plan.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the role of the region.
    /// </summary>
    public required RegionKind Kind { get; init; }

    /// <summary>
    /// Gets the rectangles the region covers.
    /// </summary>
    public List<UnitRect> Rects { get; } = [];

    /// <summary>
    /// Gets the units of the region.
    /// </summary>
    public HashSet<Cell> Units { get; } = [];

    /// <summary>
    /// Gets the index of the station the region belongs to, which for a station is its own.
    /// </summary>
    public required int Station { get; init; }

    /// <summary>
    /// Gets the region this one hangs off, or null for a station.
    /// </summary>
    public int? Host { get; init; }

    /// <summary>
    /// Gets a value indicating whether the region is a hall segment.
    /// </summary>
    public bool IsHall { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the region lies behind the level's gate.
    /// </summary>
    public bool IsGated { get; set; }
}
