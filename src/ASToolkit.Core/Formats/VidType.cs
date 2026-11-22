namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the type flags in the header of a sprite. They pick the sprite class the engine loads it as and how its pixels are stored.
/// </summary>
[Flags]
public enum VidType : short
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// The frame records are runs of 8-bit values (palette indexes and 8-bit depth). Sprites without the flag, such as the ones with 0x36, use 16-bit runs.
    /// </summary>
    Runs8Bit = 1 << 0,

    /// <summary>
    /// The sprite blends with what lies under it using the alpha byte of the palette instead of covering it.
    /// </summary>
    Blended = 1 << 1,

    /// <summary>
    /// The pixels carry a depth value for the depth buffer.
    /// </summary>
    ZBuffer = 1 << 2,

    /// <summary>
    /// The pixel colors are palette indexes.
    /// </summary>
    Palette = 1 << 3,

    /// <summary>
    /// The sprite uses the current format version.
    /// </summary>
    CurrentVersion = 1 << 4,

    /// <summary>
    /// The sprite is a hardware sprite whose pixels are 32-bit surfaces in a SURF chunk.
    /// </summary>
    Hardware = 1 << 5,

    /// <summary>
    /// The sprite is a surface sprite drawn from the cached frame records of its CADR chunk instead of frames.
    /// </summary>
    Surface = 1 << 6,

    /// <summary>
    /// The sprite is an omni-light, a 2 x 2 sprite whose frames are BGRA colors.
    /// </summary>
    Light = 1 << 7,

    /// <summary>
    /// The surfaces are range coded.
    /// </summary>
    RangeCoded = 1 << 8,

    /// <summary>
    /// Each cached frame record starts with a 12-byte vector.
    /// </summary>
    FrameVector = 1 << 9,

    /// <summary>
    /// The sprite stores several palettes.
    /// </summary>
    PaletteVariants = 1 << 10,

    /// <summary>
    /// The surfaces of a SURF chunk are DXT-compressed, or each CADR record header holds one more 16-bit value.
    /// </summary>
    SurfaceVariant = 1 << 11,

    /// <summary>
    /// The sprite is loaded as a sprite class of its own that the engine puts on the hardware layer.
    /// </summary>
    HardwareLayer = 1 << 12,

    /// <summary>
    /// The surface holds a normal map under the image, one more byte per pixel.
    /// </summary>
    Normals = 1 << 13,

    /// <summary>
    /// The sprite is a text font the engine builds at run time. No file of the game sets it.
    /// </summary>
    Font = 1 << 14,

    /// <summary>
    /// Each pixel has one more material byte. With the normal map the value under the image is 16 bits wide.
    /// </summary>
    Material = unchecked((short)(1 << 15))
}
