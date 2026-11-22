using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly struct CursorEntry
{
    [FieldOffset(4)]
    public readonly ushort HotspotX;

    [FieldOffset(6)]
    public readonly ushort HotspotY;

    [FieldOffset(12)]
    public readonly uint Offset;
}
