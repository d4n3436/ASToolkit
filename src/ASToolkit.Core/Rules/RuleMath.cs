namespace ASToolkit.Core.Rules;

internal static class RuleMath
{
    public static bool IsWithin(float value, float target, float tolerance) => Math.Abs(value - target) <= tolerance * target;

    public static float GetQuantile(IReadOnlyList<float> sorted, float q) => sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];

    public static float GetDistance(float x1, float y1, float x2, float y2) => MathF.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
}
