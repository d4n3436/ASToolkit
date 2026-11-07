namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Specifies a fault the doors stage counts while laying a building's doors, such as a lock off the way to its key or a dark room past the breaker.
/// </summary>
public enum BuildingFault
{
    /// <summary>A room whose phase lies outside the building's phases.</summary>
    RoomWithoutPhase,

    /// <summary>A lock whose earlier room lies more than one contact from every way to the key.</summary>
    LockSourceOffPath,

    /// <summary>A walk back across the blackout phase on a level without dynamite sites.</summary>
    CrossedBackWithoutSites,

    /// <summary>A dark room deeper than the breaker on every tree of the blackout phase.</summary>
    DarkRoomPastBreaker
}
