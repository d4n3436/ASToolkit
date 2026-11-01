namespace ASToolkit.Core.Rooms;

/// <summary>
/// Specifies where a room stands on its level's door graph.
/// </summary>
public enum RoomRole
{
    /// <summary>
    /// The room the player starts in.
    /// </summary>
    Start,

    /// <summary>
    /// The hall next to the start that the branches open onto.
    /// </summary>
    Hub,

    /// <summary>
    /// The main body room that leads on from the hub.
    /// </summary>
    Neck,

    /// <summary>
    /// A hall of the main body other than the hub.
    /// </summary>
    Hall,

    /// <summary>
    /// A main body room on the side of the hub.
    /// </summary>
    HubRow,

    /// <summary>
    /// A main body room on the side of the second hall.
    /// </summary>
    HallRow,

    /// <summary>
    /// Another room of the main body.
    /// </summary>
    Body,

    /// <summary>
    /// The room of a branch that opens onto the main body.
    /// </summary>
    BranchRoot,

    /// <summary>
    /// A room deeper in a branch.
    /// </summary>
    Branch,

    /// <summary>
    /// The room that holds the key of a gate on its own off the main body.
    /// </summary>
    Key,

    /// <summary>
    /// A secret or spawn closet, or a nook too small to be a room.
    /// </summary>
    Closet,

    /// <summary>
    /// A room behind a breakable wall.
    /// </summary>
    Secret,

    /// <summary>
    /// Bare ground outside the walls.
    /// </summary>
    Outside
}
