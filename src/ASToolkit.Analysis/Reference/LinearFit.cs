using MathNet.Numerics.Statistics;

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
    public static FittedValue<LinearFit> Fit(IReadOnlyList<(float X, float Y)> samples)
    {
        double[] xs = samples.Select(sample => (double)sample.X).ToArray();
        double[] ys = samples.Select(sample => (double)sample.Y).ToArray();
        if (samples.Count < MinSamples)
        {
            float mean = samples.Count == 0 ? 0 : (float)ys.Mean();
            float spread = samples.Count < 2 ? 0 : (float)ys.StandardDeviation();
            return new FittedValue<LinearFit>(new LinearFit(mean, 0, spread), samples.Count, IsBackedOff: true);
        }

        (double intercept, double slope) = MathNet.Numerics.Fit.Line(xs, ys);
        double squares = xs.Zip(ys).Sum(pair => Math.Pow(pair.Second - intercept - slope * pair.First, 2));
        return new FittedValue<LinearFit>(new LinearFit((float)intercept, (float)slope, (float)Math.Sqrt(squares / (samples.Count - 2))), samples.Count, IsBackedOff: false);
    }
}
