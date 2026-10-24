using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents the way the player walks through the quest steps of a level.
/// </summary>
public sealed class WalkedRoute
{
    /// <summary>
    /// Gets the stretches in walking order.
    /// </summary>
    public required IReadOnlyList<RouteSegment> Segments { get; init; }

    /// <summary>
    /// Gets the quest steps in the order the walk reaches them.
    /// </summary>
    public required IReadOnlyList<ProgressionStep> Steps { get; init; }

    /// <summary>
    /// Gets the walked length in pixels.
    /// </summary>
    public required int WalkedPixels { get; init; }

    /// <summary>
    /// Gets the number of quest steps the walk could not reach.
    /// </summary>
    public required int UnreachedSteps { get; init; }
}
