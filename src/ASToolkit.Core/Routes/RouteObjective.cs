namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents an objective in the order the tour completed it.
/// </summary>
/// <param name="Kind">The objective kind.</param>
/// <param name="X">The x position.</param>
/// <param name="Y">The y position.</param>
/// <param name="Progress">The share of the tour walked when the objective was completed.</param>
/// <param name="TriggerRadius">The script's trigger radius, when the objective is a blackout.</param>
public sealed record RouteObjective(ObjectiveKind Kind, float X, float Y, float Progress, float TriggerRadius = 0);