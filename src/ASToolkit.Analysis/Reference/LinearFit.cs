using System.Numerics;

namespace ASToolkit.Analysis.Reference;

/// <summary>
/// Represents a straight line fitted by least squares to samples, such as the logarithm of monster hit points by level, with the standard
/// deviation of the samples around it for drawing values near the line.
/// </summary>
/// <param name="Intercept">The value of the line at zero.</param>
/// <param name="Slope">The change of the line per unit.</param>
/// <param name="Spread">The standard deviation of the samples around the line.</param>
public readonly record struct LinearFit(float Intercept, float Slope, float Spread)
{
    /// <summary>
    /// Represents the minimum number of samples a line is fitted to.
    /// </summary>
    public const int MinSamples = 8;

    /// <summary>
    /// Returns the value of the line at a point.
    /// </summary>
    /// <param name="x">The point.</param>
    /// <returns>The value of the line at the point.</returns>
    public float Evaluate(float x) => Intercept + Slope * x;

    /// <summary>
    /// Fits a line to samples by least squares, or a flat line at their mean if there are too few samples for a line.
    /// </summary>
    /// <param name="samples">The samples as x and y values.</param>
    /// <returns>The fitted line with the number of samples.</returns>
    public static FittedValue<LinearFit> Fit(ReadOnlySpan<Vector2> samples)
    {
        if (samples.Length < MinSamples)
        {
            return new FittedValue<LinearFit>(FitFlat(samples), samples.Length, IsBackedOff: true);
        }

        var mean = GetMean(samples);
        var products = Vector2.Zero;
        foreach (var sample in samples)
        {
            var offset = sample - mean;
            products += offset.X * offset;
        }

        float slope = products.Y / products.X;
        var line = new LinearFit(mean.Y - slope * mean.X, slope, 0);
        return new FittedValue<LinearFit>(line with { Spread = line.MeasureSpread(samples, 2) }, samples.Length, IsBackedOff: false);
    }

    /// <summary>
    /// Fits a flat line at the mean y value of samples, with their sample standard deviation as the spread.
    /// </summary>
    /// <param name="samples">The samples as x and y values.</param>
    /// <returns>The flat line, at zero when there are no samples.</returns>
    public static LinearFit FitFlat(ReadOnlySpan<Vector2> samples)
    {
        var line = new LinearFit(GetMean(samples).Y, 0, 0);
        return samples.Length < 2 ? line : line with { Spread = line.MeasureSpread(samples, 1) };
    }

    private static Vector2 GetMean(ReadOnlySpan<Vector2> samples)
    {
        var sum = Vector2.Zero;
        foreach (var sample in samples)
        {
            sum += sample;
        }

        return samples.Length == 0 ? sum : sum / samples.Length;
    }

    private float MeasureSpread(ReadOnlySpan<Vector2> samples, int terms)
    {
        float squares = 0;
        foreach (var sample in samples)
        {
            float residual = sample.Y - Evaluate(sample.X);
            squares += residual * residual;
        }

        return MathF.Sqrt(squares / (samples.Length - terms));
    }
}
