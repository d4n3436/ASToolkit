using System.Drawing;

namespace ASToolkit.Gui.Models;

/// <summary>
/// Represents a point of the map under the pointer.
/// </summary>
/// <param name="Position">The position, in map pixels.</param>
/// <param name="Zoom">The screen pixels per map pixel.</param>
public sealed record MapPoint(Point Position, double Zoom);
