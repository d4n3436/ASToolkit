using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a stay of the route in one place.
/// </summary>
/// <param name="Place">The place number, or 0 for none.</param>
/// <param name="Enter">How far along the route it enters, in map pixels.</param>
/// <param name="Leave">How far along the route it leaves, in map pixels.</param>
/// <param name="Phase">The gate phase when it enters.</param>
public sealed record RouteVisit(int Place, int Enter, int Leave, RoutePhase Phase);
