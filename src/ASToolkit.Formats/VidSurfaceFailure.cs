namespace ASToolkit.Formats;

/// <summary>
/// Specifies why the textures of a hardware sprite could not all be read.
/// </summary>
public enum VidSurfaceFailure
{
    /// <summary>
    /// Every texture was read.
    /// </summary>
    None,

    /// <summary>
    /// The pixels of a texture could not be read.
    /// </summary>
    Decode,

    /// <summary>
    /// The depth of a texture could not be read.
    /// </summary>
    DepthDecode,

    /// <summary>
    /// The depth of a texture does not have two bytes for each pixel.
    /// </summary>
    DepthSize
}
