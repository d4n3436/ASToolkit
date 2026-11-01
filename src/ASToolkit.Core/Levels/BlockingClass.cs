namespace ASToolkit.Core.Levels;

/// <summary>
/// Specifies how an object stands in the player's way.
/// </summary>
public enum BlockingClass
{
    /// <summary>
    /// An object the player walks through.
    /// </summary>
    WalkThrough,

    /// <summary>
    /// An object that blocks the player until it is shot apart.
    /// </summary>
    Hittable,

    /// <summary>
    /// An object that always blocks the player.
    /// </summary>
    Solid
}
