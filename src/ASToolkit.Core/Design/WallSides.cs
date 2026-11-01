namespace ASToolkit.Core.Design;

/// <summary>
/// Specifies the walls around a floor cell.
/// </summary>
[Flags]
public enum WallSides
{
    /// <summary>
    /// No wall.
    /// </summary>
    None = 0,

    /// <summary>
    /// The upper <c>\</c> wall, toward +U.
    /// </summary>
    UpperBack = 1,

    /// <summary>
    /// The upper <c>/</c> wall, toward −V.
    /// </summary>
    UpperSlash = 2,

    /// <summary>
    /// The lower <c>\</c> wall, toward −U.
    /// </summary>
    LowerBack = 4,

    /// <summary>
    /// The lower <c>/</c> wall, toward +V.
    /// </summary>
    LowerSlash = 8,

    /// <summary>
    /// The two upper walls, the ones facing the camera.
    /// </summary>
    Far = UpperBack | UpperSlash,

    /// <summary>
    /// The two lower walls.
    /// </summary>
    Near = LowerBack | LowerSlash
}
