using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents a level layout on the unit grid.
/// </summary>
public sealed class LayoutPlan
{
    /// <summary>
    /// Gets the unit and opening sizes of the plan's wall set.
    /// </summary>
    public required WallSetSettings Settings { get; init; }

    /// <summary>
    /// Gets the regions, in which the ID of a region is its index.
    /// </summary>
    public required IReadOnlyList<PlanRegion> Regions { get; init; }

    /// <summary>
    /// Gets the links between regions.
    /// </summary>
    public required IReadOnlyList<PlanLink> Links { get; init; }

    /// <summary>
    /// Gets the edges of the jammed doors, which are the shut doors in walls shared by two rooms that keep the walk on its route.
    /// </summary>
    public required IReadOnlyList<EdgeKey> JammedDoors { get; init; }

    /// <summary>
    /// Gets the region of every occupied unit.
    /// </summary>
    public required IReadOnlyDictionary<Cell, int> Occupancy { get; init; }

    /// <summary>
    /// Gets the stations in route order.
    /// </summary>
    public required IReadOnlyList<PlanRegion> Stations { get; init; }

    /// <summary>
    /// Gets the gate kind, or null for a level without a gate.
    /// </summary>
    public GateKind? Gate { get; init; }

    /// <summary>
    /// Gets the room of the gate's key, the breaker or generator.
    /// </summary>
    public PlanRegion? KeyRoom { get; init; }

    /// <summary>
    /// Gets the rooms a fenced level's generator may stand in, with the walk in pixels from the start to the back of each.
    /// </summary>
    public required IReadOnlyList<PlanRegion> GeneratorRooms { get; init; }

    /// <summary>
    /// Gets the link the blackout trigger sits in.
    /// </summary>
    public PlanLink? BlackoutLink { get; init; }

    /// <summary>
    /// Gets the room the blackout trigger stands in.
    /// </summary>
    public PlanRegion? TriggerRoom { get; init; }

    /// <summary>
    /// Gets the rooms of a lab's second wing, which are the only rooms that monsters, their triggers and dynamite sites stand in, or any room when the list is empty.
    /// </summary>
    public required IReadOnlySet<PlanRegion> SecondWing { get; init; }

    /// <summary>
    /// Gets the site rooms that are locked until the key, with some of the sites standing in them.
    /// </summary>
    public required IReadOnlySet<PlanRegion> LockedSiteRooms { get; init; }

    /// <summary>
    /// Determines whether monsters, their triggers and dynamite sites may stand in a region.
    /// </summary>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> if the region may hold them; otherwise, <see langword="false"/>.</returns>
    public bool HoldsFights(PlanRegion region) => SecondWing.Count == 0 || SecondWing.Contains(region);
}
