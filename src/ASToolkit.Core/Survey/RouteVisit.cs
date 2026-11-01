using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a stay of the route in one place.
/// </summary>
/// <param name="Place">The place number, or 0 for none.</param>
/// <param name="Enter">How far along the route, in map pixels, the route enters the place.</param>
/// <param name="Leave">How far along the route, in map pixels, the route leaves the place.</param>
/// <param name="Phase">The gate phase when the route enters the place.</param>
public sealed record RouteVisit(int Place, int Enter, int Leave, RoutePhase Phase);
