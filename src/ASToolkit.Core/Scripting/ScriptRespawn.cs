namespace ASToolkit.Core.Scripting;

/// <summary>
/// Specifies where a script trigger moves the point the player respawns at.
/// </summary>
public enum ScriptRespawn
{
    /// <summary>
    /// The respawn point stays.
    /// </summary>
    None,

    /// <summary>
    /// A point the script names.
    /// </summary>
    Fixed,

    /// <summary>
    /// The point where the player stands when the trigger fires.
    /// </summary>
    Player
}
