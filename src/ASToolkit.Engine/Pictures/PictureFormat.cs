namespace ASToolkit.Engine;

/// <summary>
/// Specifies the file format a picture channel loaded its image from.
/// </summary>
public enum PictureFormat
{
    /// <summary>
    /// The channel has no image file.
    /// </summary>
    None = 0,

    /// <summary>
    /// The image is read from TGA files.
    /// </summary>
    Tga = 1,

    /// <summary>
    /// The image is read from BMP files.
    /// </summary>
    Bmp = 2,

    /// <summary>
    /// The image is read from FLC animation files.
    /// </summary>
    Flc = 3,

    /// <summary>
    /// The image is read from compressed Z files.
    /// </summary>
    Z = 5
}
