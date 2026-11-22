using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 4)]
internal readonly struct VidSurfaceSize
{
    [FieldOffset(0)]
    public readonly short Width;

    [FieldOffset(2)]
    public readonly short Height;

    public Size Size => new(Width, Height);
}
