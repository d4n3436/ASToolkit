using System.Numerics;

namespace ASToolkit.Engine;

/// <summary>
/// Provides map geometry helpers for vectors.
/// </summary>
public static class VectorExtensions
{
    /// <summary>
    /// Projects a map position onto the screen plane, where height raises the point.
    /// </summary>
    /// <param name="position">The map position.</param>
    /// <returns>The position on the screen plane.</returns>
    public static Vector2 ToDrawPosition(this Vector3 position) => new(position.X, position.Y - position.Z);

    /// <summary>
    /// Measures the octagonal approximation of the distance between two points.
    /// </summary>
    /// <param name="from">The first point.</param>
    /// <param name="to">The second point.</param>
    /// <returns>The longer of the two axis offsets plus half the shorter one.</returns>
    [EngineRoutine(0x00410d40)]
    public static float MeasureOctagonalDistance(this Vector2 from, Vector2 to)
    {
        var offset = Vector2.Abs(to - from);

        return Math.Max(offset.X, offset.Y) + Math.Min(offset.X, offset.Y) * 0.5f;
    }
}
