using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents where the breaker hangs.
/// </summary>
/// <param name="Wall">The far wall edge the breaker hangs on.</param>
/// <param name="Position">The breaker's position.</param>
public sealed record BreakerSpot(EdgeKey Wall, PointF Position);
