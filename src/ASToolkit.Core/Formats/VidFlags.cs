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
    /// Unknown. Seen on every sprite with frames except those with the flags 0x36 and 0xB0.
    /// </summary>
    Unknown01 = 1 << 0,

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
    /// Unknown. Seen on every sprite with surfaces and on those with the flags 0x36 and 0xB0.
    /// </summary>
    Unknown20 = 1 << 5,

    /// <summary>
    /// Unknown. Only seen on the sprite with the flags 0x53.
    /// </summary>
    Unknown40 = 1 << 6,

    /// <summary>
    /// Unknown. Only seen on the omni-lights, which have the flags 0xB0.
    /// </summary>
    Unknown80 = 1 << 7,

    /// <summary>
    /// The surfaces are range coded.
    /// </summary>
    RangeCoded = 1 << 8,

    /// <summary>
    /// The surfaces are DXT compressed.
    /// </summary>
    Dxt = 1 << 11
}
