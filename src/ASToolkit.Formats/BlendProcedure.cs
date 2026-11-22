namespace ASToolkit.Formats;

/// <summary>
/// Specifies the blend procedure of a sprite, the blend factors and depth setup of its draw.
/// </summary>
public enum BlendProcedure
{
    /// <summary>
    /// The sprite is alpha tested, and a blended sprite mixes with the pixels under it by its source alpha.
    /// </summary>
    Alpha = 0,

    /// <summary>
    /// The sprite multiplies the pixels under it by twice its color.
    /// </summary>
    Multiply = 1,

    /// <summary>
    /// The sprite adds its color to the pixels under it.
    /// </summary>
    Additive = 2,

    /// <summary>
    /// The sprite adds its color to the pixels under it, drawn on the light layer over everything already drawn.
    /// </summary>
    AdditiveLight = 4,

    /// <summary>
    /// The sprite adds its color times the pixels under it to them, drawn on the light layer.
    /// </summary>
    Brighten = 5,

    /// <summary>
    /// The sprite is drawn like <see cref="Alpha"/> without writing depth.
    /// </summary>
    AlphaNoDepthWrite = 6,

    /// <summary>
    /// The sprite is drawn like <see cref="Alpha"/>, but it writes depth and is drawn over everything already drawn.
    /// </summary>
    AlphaDepthWrite = 7
}
