namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the pixel format codes in the header of a sprite, which uses the Direct3D 9 format values.
/// </summary>
public enum VidPixelFormat : uint
{
    /// <summary>
    /// The header has no format code.
    /// </summary>
    None = 0,

    /// <summary>
    /// 24-bit color.
    /// </summary>
    R8G8B8 = 0x14,

    /// <summary>
    /// 32-bit color with an alpha channel.
    /// </summary>
    A8R8G8B8 = 0x15,

    /// <summary>
    /// 32-bit color with an unused byte.
    /// </summary>
    X8R8G8B8 = 0x16,

    /// <summary>
    /// 16-bit color with five bits for red, six for green and five for blue.
    /// </summary>
    R5G6B5 = 0x17,

    /// <summary>
    /// 16-bit color with an unused top bit.
    /// </summary>
    X1R5G5B5 = 0x18,

    /// <summary>
    /// 16-bit color with a one-bit alpha channel.
    /// </summary>
    A1R5G5B5 = 0x19,

    /// <summary>
    /// 16-bit color with four bits for each channel including alpha.
    /// </summary>
    A4R4G4B4 = 0x1A,

    /// <summary>
    /// 8-bit alpha.
    /// </summary>
    A8 = 0x1C,

    /// <summary>
    /// 8-bit palette indices.
    /// </summary>
    P8 = 0x29,

    /// <summary>
    /// 8-bit luminance.
    /// </summary>
    L8 = 0x32,

    /// <summary>
    /// DXT1 compressed blocks.
    /// </summary>
    Dxt1 = 0x31545844,

    /// <summary>
    /// DXT3 compressed blocks.
    /// </summary>
    Dxt3 = 0x33545844,

    /// <summary>
    /// DXT5 compressed blocks.
    /// </summary>
    Dxt5 = 0x35545844
}
