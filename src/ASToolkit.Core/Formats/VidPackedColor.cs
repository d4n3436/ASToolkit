using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a palette entry of three bytes, the form older sprite files store their palettes in.
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
    /// Gets the color packed as 0xRRGGBB.
    /// </summary>
    public uint Rgb => (uint)Red << 16 | (uint)Green << 8 | Blue;
}
