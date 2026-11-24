namespace ASToolkit.Engine;

/// <summary>
/// Specifies how a texture is created.
/// </summary>
[Flags]
public enum TextureFlags
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// The texture is updated often.
    /// </summary>
    Dynamic = 1 << 0,

    /// <summary>
    /// The texels are kept in system memory.
    /// </summary>
    SystemMemory = 1 << 1,

    /// <summary>
    /// The texture is a render target.
    /// </summary>
    RenderTarget = 1 << 2,

    /// <summary>
    /// The texture has an alpha channel in its palette.
    /// </summary>
    AlphaPalette = 1 << 3,

    /// <summary>
    /// The texture needs a layout with alpha.
    /// </summary>
    HasAlpha = 1 << 4
}
