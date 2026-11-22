using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly struct RegionFog
{
    [FieldOffset(0)]
    public readonly RegionFlags Flags;

    [FieldOffset(4)]
    public readonly int Far;

    [FieldOffset(8)]
    public readonly int Near;

    [FieldOffset(12)]
    public readonly uint Color;
}
