namespace ASToolkit.Formats;

/// <summary>
/// Specifies the pixel format codes in the header of a sprite, using the Direct3D 9 format values.
/// </summary>
public enum VidPixelFormat
{
    /// <summary>
    /// The header doesn't have a format code.
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
    /// 16-bit palette indexes with an eight-bit alpha channel.
    /// </summary>
    A8P8 = 0x28,

    /// <summary>
    /// 8-bit palette indexes.
    /// </summary>
    P8 = 0x29,

    /// <summary>
    /// 8-bit luminance.
    /// </summary>
    L8 = 0x32,

    /// <summary>
    /// 16-bit luminance with an eight-bit alpha channel.
    /// </summary>
    A8L8 = 0x33,

    /// <summary>
    /// 8-bit luminance with four bits for luminance and four for alpha.
    /// </summary>
    A4L4 = 0x34,

    /// <summary>
    /// 16-bit signed bump map offsets with eight bits for each axis.
    /// </summary>
    V8U8 = 0x3C,

    /// <summary>
    /// 16-bit bump map offsets with five signed bits for each axis and six bits of luminance.
    /// </summary>
    L6V5U5 = 0x3D,

    /// <summary>
    /// 32-bit bump map offsets with eight signed bits for each axis, eight bits of luminance and an unused byte.
    /// </summary>
    X8L8V8U8 = 0x3E,

    /// <summary>
    /// 32-bit signed bump map offsets with eight bits for each of four axes.
    /// </summary>
    Q8W8V8U8 = 0x3F,

    /// <summary>
    /// 32-bit signed bump map offsets with sixteen bits for each axis.
    /// </summary>
    V16U16 = 0x40,

    /// <summary>
    /// 32-bit signed bump map offsets with ten bits for the first axis, eleven for the second and eleven for the third.
    /// </summary>
    W11V11U10 = 0x41,

    /// <summary>
    /// 16-bit depth values the CPU can lock.
    /// </summary>
    D16Lockable = 0x46,

    /// <summary>
    /// 32-bit depth values.
    /// </summary>
    D32 = 0x47,

    /// <summary>
    /// 15-bit depth values with a 1-bit stencil.
    /// </summary>
    D15S1 = 0x49,

    /// <summary>
    /// 24-bit depth values with an 8-bit stencil.
    /// </summary>
    D24S8 = 0x4B,

    /// <summary>
    /// 24-bit depth values in 32 bits.
    /// </summary>
    D24X8 = 0x4D,

    /// <summary>
    /// 16-bit depth values.
    /// </summary>
    D16 = 0x50,

    /// <summary>
    /// Vertex buffer data.
    /// </summary>
    VertexData = 0x64,

    /// <summary>
    /// 16-bit vertex indexes.
    /// </summary>
    Index16 = 0x65,

    /// <summary>
    /// 32-bit vertex indexes.
    /// </summary>
    Index32 = 0x66,

    /// <summary>
    /// DXT1 compressed blocks.
    /// </summary>
    Dxt1 = 0x31545844,

    /// <summary>
    /// DXT2 compressed blocks, with the colors premultiplied by the alpha.
    /// </summary>
    Dxt2 = 0x32545844,

    /// <summary>
    /// DXT3 compressed blocks.
    /// </summary>
    Dxt3 = 0x33545844,

    /// <summary>
    /// DXT4 compressed blocks, with the colors premultiplied by the alpha.
    /// </summary>
    Dxt4 = 0x34545844,

    /// <summary>
    /// DXT5 compressed blocks.
    /// </summary>
    Dxt5 = 0x35545844
}
