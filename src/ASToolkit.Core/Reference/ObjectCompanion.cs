using ASToolkit.Formats;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents an object that lies by an anchor object in the original levels far more often than chance would put it there.
/// </summary>
public sealed class ObjectCompanion
{
    /// <summary>
    /// Gets the ID of the companion object.
    /// </summary>
    public required KnownObject Id { get; init; }

    /// <summary>
    /// Gets how many of the companion object lie by each anchor object, on average.
    /// </summary>
    public required float Rate { get; init; }

    /// <summary>
    /// Gets how each companion object lay by the anchor object nearest it.
    /// </summary>
    public required IReadOnlyList<AnchorSample> Samples { get; init; }

    /// <summary>
    /// Draws samples of the companion object for one anchor object, as many as the rate gives, each a different one of the samples that lie by an anchor with the nearest direction.
    /// </summary>
    /// <param name="rng">The random number generator.</param>
    /// <param name="anchorDirection">The direction of the anchor object.</param>
    /// <returns>The drawn samples.</returns>
    public AnchorSample[] Draw(Random rng, Angle anchorDirection)
    {
        int count = (int)Rate + (rng.NextSingle() < Rate % 1 ? 1 : 0);
        if (count == 0)
        {
            return [];
        }

        int nearest = Samples.Min(sample => GetTurn(sample.AnchorDirection, anchorDirection));
        var samples = Samples.Where(sample => GetTurn(sample.AnchorDirection, anchorDirection) == nearest).ToArray();
        for (int i = 0; i < Math.Min(count, samples.Length); i++)
        {
            int pick = rng.Next(i, samples.Length);
            (samples[i], samples[pick]) = (samples[pick], samples[i]);
        }

        return samples.Take(count).ToArray();
    }

    private static int GetTurn(Angle direction, Angle other) => direction.MeasureTurnTo(other);
}
