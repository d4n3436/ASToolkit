using System.Numerics;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents one vertex of the mesh a hardware layer frame is drawn with.
/// </summary>
/// <param name="Position">The column and row on the canvas, and the height above the base of the sprite.</param>
/// <param name="TextureCoordinate">The position in the texture, as a fraction of its size.</param>
public readonly record struct HardwareLayerVertex(Vector3 Position, Vector2 TextureCoordinate);
