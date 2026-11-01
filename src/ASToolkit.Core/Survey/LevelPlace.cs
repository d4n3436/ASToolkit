using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a place of a level, a room, hall, corridor or closet cut from its floor.
/// </summary>
public sealed class LevelPlace
{
    /// <summary>
    /// Gets the number of the place, from 1 in route order.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the shape of the place.
    /// </summary>
    public required PlaceKind Kind { get; init; }

    /// <summary>
    /// Gets the part of the level the place belongs to.
    /// </summary>
    public required GateZone Zone { get; init; }

    /// <summary>
    /// Gets the floor cells of the place on the route grid.
    /// </summary>
    public required IReadOnlyList<Cell> Cells { get; init; }

    /// <summary>
    /// Gets the middle of the place in map pixels.
    /// </summary>
    public required Point Center { get; init; }

    /// <summary>
    /// Gets what the place is for.
    /// </summary>
    public required PlaceUse Use { get; init; }

    /// <summary>
    /// Gets the number of furniture pieces in the place.
    /// </summary>
    public required int FurniturePieces { get; init; }

    /// <summary>
    /// Gets the facts that hold for the place.
    /// </summary>
    public required PlaceTags Tags { get; init; }

    /// <summary>
    /// Gets the floor area in square map pixels.
    /// </summary>
    public required int AreaPixels { get; init; }

    /// <summary>
    /// Gets the longer side of the place's lattice box in map pixels.
    /// </summary>
    public required int LengthPixels { get; init; }

    /// <summary>
    /// Gets the shorter side of the place's lattice box in map pixels.
    /// </summary>
    public required int WidthPixels { get; init; }

    /// <summary>
    /// Gets the walk from the start in map pixels.
    /// </summary>
    public int? WalkFromStart { get; init; }

    /// <summary>
    /// Gets the route distance of the first visit.
    /// </summary>
    public int? FirstVisit { get; init; }

    /// <summary>
    /// Gets the number of times the route enters the place.
    /// </summary>
    public required int Visits { get; init; }

    /// <summary>
    /// Gets the detour from the route in map pixels.
    /// </summary>
    public int? Detour { get; init; }

    /// <summary>
    /// Gets the place the route enters the place from.
    /// </summary>
    public int? EnteredFrom { get; init; }

    /// <summary>
    /// Gets the hit points of the place's fights.
    /// </summary>
    public required int ThreatHitPoints { get; init; }

    /// <summary>
    /// Gets the share of the place's cells that solid objects block.
    /// </summary>
    public required float BlockedShare { get; init; }
}
