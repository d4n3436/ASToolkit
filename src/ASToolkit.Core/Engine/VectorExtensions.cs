using System.Numerics;

namespace ASToolkit.Core.Engine;

internal static class VectorExtensions
{
    public static Vector2 ToDrawPosition(this Vector3 position) => new(position.X, position.Y - position.Z);

    [EngineRoutine(0x00410d40)]
    public static float MeasureOctagonalDistance(this Vector2 from, Vector2 to)
    {
        var offset = Vector2.Abs(to - from);

        return Math.Max(offset.X, offset.Y) + Math.Min(offset.X, offset.Y) * 0.5f;
    }
}
