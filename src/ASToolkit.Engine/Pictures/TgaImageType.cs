namespace ASToolkit.Engine;

/// <summary>
/// Specifies the image type field of a TGA file, which combines a base type with a run-length flag.
/// </summary>
[Flags]
public enum TgaImageType : byte
{
    /// <summary>
    /// The file has no image data.
    /// </summary>
    None = 0,

    /// <summary>
    /// The pixels are indexes into a color map.
    /// </summary>
    ColorMapped = 1 << 0,

    /// <summary>
    /// The pixels are colors.
    /// </summary>
    TrueColor = 1 << 1,

    /// <summary>
    /// The pixels are shades of gray.
    /// </summary>
    Grayscale = ColorMapped | TrueColor,

    /// <summary>
    /// The pixels are run-length encoded.
    /// </summary>
    RunLength = 1 << 3
}
