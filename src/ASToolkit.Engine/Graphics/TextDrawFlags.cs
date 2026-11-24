namespace ASToolkit.Engine;

/// <summary>
/// Specifies how <see cref="D3DFont.Draw"/> draws text.
/// </summary>
[Flags]
public enum TextDrawFlags
{
    /// <summary>
    /// The glyphs are sampled with point filtering.
    /// </summary>
    None = 0,

    /// <summary>
    /// The glyphs are sampled with linear filtering.
    /// </summary>
    Filtered = 1 << 2
}
