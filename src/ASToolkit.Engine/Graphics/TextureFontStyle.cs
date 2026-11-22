namespace ASToolkit.Engine;

/// <summary>
/// Specifies how the glyphs of a <see cref="TextureFont"/> are rendered and drawn.
/// </summary>
[Flags]
public enum TextureFontStyle
{
    /// <summary>
    /// Regular glyphs of a variable-pitch font, drawn without the depth test.
    /// </summary>
    None = 0,

    /// <summary>
    /// The glyphs are bold.
    /// </summary>
    Bold = 1 << 0,

    /// <summary>
    /// The glyphs are italic.
    /// </summary>
    Italic = 1 << 1,

    /// <summary>
    /// The text is drawn with the depth test.
    /// </summary>
    DepthTest = 1 << 2,

    /// <summary>
    /// The font is chosen from the fixed-pitch fonts.
    /// </summary>
    FixedPitch = 1 << 3
}
