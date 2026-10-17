namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents a script trigger that fires when the player comes within a radius of a point.
/// </summary>
/// <param name="X">The x of the point.</param>
/// <param name="Y">The y of the point.</param>
/// <param name="Radius">The radius.</param>
public readonly record struct TriggerArea(int X, int Y, int Radius);
