namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies how the tour crosses from one section of a level into the next.
/// </summary>
public enum BoundaryKind
{
    /// <summary>
    /// An opening, doorway or door between two rooms.
    /// </summary>
    Passage,

    /// <summary>
    /// A passage into or out of a corridor.
    /// </summary>
    Corridor,

    /// <summary>
    /// A locked door, or a passage into the locked zone.
    /// </summary>
    LockedDoor,

    /// <summary>
    /// A passage into the zone dark until the breaker.
    /// </summary>
    Blackout,

    /// <summary>
    /// A fence standing in a frame, or a passage into the fenced zone.
    /// </summary>
    Fence,

    /// <summary>
    /// A broken or secret wall.
    /// </summary>
    Breach
}
