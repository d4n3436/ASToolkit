namespace ASToolkit.Core.Levels;

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
    UpperBack = 1 << 0,

    /// <summary>
    /// The upper <c>/</c> wall, toward −V.
    /// </summary>
    UpperSlash = 1 << 1,

    /// <summary>
    /// The lower <c>\</c> wall, toward −U.
    /// </summary>
    LowerBack = 1 << 2,

    /// <summary>
    /// The lower <c>/</c> wall, toward +V.
    /// </summary>
    LowerSlash = 1 << 3,

    /// <summary>
    /// The two upper walls, the ones facing the camera.
    /// </summary>
    Far = UpperBack | UpperSlash,

    /// <summary>
    /// The two lower walls.
    /// </summary>
    Near = LowerBack | LowerSlash
}
