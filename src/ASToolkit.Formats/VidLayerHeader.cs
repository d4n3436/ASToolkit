using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct VidLayerHeader
{
    [FieldOffset(0)]
    public VidPixelFormat Format;

    [FieldOffset(4)]
    public short Width;

    [FieldOffset(6)]
    public short Height;
}
