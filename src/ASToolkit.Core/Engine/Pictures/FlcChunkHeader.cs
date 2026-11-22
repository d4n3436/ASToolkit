using System.Runtime.InteropServices;

namespace ASToolkit.Core.Engine;

[StructLayout(LayoutKind.Explicit, Size = 6)]
internal readonly struct FlcChunkHeader
{
    [FieldOffset(0)]
    public readonly int Size;

    [FieldOffset(4)]
    public readonly FlcChunkType Type;
}
