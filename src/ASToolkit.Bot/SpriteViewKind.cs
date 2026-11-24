namespace ASToolkit.Bot;

/// <summary>
/// Specifies which sprites a <see cref="SpriteView"/> enumerates.
/// </summary>
public enum SpriteViewKind
{
    /// <summary>
    /// The sprites of the update lists.
    /// </summary>
    Updated,

    /// <summary>
    /// The units of the collision list other than the scripted ones, buildings, the player's body and its torso.
    /// </summary>
    Monsters,

    /// <summary>
    /// The scripted sprites of the update lists.
    /// </summary>
    Scripted
}
