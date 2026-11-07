using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

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
    public required RegionKind Kind { get; init; }

    /// <summary>
    /// Gets the rectangles the region covers.
    /// </summary>
    public required IReadOnlyList<UnitRect> Rects { get; init; }

    /// <summary>
    /// Gets the units of the region.
    /// </summary>
    public required IReadOnlySet<Cell> Units { get; init; }

    /// <summary>
    /// Gets the index of the station the region belongs to, which for a station is its own.
    /// </summary>
    public required int Station { get; init; }

    /// <summary>
    /// Gets the region this region hangs off, or null for a station.
    /// </summary>
    public int? Host { get; init; }

    /// <summary>
    /// Gets a value indicating whether the region is a hall segment.
    /// </summary>
    public bool IsHall { get; init; }

    /// <summary>
    /// Gets a value indicating whether the region lies behind the level's gate.
    /// </summary>
    public bool IsGated { get; init; }

    /// <summary>
    /// Gets the mission phase the region first belongs to.
    /// </summary>
    public int Phase { get; init; }

    /// <summary>
    /// Gets a value indicating whether the level's critical walk, the way in and the way back, passes the region.
    /// </summary>
    public bool IsOnRoute { get; init; }

    /// <summary>
    /// Gets the mission role of the region's room, or null for a region without one.
    /// </summary>
    public MissionRole? Role { get; init; }

    /// <summary>
    /// Gets the room type drawn for the region, or null for a region that is not a room.
    /// </summary>
    public RoomType? Type { get; init; }
}
