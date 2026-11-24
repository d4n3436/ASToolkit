using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a three-byte palette entry of older sprite files.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 3)]
public readonly struct VidPackedColor
{
    /// <summary>
    /// The red channel.
    /// </summary>
    [FieldOffset(0)]
    public readonly byte Red;

    /// <summary>
    /// The green channel.
    /// </summary>
    [FieldOffset(1)]
    public readonly byte Green;

    /// <summary>
    /// The blue channel.
    /// </summary>
    [FieldOffset(2)]
    public readonly byte Blue;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidPackedColor"/> structure from a color.
    /// </summary>
    /// <param name="rgb">The color, packed as 0xRRGGBB. Bits above the red channel are ignored.</param>
    public VidPackedColor(uint rgb)
    {
        Red = (byte)(rgb >> 16);
        Green = (byte)(rgb >> 8);
        Blue = (byte)rgb;
    }

    /// <summary>
    /// Gets the color packed as 0xRRGGBB.
    /// </summary>
    public uint Rgb => (uint)Red << 16 | (uint)Green << 8 | Blue;
}
