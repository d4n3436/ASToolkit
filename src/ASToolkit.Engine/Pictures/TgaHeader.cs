using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 18)]
internal readonly struct TgaHeader
{
    [FieldOffset(0)]
    public readonly byte IdLength;

    [FieldOffset(1)]
    public readonly byte ColorMapType;

    [FieldOffset(2)]
    public readonly TgaImageType ImageType;

    [FieldOffset(12)]
    public readonly ushort Width;

    [FieldOffset(14)]
    public readonly ushort Height;

    [FieldOffset(16)]
    public readonly byte Depth;

    public Size Size => new(Width, Height);
}
