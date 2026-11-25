namespace ASToolkit.Engine;

/// <summary>
/// Specifies which images a picture has and how its sprite file is made.
/// </summary>
[Flags]
public enum PictureFlags
{
    /// <summary>
    /// The picture has no flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// The picture has an image with colors.
    /// </summary>
    Color = 1 << 0,

    /// <summary>
    /// The picture has an image with alpha values.
    /// </summary>
    Alpha = 1 << 1,

    /// <summary>
    /// The picture has an image with depth values.
    /// </summary>
    Depth = 1 << 2,

    /// <summary>
    /// The pixels of the sprite file are palette indexes.
    /// </summary>
    Palette = 1 << 3,

    /// <summary>
    /// The sprite file is of the current version.
    /// </summary>
    CurrentVersion = 1 << 4,

    /// <summary>
    /// The sprite file is made for the hardware renderer.
    /// </summary>
    Hardware = 1 << 5,

    /// <summary>
    /// The picture is a light, whose sprite file holds one color for each frame.
    /// </summary>
    Light = 1 << 7,

    /// <summary>
    /// The records of the sprite file are range coded.
    /// </summary>
    RangeCoded = 1 << 8,

    /// <summary>
    /// The surfaces of the sprite file are DXT-compressed.
    /// </summary>
    SurfaceVariant = 1 << 11,

    /// <summary>
    /// The sprite file is made of layers for the hardware renderer.
    /// </summary>
    HardwareLayer = 1 << 12,

    /// <summary>
    /// The frame records of the sprite file hold the outline of the opaque pixels of the frame.
    /// </summary>
    Outline = 1 << 17,

    /// <summary>
    /// The outline of a frame is simplified with a larger tolerance.
    /// </summary>
    CoarseOutline = 1 << 19,

    /// <summary>
    /// The frame records of the sprite file hold no pixel rows, so only the outline is left.
    /// </summary>
    OutlineOnly = 1 << 20
}
