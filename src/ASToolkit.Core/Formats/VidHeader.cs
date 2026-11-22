using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

[StructLayout(LayoutKind.Explicit)]
internal readonly struct VidHeader
{
    [FieldOffset(0)]
    public readonly VidType Type;

    [FieldOffset(2)]
    public readonly short Speed;

    [FieldOffset(4)]
    public readonly short Frames;

    [FieldOffset(6)]
    public readonly short Width;

    [FieldOffset(8)]
    public readonly short Height;

    public Size Size => new(Width, Height);
}
