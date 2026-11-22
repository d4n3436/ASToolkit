namespace ASToolkit.Core.Engine;

/// <summary>
/// Specifies how a picture is converted into a sprite file.
/// </summary>
[Flags]
public enum PictureConvertOptions
{
    /// <summary>
    /// The picture is converted into a software sprite file.
    /// </summary>
    None = 0,

    /// <summary>
    /// The sprite file is made for the hardware renderer.
    /// </summary>
    Hardware = 1 << 0,

    /// <summary>
    /// The records of the sprite file are range coded.
    /// </summary>
    RangeCoded = 1 << 1,

    /// <summary>
    /// The frame records of the sprite file hold the outline of the opaque pixels of the frame.
    /// </summary>
    Outline = 1 << 2,

    /// <summary>
    /// The surfaces of the sprite file are DXT-compressed.
    /// </summary>
    SurfaceVariant = 1 << 3,

    /// <summary>
    /// The sprite file is made of layers for the hardware renderer.
    /// </summary>
    HardwareLayer = 1 << 4,

    /// <summary>
    /// The outline of a frame is simplified with a larger tolerance.
    /// </summary>
    CoarseOutline = 1 << 5,

    /// <summary>
    /// The frame records of the sprite file hold no pixel rows, so only the outline is left.
    /// </summary>
    OutlineOnly = 1 << 6
}
