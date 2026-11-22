using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly struct ZHeader
{
    [FieldOffset(0)]
    public readonly uint Magic;

    [FieldOffset(4)]
    public readonly Size Size;

    [FieldOffset(12)]
    public readonly int Frames;
}
