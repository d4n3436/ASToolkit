using System.Numerics;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents one vertex of a weather particle, in screen coordinates.
/// </summary>
/// <param name="Position">The position on the screen, with the depth from 0 to 1 as the third component.</param>
/// <param name="Color">The color of the vertex, packed as 0xAARRGGBB, or 0 while the particle isn't spawned.</param>
public record struct WeatherVertex(Vector3 Position, uint Color);
