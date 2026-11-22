using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 40)]
internal readonly struct CursorBitmapHeader
{
    [FieldOffset(0)]
    public readonly uint Size;

    [FieldOffset(4)]
    public readonly int Width;

    [FieldOffset(8)]
    public readonly int Height;

    [FieldOffset(14)]
    public readonly ushort BitCount;

    [FieldOffset(32)]
    public readonly uint ColorsUsed;
}
