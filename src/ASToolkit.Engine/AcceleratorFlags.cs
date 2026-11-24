namespace ASToolkit.Engine;

[Flags]
internal enum AcceleratorFlags
{
    None = 0,
    VirtualKey = 1 << 0,
    NoInvert = 1 << 1,
    Shift = 1 << 2,
    Control = 1 << 3,
    Alt = 1 << 4,
    Last = 1 << 7
}
