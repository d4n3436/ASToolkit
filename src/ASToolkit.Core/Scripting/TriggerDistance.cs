using System.Numerics;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Provides the distance the Alien Shooter 2 scripts compare with the radius of a trigger area.
/// </summary>
public static class TriggerDistance
{
    private const float LongWeight = 0.961f;
    private const float ShortWeight = 0.398f;

    /// <summary>
    /// Gets the distance between two points on the map, ignoring the height.
    /// </summary>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    public static float Get(Vector3 first, Vector3 second)
    {
        float x = MathF.Abs(first.X - second.X);
        float y = MathF.Abs(first.Y - second.Y);
        return y < x ? x * LongWeight + y * ShortWeight : x * ShortWeight + y * LongWeight;
    }

    /// <summary>
    /// Tells whether a point is inside a trigger area.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="center">The center of the area.</param>
    /// <param name="radius">The radius of the area.</param>
    public static bool Contains(Vector3 point, Vector3 center, float radius) => Get(point, center) < radius;
}
