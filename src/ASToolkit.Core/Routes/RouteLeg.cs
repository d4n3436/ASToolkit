namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents a stretch of the tour walked under one gate state.
/// </summary>
/// <param name="Phase">The gate state.</param>
/// <param name="Pixels">The walked length.</param>
public sealed record RouteLeg(RoutePhase Phase, int Pixels);
