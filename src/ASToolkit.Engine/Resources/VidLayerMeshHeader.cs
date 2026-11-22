using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 8)]
internal readonly struct VidLayerMeshHeader
{
    [FieldOffset(0)]
    public readonly int VertexCount;

    [FieldOffset(4)]
    public readonly int IndexCount;
}
