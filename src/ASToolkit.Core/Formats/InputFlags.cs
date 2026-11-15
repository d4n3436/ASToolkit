namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the input state bits the engine keeps for the keyboard and the mouse.
/// </summary>
[Flags]
public enum InputFlags : uint
{
    /// <summary>
    /// No input.
    /// </summary>
    None = 0,

    /// <summary>
    /// The left mouse button clicked this frame.
    /// </summary>
    LeftClick = 1 << 0,

    /// <summary>
    /// The middle mouse button clicked this frame.
    /// </summary>
    MiddleClick = 1 << 1,

    /// <summary>
    /// The right mouse button clicked this frame.
    /// </summary>
    RightClick = 1 << 2,

    /// <summary>
    /// The left mouse button released this frame.
    /// </summary>
    LeftClickRelease = 1 << 3,

    /// <summary>
    /// The right mouse button released this frame.
    /// </summary>
    RightClickRelease = 1 << 4,

    /// <summary>
    /// The left mouse button held.
    /// </summary>
    LeftDown = 1 << 5,

    /// <summary>
    /// The right mouse button held.
    /// </summary>
    RightDown = 1 << 6,

    /// <summary>
    /// The key bound to moving left held.
    /// </summary>
    Left = 1 << 7,

    /// <summary>
    /// The key bound to moving right held.
    /// </summary>
    Right = 1 << 8,

    /// <summary>
    /// The key bound to moving down held.
    /// </summary>
    Down = 1 << 9,

    /// <summary>
    /// The key bound to moving up held.
    /// </summary>
    Up = 1 << 10,

    /// <summary>
    /// The Shift key held.
    /// </summary>
    Shift = 1 << 11,

    /// <summary>
    /// The Ctrl key held.
    /// </summary>
    Ctrl = 1 << 12,

    /// <summary>
    /// The Alt key held.
    /// </summary>
    Alt = 1 << 13,

    /// <summary>
    /// The control bound to the first action (firing) held.
    /// </summary>
    First = 1 << 14,

    /// <summary>
    /// The control bound to the second action held.
    /// </summary>
    Second = 1 << 15,

    /// <summary>
    /// Any of the four movement keys.
    /// </summary>
    Movement = Left | Right | Down | Up
}
