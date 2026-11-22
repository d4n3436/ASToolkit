using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 8)]
internal readonly struct DxtColorBlock
{
    [FieldOffset(0)]
    public readonly ushort First;

    [FieldOffset(2)]
    public readonly ushort Second;

    [FieldOffset(4)]
    public readonly uint Indexes;
}
