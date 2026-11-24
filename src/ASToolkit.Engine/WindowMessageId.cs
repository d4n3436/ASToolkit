namespace ASToolkit.Engine;

/// <summary>
/// Specifies the identifier of a window message.
/// </summary>
public enum WindowMessageId
{
    /// <summary>
    /// The window is being destroyed.
    /// </summary>
    Destroy = 0x2,

    /// <summary>
    /// The window has moved.
    /// </summary>
    Move = 0x3,

    /// <summary>
    /// The window needs to be painted.
    /// </summary>
    Paint = 0xF,

    /// <summary>
    /// The window is asked to close.
    /// </summary>
    Close = 0x10,

    /// <summary>
    /// The message loop is asked to end.
    /// </summary>
    Quit = 0x12,

    /// <summary>
    /// The application is activated or deactivated.
    /// </summary>
    ActivateApp = 0x1C,

    /// <summary>
    /// The mouse cursor has moved over the window.
    /// </summary>
    HitTest = 0x84,

    /// <summary>
    /// A key is pressed.
    /// </summary>
    KeyDown = 0x100,

    /// <summary>
    /// A key is released.
    /// </summary>
    KeyUp = 0x101,

    /// <summary>
    /// A key typed a character.
    /// </summary>
    Char = 0x102,

    /// <summary>
    /// A key is pressed while Alt is held, or F10 is pressed.
    /// </summary>
    SysKeyDown = 0x104,

    /// <summary>
    /// A key pressed while Alt was held, or F10, is released.
    /// </summary>
    SysKeyUp = 0x105,

    /// <summary>
    /// A menu or accelerator command is chosen.
    /// </summary>
    Command = 0x111,

    /// <summary>
    /// A command from the window menu is chosen.
    /// </summary>
    SysCommand = 0x112,

    /// <summary>
    /// The left mouse button is pressed.
    /// </summary>
    LeftButtonDown = 0x201,

    /// <summary>
    /// The left mouse button is released.
    /// </summary>
    LeftButtonUp = 0x202,

    /// <summary>
    /// The right mouse button is pressed.
    /// </summary>
    RightButtonDown = 0x204,

    /// <summary>
    /// The right mouse button is released.
    /// </summary>
    RightButtonUp = 0x205,

    /// <summary>
    /// The middle mouse button is pressed.
    /// </summary>
    MiddleButtonDown = 0x207,

    /// <summary>
    /// The mouse wheel is turned.
    /// </summary>
    MouseWheel = 0x20A,

    /// <summary>
    /// A menu loop has started.
    /// </summary>
    EnterMenuLoop = 0x211,

    /// <summary>
    /// A menu loop has ended.
    /// </summary>
    ExitMenuLoop = 0x212,

    /// <summary>
    /// A window move or resize has started.
    /// </summary>
    EnterSizeMove = 0x231,

    /// <summary>
    /// A window move or resize has ended.
    /// </summary>
    ExitSizeMove = 0x232
}
