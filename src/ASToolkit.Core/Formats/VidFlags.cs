namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the flags in the header of a sprite.
/// </summary>
[Flags]
public enum VidFlags : ushort
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// The frame records are runs of 8-bit values (palette indices and 8-bit depth). Sprites without the flag, such as the ones with 0x36, use 16-bit runs.
    /// </summary>
    Runs8Bit = 1 << 0,

    /// <summary>
    /// The sprite blends with what lies under it using the alpha byte of the palette instead of covering it.
    /// </summary>
    Blended = 1 << 1,

    /// <summary>
    /// The pixels carry a depth value.
    /// </summary>
    Depth = 1 << 2,

    /// <summary>
    /// The pixel colors are palette indices.
    /// </summary>
    Palette = 1 << 3,

    /// <summary>
    /// The sprite uses the current format version.
    /// </summary>
    CurrentVersion = 1 << 4,

    /// <summary>
    /// The sprite has a SURF chunk with 32-bit surfaces.
    /// </summary>
    Surface = 1 << 5,

    /// <summary>
    /// The sprite has a CADR chunk with cached surface data instead of frames.
    /// </summary>
    Cadr = 1 << 6,

    /// <summary>
    /// The sprite is an omni-light: a 2 x 2 sprite whose frames are BGRA colors.
    /// </summary>
    OmniLight = 1 << 7,

    /// <summary>
    /// The surfaces are range coded.
    /// </summary>
    RangeCoded = 1 << 8,

    /// <summary>
    /// Each cached frame record starts with a 12-byte vector.
    /// </summary>
    FrameVector = 1 << 9,

    /// <summary>
    /// The sprite stores several palettes. No sprite of the game sets it.
    /// </summary>
    PaletteVariants = 1 << 10,

    /// <summary>
    /// The surfaces are DXT compressed.
    /// </summary>
    Dxt = 1 << 11,

    /// <summary>
    /// Has no known meaning. The engine never tests it, and no sprite of the game sets it.
    /// </summary>
    Unused = 1 << 12,

    /// <summary>
    /// The surface holds a second image under the first, which takes one more byte per pixel. No sprite of the game sets it.
    /// </summary>
    SecondImage = 1 << 13,

    /// <summary>
    /// The sprite is a text font the engine builds at run time. No file sets it.
    /// </summary>
    Font = 1 << 14,

    /// <summary>
    /// Each pixel has one more byte, which makes the color of the second image 16 bits wide. No sprite of the game sets it.
    /// </summary>
    SecondImageWide = 1 << 15
}
