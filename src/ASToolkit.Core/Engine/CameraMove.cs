namespace ASToolkit.Core.Engine;

[Flags]
internal enum CameraMove
{
    None = 0,
    Smooth = 2,
    Unclamped = 0x10000000
}
