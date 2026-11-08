using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents a level layout on the unit grid, with a chain of stations from the start, side rooms, closets and the gate.
/// </summary>
public sealed class LayoutPlan
{
    /// <summary>
    /// Gets the unit and opening sizes of the plan's wall set.
    /// </summary>
    public required WallSetSettings Settings { get; init; }

    /// <summary>
    /// Gets the regions; a region's ID is its index.
    /// </summary>
    public required IReadOnlyList<PlanRegion> Regions { get; init; }

    /// <summary>
    /// Gets the links between regions.
    /// </summary>
    public required IReadOnlyList<PlanLink> Links { get; init; }

    /// <summary>
    /// Gets the edges of the jammed doors, shut doors in walls two rooms share that keep the walk on its route.
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
    public required IReadOnlyList<(PlanRegion Room, float WalkPixels)> GeneratorRooms { get; init; }

    /// <summary>
    /// Gets the link the blackout trigger sits in.
    /// </summary>
    public PlanLink? BlackoutLink { get; init; }

    /// <summary>
    /// Gets the room the blackout trigger stands in.
    /// </summary>
    public PlanRegion? TriggerRoom { get; init; }

    /// <summary>
    /// Gets the rooms of a lab's second wing, the only ones monsters, their triggers and dynamite sites stand in, any room when empty.
    /// </summary>
    public required IReadOnlySet<PlanRegion> SecondWing { get; init; }

    /// <summary>
    /// Gets the site rooms locked until the key, some of the sites standing in them.
    /// </summary>
    public required IReadOnlySet<PlanRegion> LockedSiteRooms { get; init; }

    /// <summary>
    /// Returns whether monsters, their triggers and dynamite sites may stand in a region.
    /// </summary>
    public bool HoldsFights(PlanRegion region) => SecondWing.Count == 0 || SecondWing.Contains(region);

    /// <summary>
    /// Returns the zone a region lies in.
    /// </summary>
    public GateZone GetZone(PlanRegion region) => region.Kind is RegionKind.SecretCloset or RegionKind.SecretRoom ? GateZone.Secret : !region.IsGated ? GateZone.Open : Gate switch
    {
        GateKind.Fences => GateZone.Fenced,
        GateKind.Blackout => GateZone.Blackout,
        _ => GateZone.Locked
    };
}
