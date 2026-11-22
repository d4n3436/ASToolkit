using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 128)]
internal readonly struct FlcHeader
{
    [FieldOffset(4)]
    public readonly FlcMagic Magic;

    [FieldOffset(6)]
    public readonly ushort Frames;

    [FieldOffset(8)]
    public readonly ushort Width;

    [FieldOffset(10)]
    public readonly ushort Height;

    [FieldOffset(16)]
    public readonly int Speed;

    [FieldOffset(80)]
    public readonly int FirstFrameOffset;

    public Size Size => new(Width, Height);
}
