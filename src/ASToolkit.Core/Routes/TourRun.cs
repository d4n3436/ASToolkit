using System.Numerics;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents a stretch of the tour walked under one gate state.
/// </summary>
/// <param name="Phase">The gate state.</param>
/// <param name="Points">The cell centers of the stretch in walking order, in map pixels.</param>
public sealed record TourRun(RoutePhase Phase, IReadOnlyList<Vector2> Points);
