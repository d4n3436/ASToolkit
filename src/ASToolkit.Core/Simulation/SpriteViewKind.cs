namespace ASToolkit.Core.Simulation;

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
    /// The units of the collision list that are not scripted, buildings, the player's body or its torso.
    /// </summary>
    Monsters,

    /// <summary>
    /// The updated sprites that did not come from the map, plus the scripted ones.
    /// </summary>
    Active,

    /// <summary>
    /// The scripted sprites of the update lists.
    /// </summary>
    Scripted
}
