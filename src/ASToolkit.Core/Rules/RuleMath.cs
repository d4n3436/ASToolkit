namespace ASToolkit.Core.Rules;

internal static class RuleMath
{
    public static bool IsWithin(double value, double target, double tolerance) => Math.Abs(value - target) <= tolerance * target;

    public static double GetQuantile(IReadOnlyList<double> sorted, double q) => sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];

    public static double GetDistance(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
}
