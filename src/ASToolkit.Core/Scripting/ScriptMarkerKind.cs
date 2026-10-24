namespace ASToolkit.Core.Scripting;

/// <summary>
/// Specifies what a script event of a level does.
/// </summary>
public enum ScriptMarkerKind
{
    /// <summary>
    /// An event with no effect on the way through the level.
    /// </summary>
    Other,

    /// <summary>
    /// A dialog.
    /// </summary>
    Dialog,

    /// <summary>
    /// The building of monsters.
    /// </summary>
    Spawn,

    /// <summary>
    /// The opening of a door, a wall or a hidden area.
    /// </summary>
    Opening,

    /// <summary>
    /// The closing of a door or a passage.
    /// </summary>
    Closing,

    /// <summary>
    /// A new place where the player comes back after dying.
    /// </summary>
    Checkpoint,

    /// <summary>
    /// The player taking control of a unit.
    /// </summary>
    Control,

    /// <summary>
    /// A move of the player to another place.
    /// </summary>
    Teleport,

    /// <summary>
    /// The lights going out and the doors locking.
    /// </summary>
    Blackout,

    /// <summary>
    /// The failure of the mission.
    /// </summary>
    Failure,

    /// <summary>
    /// The start of a quest.
    /// </summary>
    Quest,

    /// <summary>
    /// The end of the level.
    /// </summary>
    Exit
}
