using System.Drawing;
namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents a script trigger that fires when the player comes within a radius of a point.
/// </summary>
/// <param name="Center">The center of the area.</param>
/// <param name="Radius">The radius.</param>
public readonly record struct TriggerArea(Point Center, int Radius);
