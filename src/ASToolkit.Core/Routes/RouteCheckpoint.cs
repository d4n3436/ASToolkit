namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents a checkpoint trigger and where it sits on the tour.
/// </summary>
/// <param name="X">The x position.</param>
/// <param name="Y">The y position.</param>
/// <param name="Progress">The share of the tour walked when the player first gets there.</param>
public sealed record RouteCheckpoint(int X, int Y, double Progress);
