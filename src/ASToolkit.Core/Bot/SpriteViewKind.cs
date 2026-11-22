namespace ASToolkit.Core.Bot;

/// <summary>
/// Specifies the sprites a <see cref="SpriteView"/> walks.
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
    /// The scripted sprites and the updated sprites that didn't come from the map.
    /// </summary>
    Active,

    /// <summary>
    /// The scripted sprites of the update lists.
    /// </summary>
    Scripted
}
