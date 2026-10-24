using System.Drawing;

namespace ASToolkit.Gui.Models;

/// <summary>
/// Represents a point of a map scene, in pixels from its top left corner, at a game time.
/// </summary>
/// <param name="Position">The position in scene pixels.</param>
/// <param name="Time">The game time in milliseconds, or null when the scene is shown without animation.</param>
/// <param name="Zoom">The screen pixels per scene pixel.</param>
public sealed record ScenePoint(Point Position, int? Time, double Zoom);
