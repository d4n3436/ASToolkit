using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly struct FlcFrameHeader
{
    [FieldOffset(4)]
    public readonly ushort Magic;

    [FieldOffset(6)]
    public readonly ushort ChunkCount;
}
