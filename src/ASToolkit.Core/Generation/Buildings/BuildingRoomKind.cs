namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Specifies the kinds of rooms in a building, such as a hall, a room or a closet.
/// </summary>
public enum BuildingRoomKind
{
    /// <summary>A hall or corridor rooms open onto.</summary>
    Hall,

    /// <summary>A room.</summary>
    Room,

    /// <summary>A secret closet behind a breakable wall.</summary>
    SecretCloset,

    /// <summary>A furnished room off the main path behind a breakable wall.</summary>
    SecretRoom,

    /// <summary>A spawn closet whose wall explodes open.</summary>
    SpawnCloset
}
