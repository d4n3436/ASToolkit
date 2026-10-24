namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents the area around a solid object that the player cannot enter.
/// </summary>
/// <param name="MinX">The left edge.</param>
/// <param name="MaxX">The right edge.</param>
/// <param name="MinY">The top edge.</param>
/// <param name="MaxY">The bottom edge.</param>
public readonly record struct CollisionBox(float MinX, float MaxX, float MinY, float MaxY)
{
    /// <summary>
    /// Returns whether a point lies in the box.
    /// </summary>
    public bool Contains(float x, float y) => MinX <= x && x <= MaxX && MinY <= y && y <= MaxY;
}
