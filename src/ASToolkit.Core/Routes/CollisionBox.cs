namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents the area around a solid object that the player cannot enter.
/// </summary>
/// <param name="MinX">The left edge.</param>
/// <param name="MaxX">The right edge.</param>
/// <param name="MinY">The top edge.</param>
/// <param name="MaxY">The bottom edge.</param>
public readonly record struct CollisionBox(double MinX, double MaxX, double MinY, double MaxY)
{
    /// <summary>
    /// Returns whether a point lies in the box.
    /// </summary>
    public bool Contains(double x, double y) => MinX <= x && x <= MaxX && MinY <= y && y <= MaxY;
}
