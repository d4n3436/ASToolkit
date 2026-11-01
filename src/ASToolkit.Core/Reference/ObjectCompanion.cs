using ASToolkit.Core.Formats;

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
    /// Draws as many of the companion object as its rate gives for one anchor object, each as a different one of those lying by an anchor of the nearest facing.
    /// </summary>
    public AnchorSample[] Draw(Random rng, byte anchorFacing)
    {
        int count = (int)Rate + (rng.NextSingle() < Rate % 1 ? 1 : 0);
        if (count == 0)
        {
            return [];
        }

        int nearest = Samples.Min(sample => GetTurn(sample.AnchorFacing, anchorFacing));
        var samples = Samples.Where(sample => GetTurn(sample.AnchorFacing, anchorFacing) == nearest).ToArray();
        for (int index = 0; index < Math.Min(count, samples.Length); index++)
        {
            int pick = rng.Next(index, samples.Length);
            (samples[index], samples[pick]) = (samples[pick], samples[index]);
        }

        return samples.Take(count).ToArray();
    }

    private static int GetTurn(byte facing, byte other) => Math.Min(Math.Abs(facing - other), 256 - Math.Abs(facing - other));
}
