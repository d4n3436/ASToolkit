namespace ASToolkit.Core.Rooms;

/// <summary>
/// Specifies what a room is furnished as.
/// </summary>
public enum RoomType
{
    /// <summary>
    /// A room with almost nothing in it that joins other rooms.
    /// </summary>
    Passage,

    /// <summary>
    /// A room with almost nothing in it.
    /// </summary>
    Bare,

    /// <summary>
    /// A hall or corridor with wall gear, crates and cylinders.
    /// </summary>
    Hall,

    /// <summary>
    /// Desks with chairs and computers.
    /// </summary>
    Office,

    /// <summary>
    /// Lab tables with tubes, flasks and equipment.
    /// </summary>
    Lab,

    /// <summary>
    /// Banks of big computers.
    /// </summary>
    ComputerRoom,

    /// <summary>
    /// Transformers and electric panels.
    /// </summary>
    MachineRoom,

    /// <summary>
    /// Shelves, cabinets and stacks of crates and boxes.
    /// </summary>
    Storage,

    /// <summary>
    /// Containers, trucks and missiles.
    /// </summary>
    Depot,

    /// <summary>
    /// Plants and water cylinders.
    /// </summary>
    Lounge,

    /// <summary>
    /// Beds, books, toilets and gym gear.
    /// </summary>
    Quarters,

    /// <summary>
    /// A bare bay the aliens spawn into, only on the arena level.
    /// </summary>
    Arena,

    /// <summary>
    /// A secret or spawn closet.
    /// </summary>
    Closet
}
