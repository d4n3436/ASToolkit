namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents an animation state of an object.
/// </summary>
public enum SpriteAnimation
{
    /// <summary>
    /// The normal state of the object.
    /// </summary>
    Stand = 0,

    /// <summary>
    /// The object stops moving or builds a child unit.
    /// </summary>
    StopMove = 1,

    /// <summary>
    /// The unit moves.
    /// </summary>
    Go = 2,

    /// <summary>
    /// The unit starts moving.
    /// </summary>
    StartMove = 3,

    /// <summary>
    /// The object turns left.
    /// </summary>
    LRotate = 4,

    /// <summary>
    /// The object turns right.
    /// </summary>
    RRotate = 5,

    /// <summary>
    /// The object opens.
    /// </summary>
    Open = 6,

    /// <summary>
    /// The object closes or is hit.
    /// </summary>
    Close = 7,

    /// <summary>
    /// The unit attacks.
    /// </summary>
    Fight = 8,

    /// <summary>
    /// The player clicked the unit.
    /// </summary>
    Salut = 9,

    /// <summary>
    /// The object stands open.
    /// </summary>
    StandOpen = 10,

    /// <summary>
    /// The object loads, links or hits a vertical wall.
    /// </summary>
    Load = 11,

    /// <summary>
    /// The object unloads, idles or clashes.
    /// </summary>
    Unload = 12,

    /// <summary>
    /// The unit lost half of its hit points.
    /// </summary>
    Wound = 13,

    /// <summary>
    /// The object is born.
    /// </summary>
    Birth = 14,

    /// <summary>
    /// The unit dies.
    /// </summary>
    Death = 15,

    /// <summary>
    /// The unit dies from damage above half of its maximum hit points at once.
    /// </summary>
    Death2 = 16
}
