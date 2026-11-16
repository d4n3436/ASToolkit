namespace ASToolkit.Core.Engine;

/// <summary>
/// Specifies the side list that removes a sprite when it leaves the world.
/// </summary>
public enum UpdateRole
{
    /// <summary>
    /// No side list.
    /// </summary>
    None,
    
    /// <summary>
    /// The player's body.
    /// </summary>
    Player,
    
    /// <summary>
    /// The player's torso.
    /// </summary>
    Torso,
    
    /// <summary>
    /// The monster list.
    /// </summary>
    Monster,
    
    /// <summary>
    /// The scripted sprite list.
    /// </summary>
    Scripted,
    
    /// <summary>
    /// The effect list.
    /// </summary>
    Effect,
    
    /// <summary>
    /// A map sprite.
    /// </summary>
    Sprite
}
