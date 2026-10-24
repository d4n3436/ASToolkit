namespace ASToolkit.Gui.Models;

/// <summary>
/// Represents a point of a map scene, in pixels from its top left corner, at a game time.
/// </summary>
/// <param name="X">The x position in scene pixels.</param>
/// <param name="Y">The y position in scene pixels.</param>
/// <param name="Time">The game time in milliseconds, or null when the scene is shown without animation.</param>
/// <param name="Zoom">The screen pixels per scene pixel.</param>
public sealed record ScenePoint(int X, int Y, int? Time, double Zoom);
