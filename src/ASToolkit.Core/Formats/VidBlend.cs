namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies how the engine combines a sprite with what lies under it.
/// </summary>
public enum VidBlend : uint
{
    /// <summary>
    /// The sprite covers the pixels under it, or blends by the alpha of its palette.
    /// </summary>
    Alpha = 0,

    /// <summary>
    /// The sprite multiplies the pixels under it: twice the source times the destination.
    /// </summary>
    Multiply = 1,

    /// <summary>
    /// The sprite adds its color to the pixels under it.
    /// </summary>
    Additive = 2,

    /// <summary>
    /// The sprite adds its color to the pixels under it and is drawn with the lights.
    /// </summary>
    AdditiveLight = 4,

    /// <summary>
    /// The sprite adds its color times the pixels under it to them and is drawn with the lights.
    /// </summary>
    Brighten = 5,

    /// <summary>
    /// The sprite blends by alpha and does not write depth.
    /// </summary>
    AlphaNoDepthWrite = 6,

    /// <summary>
    /// The sprite blends by alpha and writes depth.
    /// </summary>
    AlphaDepthWrite = 7
}
