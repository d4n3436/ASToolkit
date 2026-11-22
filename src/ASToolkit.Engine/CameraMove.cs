namespace ASToolkit.Engine;

[Flags]
internal enum CameraMove
{
    None = 0,
    Smooth = 1 << 1,
    Unclamped = 1 << 28
}
