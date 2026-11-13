using System.Numerics;

namespace ASToolkit.Core.Engine;

internal static class VectorExtensions
{
    // Measures the engine's octagonal distance, the larger axis offset plus half the smaller
    public static float MeasureDistance(this Vector2 from, Vector2 to)
    {
        var offset = Vector2.Abs(to - from);

        return Math.Max(offset.X, offset.Y) + Math.Min(offset.X, offset.Y) * 0.5f;
    }
}
