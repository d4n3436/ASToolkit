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
    /// Unknown. Set on every sprite with a <c>DATA</c> chunk of frames, including the texture ones, except those with the flags 0x36 and 0xB0.
    /// </summary>
    Unknown01 = 0x01,

    /// <summary>
    /// The sprite blends with what lies under it using the alpha byte of the palette instead of covering it.
    /// </summary>
    Blended = 0x02,

    /// <summary>
    /// The pixels carry a depth value.
    /// </summary>
    Depth = 0x04,

    /// <summary>
    /// The pixel colors are palette indices.
    /// </summary>
    Palette = 0x08,

    /// <summary>
    /// The sprite uses the current format version.
    /// </summary>
    CurrentVersion = 0x10,

    /// <summary>
    /// Unknown. Set on every sprite with a <c>SURF</c> chunk of surfaces, and on those with the flags 0x36 and 0xB0, which have none.
    /// </summary>
    Unknown20 = 0x20,

    /// <summary>
    /// Unknown. Only seen on the sprite with the flags 0x53, which has a <c>CADR</c> chunk instead of frames.
    /// </summary>
    Unknown40 = 0x40,

    /// <summary>
    /// Unknown. Only seen on the sprites with the flags 0xB0, the omni-lights.
    /// </summary>
    Unknown80 = 0x80,

    /// <summary>
    /// The surfaces are range coded.
    /// </summary>
    RangeCoded = 0x100,

    /// <summary>
    /// The surfaces are DXT compressed.
    /// </summary>
    Dxt = 0x800
}
