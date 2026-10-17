using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents where the breaker hangs and the props of an original breaker room around it.
/// </summary>
/// <param name="Wall">The far wall edge the breaker hangs on.</param>
/// <param name="X">The breaker's x position.</param>
/// <param name="Y">The breaker's y position.</param>
/// <param name="Props">The props, positioned relative to the breaker.</param>
public sealed record BreakerSpot(EdgeKey Wall, double X, double Y, IReadOnlyList<PropPlacement> Props);
