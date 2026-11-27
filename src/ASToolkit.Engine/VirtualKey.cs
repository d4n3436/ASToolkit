namespace ASToolkit.Engine;

/// <summary>
/// Specifies the virtual key code of a key or mouse button.
/// </summary>
public enum VirtualKey
{
    /// <summary>
    /// No key.
    /// </summary>
    None = 0,

    /// <summary>
    /// The left mouse button.
    /// </summary>
    LeftButton = 0x1,

    /// <summary>
    /// The right mouse button.
    /// </summary>
    RightButton = 0x2,

    /// <summary>
    /// The Backspace key.
    /// </summary>
    Backspace = 0x8,

    /// <summary>
    /// The Tab key.
    /// </summary>
    Tab = 0x9,

    /// <summary>
    /// The Enter key.
    /// </summary>
    Enter = 0xD,

    /// <summary>
    /// The Shift key.
    /// </summary>
    Shift = 0x10,

    /// <summary>
    /// The Ctrl key.
    /// </summary>
    Control = 0x11,

    /// <summary>
    /// The Alt key.
    /// </summary>
    Alt = 0x12,

    /// <summary>
    /// The Pause key.
    /// </summary>
    Pause = 0x13,

    /// <summary>
    /// The Caps Lock key.
    /// </summary>
    CapsLock = 0x14,

    /// <summary>
    /// The Esc key.
    /// </summary>
    Escape = 0x1B,

    /// <summary>
    /// The Spacebar.
    /// </summary>
    Space = 0x20,

    /// <summary>
    /// The Page Up key.
    /// </summary>
    PageUp = 0x21,

    /// <summary>
    /// The Page Down key.
    /// </summary>
    PageDown = 0x22,

    /// <summary>
    /// The End key.
    /// </summary>
    End = 0x23,

    /// <summary>
    /// The Home key.
    /// </summary>
    Home = 0x24,

    /// <summary>
    /// The Left Arrow key.
    /// </summary>
    Left = 0x25,

    /// <summary>
    /// The Up Arrow key.
    /// </summary>
    Up = 0x26,

    /// <summary>
    /// The Right Arrow key.
    /// </summary>
    Right = 0x27,

    /// <summary>
    /// The Down Arrow key.
    /// </summary>
    Down = 0x28,

    /// <summary>
    /// The Print Screen key.
    /// </summary>
    PrintScreen = 0x2C,

    /// <summary>
    /// The Insert key.
    /// </summary>
    Insert = 0x2D,

    /// <summary>
    /// The Delete key.
    /// </summary>
    Delete = 0x2E,

    /// <summary>
    /// The 0 key.
    /// </summary>
    Number0 = 0x30,

    /// <summary>
    /// The 1 key.
    /// </summary>
    Number1 = 0x31,

    /// <summary>
    /// The 2 key.
    /// </summary>
    Number2 = 0x32,

    /// <summary>
    /// The 3 key.
    /// </summary>
    Number3 = 0x33,

    /// <summary>
    /// The 4 key.
    /// </summary>
    Number4 = 0x34,

    /// <summary>
    /// The 5 key.
    /// </summary>
    Number5 = 0x35,

    /// <summary>
    /// The 6 key.
    /// </summary>
    Number6 = 0x36,

    /// <summary>
    /// The 7 key.
    /// </summary>
    Number7 = 0x37,

    /// <summary>
    /// The 8 key.
    /// </summary>
    Number8 = 0x38,

    /// <summary>
    /// The 9 key.
    /// </summary>
    Number9 = 0x39,

    /// <summary>
    /// The A key.
    /// </summary>
    A = 0x41,

    /// <summary>
    /// The B key.
    /// </summary>
    B = 0x42,

    /// <summary>
    /// The C key.
    /// </summary>
    C = 0x43,

    /// <summary>
    /// The D key.
    /// </summary>
    D = 0x44,

    /// <summary>
    /// The E key.
    /// </summary>
    E = 0x45,

    /// <summary>
    /// The F key.
    /// </summary>
    F = 0x46,

    /// <summary>
    /// The G key.
    /// </summary>
    G = 0x47,

    /// <summary>
    /// The H key.
    /// </summary>
    H = 0x48,

    /// <summary>
    /// The I key.
    /// </summary>
    I = 0x49,

    /// <summary>
    /// The J key.
    /// </summary>
    J = 0x4A,

    /// <summary>
    /// The K key.
    /// </summary>
    K = 0x4B,

    /// <summary>
    /// The L key.
    /// </summary>
    L = 0x4C,

    /// <summary>
    /// The M key.
    /// </summary>
    M = 0x4D,

    /// <summary>
    /// The N key.
    /// </summary>
    N = 0x4E,

    /// <summary>
    /// The O key.
    /// </summary>
    O = 0x4F,

    /// <summary>
    /// The P key.
    /// </summary>
    P = 0x50,

    /// <summary>
    /// The Q key.
    /// </summary>
    Q = 0x51,

    /// <summary>
    /// The R key.
    /// </summary>
    R = 0x52,

    /// <summary>
    /// The S key.
    /// </summary>
    S = 0x53,

    /// <summary>
    /// The T key.
    /// </summary>
    T = 0x54,

    /// <summary>
    /// The U key.
    /// </summary>
    U = 0x55,

    /// <summary>
    /// The V key.
    /// </summary>
    V = 0x56,

    /// <summary>
    /// The W key.
    /// </summary>
    W = 0x57,

    /// <summary>
    /// The X key.
    /// </summary>
    X = 0x58,

    /// <summary>
    /// The Y key.
    /// </summary>
    Y = 0x59,

    /// <summary>
    /// The Z key.
    /// </summary>
    Z = 0x5A,

    /// <summary>
    /// The left Windows key.
    /// </summary>
    LeftWindows = 0x5B,

    /// <summary>
    /// The right Windows key.
    /// </summary>
    RightWindows = 0x5C,

    /// <summary>
    /// The Applications key.
    /// </summary>
    Applications = 0x5D,

    /// <summary>
    /// The 0 key on the numeric keypad.
    /// </summary>
    Numpad0 = 0x60,

    /// <summary>
    /// The 1 key on the numeric keypad.
    /// </summary>
    Numpad1 = 0x61,

    /// <summary>
    /// The 2 key on the numeric keypad.
    /// </summary>
    Numpad2 = 0x62,

    /// <summary>
    /// The 3 key on the numeric keypad.
    /// </summary>
    Numpad3 = 0x63,

    /// <summary>
    /// The 4 key on the numeric keypad.
    /// </summary>
    Numpad4 = 0x64,

    /// <summary>
    /// The 5 key on the numeric keypad.
    /// </summary>
    Numpad5 = 0x65,

    /// <summary>
    /// The 6 key on the numeric keypad.
    /// </summary>
    Numpad6 = 0x66,

    /// <summary>
    /// The 7 key on the numeric keypad.
    /// </summary>
    Numpad7 = 0x67,

    /// <summary>
    /// The 8 key on the numeric keypad.
    /// </summary>
    Numpad8 = 0x68,

    /// <summary>
    /// The 9 key on the numeric keypad.
    /// </summary>
    Numpad9 = 0x69,

    /// <summary>
    /// The Multiply key on the numeric keypad.
    /// </summary>
    Multiply = 0x6A,

    /// <summary>
    /// The Add key on the numeric keypad.
    /// </summary>
    Add = 0x6B,

    /// <summary>
    /// The Subtract key on the numeric keypad.
    /// </summary>
    Subtract = 0x6D,

    /// <summary>
    /// The Decimal key on the numeric keypad.
    /// </summary>
    Decimal = 0x6E,

    /// <summary>
    /// The Divide key on the numeric keypad.
    /// </summary>
    Divide = 0x6F,

    /// <summary>
    /// The F1 key.
    /// </summary>
    F1 = 0x70,

    /// <summary>
    /// The F2 key.
    /// </summary>
    F2 = 0x71,

    /// <summary>
    /// The F3 key.
    /// </summary>
    F3 = 0x72,

    /// <summary>
    /// The F4 key.
    /// </summary>
    F4 = 0x73,

    /// <summary>
    /// The F5 key.
    /// </summary>
    F5 = 0x74,

    /// <summary>
    /// The F6 key.
    /// </summary>
    F6 = 0x75,

    /// <summary>
    /// The F7 key.
    /// </summary>
    F7 = 0x76,

    /// <summary>
    /// The F8 key.
    /// </summary>
    F8 = 0x77,

    /// <summary>
    /// The F9 key.
    /// </summary>
    F9 = 0x78,

    /// <summary>
    /// The F10 key.
    /// </summary>
    F10 = 0x79,

    /// <summary>
    /// The F11 key.
    /// </summary>
    F11 = 0x7A,

    /// <summary>
    /// The F12 key.
    /// </summary>
    F12 = 0x7B,

    /// <summary>
    /// The Num Lock key.
    /// </summary>
    NumLock = 0x90,

    /// <summary>
    /// The Scroll Lock key.
    /// </summary>
    ScrollLock = 0x91,

    /// <summary>
    /// The ; key.
    /// </summary>
    Semicolon = 0xBA,

    /// <summary>
    /// The = key.
    /// </summary>
    Plus = 0xBB,

    /// <summary>
    /// The , key.
    /// </summary>
    Comma = 0xBC,

    /// <summary>
    /// The - key.
    /// </summary>
    Minus = 0xBD,

    /// <summary>
    /// The . key.
    /// </summary>
    Period = 0xBE,

    /// <summary>
    /// The / key.
    /// </summary>
    Slash = 0xBF,

    /// <summary>
    /// The ` key.
    /// </summary>
    Backquote = 0xC0,

    /// <summary>
    /// The [ key.
    /// </summary>
    LeftBracket = 0xDB,

    /// <summary>
    /// The \ key.
    /// </summary>
    Backslash = 0xDC,

    /// <summary>
    /// The ] key.
    /// </summary>
    RightBracket = 0xDD,

    /// <summary>
    /// The ' key.
    /// </summary>
    Quote = 0xDE
}
