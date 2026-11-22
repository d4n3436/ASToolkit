using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Engine;

[StructLayout(LayoutKind.Explicit, Size = 54)]
internal readonly struct BmpHeader
{
    [FieldOffset(10)]
    public readonly int DataOffset;

    [FieldOffset(18)]
    public readonly Size Size;

    [FieldOffset(28)]
    public readonly ushort Depth;

    [FieldOffset(30)]
    public readonly int Compression;
}
