namespace ASToolkit.Core.Engine;

/// <summary>
/// Specifies the factor a blend multiplies the source or the destination with.
/// </summary>
public enum BlendFactor
{
    /// <summary>
    /// The factor is zero.
    /// </summary>
    Zero = 1,

    /// <summary>
    /// The factor is one.
    /// </summary>
    One = 2,

    /// <summary>
    /// The factor is the source color.
    /// </summary>
    SourceColor = 3,

    /// <summary>
    /// The factor is one minus the source color.
    /// </summary>
    InverseSourceColor = 4,

    /// <summary>
    /// The factor is the source alpha.
    /// </summary>
    SourceAlpha = 5,

    /// <summary>
    /// The factor is one minus the source alpha.
    /// </summary>
    InverseSourceAlpha = 6,

    /// <summary>
    /// The factor is the destination alpha.
    /// </summary>
    DestinationAlpha = 7,

    /// <summary>
    /// The factor is one minus the destination alpha.
    /// </summary>
    InverseDestinationAlpha = 8,

    /// <summary>
    /// The factor is the destination color.
    /// </summary>
    DestinationColor = 9,

    /// <summary>
    /// The factor is one minus the destination color.
    /// </summary>
    InverseDestinationColor = 10
}
