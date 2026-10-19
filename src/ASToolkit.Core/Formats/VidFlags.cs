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
    /// The surfaces are DXT compressed.
    /// </summary>
    Dxt = 1 << 11,

    /// <summary>
    /// The sprite uses an extended format; the engine loads it with a larger sprite class. Not set on any sprite of the game.
    /// </summary>
    Extended = 1 << 12
}
