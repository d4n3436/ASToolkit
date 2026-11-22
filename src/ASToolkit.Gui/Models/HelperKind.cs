namespace ASToolkit.Gui.Models;

/// <summary>
/// Specifies the role of a helper (hidden) sprite.
/// </summary>
public enum HelperKind
{
    /// <summary>
    /// A hidden sprite of another class.
    /// </summary>
    Other,


    /// <summary>
    /// A hidden built terrain sprite, which blocks movement.
    /// </summary>
    Blocker,

    /// <summary>
    /// A hidden sprite-class sprite, which scripts use as a trigger.
    /// </summary>
    Trigger,

    /// <summary>
    /// A hidden building, which spawns monsters.
    /// </summary>
    Spawner
}
