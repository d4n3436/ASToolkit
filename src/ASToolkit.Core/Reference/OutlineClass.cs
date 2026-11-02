namespace ASToolkit.Core.Reference;

/// <summary>
/// Specifies the outline class of a room, read from what its bounding box is missing.
/// </summary>
public enum OutlineClass
{
    /// <summary>
    /// A full box.
    /// </summary>
    Box,

    /// <summary>
    /// A box with holes inside it.
    /// </summary>
    BoxWithPillars,

    /// <summary>
    /// A box with one large corner missing.
    /// </summary>
    L,

    /// <summary>
    /// A box with one small corner missing.
    /// </summary>
    NotchedCorner,

    /// <summary>
    /// A box with one large bite out of a side.
    /// </summary>
    Bay,

    /// <summary>
    /// A box with one small bite out of a side.
    /// </summary>
    NotchedSide,

    /// <summary>
    /// A box with the two corners of one side missing.
    /// </summary>
    T,

    /// <summary>
    /// A box with three or four corners missing.
    /// </summary>
    Cross,

    /// <summary>
    /// Any other outline.
    /// </summary>
    Irregular
}
