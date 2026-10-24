using System.Numerics;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents a stretch of a walked route.
/// </summary>
/// <param name="Kind">How the player covers the stretch.</param>
/// <param name="Points">The points of the stretch in the world, at the height of the ground.</param>
public sealed record RouteSegment(RouteSegmentKind Kind, IReadOnlyList<Vector3> Points);
