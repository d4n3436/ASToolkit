namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents how a drawing is combined with the colors underneath it, through a source factor and a destination factor.
/// </summary>
/// <param name="Source">The factor of the drawing's colors.</param>
/// <param name="Destination">The factor of the colors underneath.</param>
public readonly record struct QuadBlend(BlendFactor Source, BlendFactor Destination)
{
    /// <summary>
    /// Gets the blend that replaces the colors underneath.
    /// </summary>
    public static QuadBlend None => new(BlendFactor.One, BlendFactor.Zero);

    /// <summary>
    /// Gets the blend that weights the drawing by its alpha and the colors underneath by the inverse of it.
    /// </summary>
    public static QuadBlend Alpha => new(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);

    /// <summary>
    /// Gets the blend that weights the drawing by its alpha and adds it to the colors underneath.
    /// </summary>
    public static QuadBlend AlphaAdd => new(BlendFactor.SourceAlpha, BlendFactor.One);

    /// <summary>
    /// Gets the blend that multiplies the drawing by the colors underneath and adds the product to them.
    /// </summary>
    public static QuadBlend ColorAdd => new(BlendFactor.DestinationColor, BlendFactor.One);
}
