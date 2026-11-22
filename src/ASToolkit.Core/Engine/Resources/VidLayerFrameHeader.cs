using System.Runtime.InteropServices;

namespace ASToolkit.Core.Engine;

[StructLayout(LayoutKind.Explicit, Size = 4)]
internal readonly struct VidLayerFrameHeader
{
    [FieldOffset(0)]
    public readonly short Width;

    [FieldOffset(2)]
    public readonly short Height;
}
