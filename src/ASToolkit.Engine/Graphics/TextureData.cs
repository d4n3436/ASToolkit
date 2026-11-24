using System.Drawing;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a texture of a definition, with its texels in the allocated layout.
/// </summary>
/// <param name="Texels">The texels, where a texel of a 32-bit layout takes two elements and the blocks of a compressed layout are packed.</param>
/// <param name="Size">The allocated size of the texture, in texels.</param>
/// <param name="Format">The allocated layout of the texels.</param>
/// <param name="Palette">The colors a P8 texture indexes, packed as 0xAARRGGBB, or empty.</param>
public readonly record struct TextureData(ushort[] Texels, Size Size, VidPixelFormat Format, uint[] Palette)
{
    /// <summary>
    /// Gets a value indicating whether the texture doesn't have any texels.
    /// </summary>
    public bool IsEmpty => Texels is null || Texels.Length == 0;

    /// <summary>
    /// Gets a view of the texture for drawing.
    /// </summary>
    public Texture View => new(Texels, Size, Format, Palette);
}
