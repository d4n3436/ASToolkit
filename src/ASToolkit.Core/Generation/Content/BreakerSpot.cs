using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents where the breaker hangs and the props of an original breaker room around it.
/// </summary>
/// <param name="Wall">The far wall edge the breaker hangs on.</param>
/// <param name="Position">The breaker's position.</param>
/// <param name="Props">The props, positioned relative to the breaker.</param>
public sealed record BreakerSpot(EdgeKey Wall, PointF Position, IReadOnlyList<PropPlacement> Props);
