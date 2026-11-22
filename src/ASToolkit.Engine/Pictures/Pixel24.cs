using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 3)]
internal readonly struct Pixel24
{
    [FieldOffset(0)]
    public readonly ushort Low;

    [FieldOffset(2)]
    public readonly byte High;

    public uint Value => Low | (uint)High << 16;
}
