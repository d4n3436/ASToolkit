using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PaletteEntry
{
    public readonly byte Red;
    public readonly byte Green;
    public readonly byte Blue;
    public readonly byte Flags;

    public PaletteEntry(uint color)
    {
        Flags = (byte)(color >> 24);
        Red = (byte)(color >> 16);
        Green = (byte)(color >> 8);
        Blue = (byte)color;
    }

    public uint ToColor() => (uint)(Flags << 24 | Red << 16 | Green << 8 | Blue);
}
