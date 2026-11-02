using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents an area of a layout.
/// </summary>
public sealed class PlanRegion
{
    /// <summary>
    /// Gets the region ID, the region's index in the plan.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the role of the region.
    /// </summary>
    public required RegionKind Kind { get; set; }

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
    public required int Station { get; set; }

    /// <summary>
    /// Gets the region this region hangs off, or null for a station.
    /// </summary>
    public int? Host { get; set; }

    /// <summary>
    /// Gets a value indicating whether the region is a hall segment.
    /// </summary>
    public bool IsHall { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the region lies behind the level's gate.
    /// </summary>
    public bool IsGated { get; set; }

    /// <summary>
    /// Gets or sets the mission room the region holds, or null for a region the mission has no room for.
    /// </summary>
    public int? Room { get; set; }

    /// <summary>
    /// Gets or sets the mission phase the region first belongs to.
    /// </summary>
    public int Phase { get; set; }

    /// <summary>
    /// Gets or sets the region's index in the mission's flow order, or null when the flow does not order it.
    /// </summary>
    public int? Flow { get; set; }

    /// <summary>
    /// Gets or sets the mission role of the region's room, or null for a region without one.
    /// </summary>
    public MissionRole? Role { get; set; }
}
