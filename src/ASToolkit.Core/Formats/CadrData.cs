using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

[StructLayout(LayoutKind.Explicit)]
internal readonly struct CadrData
{
    [FieldOffset(0)]
    public readonly ushort IndexCount;

    [FieldOffset(2)]
    public readonly ushort VertexCount;

    [FieldOffset(4)]
    public readonly short Width;

    [FieldOffset(6)]
    public readonly short Height;

    [FieldOffset(8)]
    public readonly short Left;

    [FieldOffset(10)]
    public readonly short Top;

    public Size Size => new(Width, Height);

    public Point Origin => new(Left, Top);
}
