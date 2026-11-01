namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies where the monsters of an ambush come from, seen from its trigger.
/// </summary>
public enum SpawnSource
{
    /// <summary>
    /// A spawn closet behind a spawn wall or monster hole.
    /// </summary>
    SpawnCloset,

    /// <summary>
    /// A teleport pad.
    /// </summary>
    TeleportPad,

    /// <summary>
    /// A side room off the route.
    /// </summary>
    SideRoom,

    /// <summary>
    /// Ground further along the route.
    /// </summary>
    Ahead,

    /// <summary>
    /// Ground the route has already cleared.
    /// </summary>
    Behind,

    /// <summary>
    /// The trigger itself.
    /// </summary>
    AtTrigger
}
