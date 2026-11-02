using MathNet.Numerics.Distributions;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents a log-normal distribution fitted to positive samples, its draws kept within the samples' range.
/// </summary>
/// <param name="Mu">The mean of the samples' logarithms.</param>
/// <param name="Sigma">The standard deviation of the samples' logarithms.</param>
/// <param name="Low">The logarithm of the smallest sample.</param>
/// <param name="High">The logarithm of the largest sample.</param>
public readonly record struct LogNormalFit(double Mu, double Sigma, double Low, double High)
{
    private const double SmallestSample = 1e-3;
    private const double SingleSampleSigma = 0.3;

    /// <summary>
    /// Fits a log-normal distribution to the samples.
    /// </summary>
    public static LogNormalFit Fit(IReadOnlyCollection<double> samples)
    {
        double[] logs = samples.Select(sample => Math.Log(Math.Max(SmallestSample, sample))).ToArray();
        double mu = logs.Average();
        double sigma = logs.Length > 1 ? Math.Sqrt(logs.Sum(value => (value - mu) * (value - mu)) / logs.Length) : SingleSampleSigma;
        return new LogNormalFit(mu, sigma, logs.Min(), logs.Max());
    }

    /// <summary>
    /// Draws a value from the distribution, clamped to the samples' range.
    /// </summary>
    public double Draw(Random rng) => Math.Exp(Math.Clamp(Normal.Sample(rng, Mu, Sigma), Low, High));
}
