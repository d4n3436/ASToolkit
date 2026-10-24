namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents the measurements of the gates of a level.
/// </summary>
public sealed class GateMeasurement
{
    /// <summary>
    /// Gets the share of the reachable floor behind locked doors at the start, or null when no doors lock.
    /// </summary>
    public float? LockedShare { get; init; }

    /// <summary>
    /// Gets the share of the reachable floor behind the fences, or null when there are none.
    /// </summary>
    public float? FencedOffShare { get; init; }

    /// <summary>
    /// Gets the walk from the key (breaker or generator) to the nearest gate it opens, or null when no gate is reachable.
    /// </summary>
    public int? KeyToGatePixels { get; init; }

    /// <summary>
    /// Gets the progress of the blackout trigger, or null.
    /// </summary>
    public float? BlackoutProgress { get; init; }

    /// <summary>
    /// Gets the walk from the blackout to each breaker with the doors locked; -1 when a breaker is out of reach.
    /// </summary>
    public IReadOnlyList<int> BreakerFromBlackoutPixels { get; init; } = [];
}
