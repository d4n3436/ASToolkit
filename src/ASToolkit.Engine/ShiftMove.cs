namespace ASToolkit.Engine;

[Flags]
internal enum ShiftMove
{
    None = 0,
    Smooth = 1 << 1,
    Unclamped = 1 << 28
}
