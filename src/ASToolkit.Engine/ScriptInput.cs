namespace ASToolkit.Engine;

[Flags]
internal enum ScriptInput
{
    None = 0,
    LeftClick = 1 << 0,
    RightClick = 1 << 1,
    LeftDown = 1 << 2,
    RightDown = 1 << 3,
    Shift = 1 << 4,
    Ctrl = 1 << 5,
    Left = 1 << 6,
    Right = 1 << 7,
    Up = 1 << 8,
    Down = 1 << 9,
    First = 1 << 10,
    Second = 1 << 11
}
