using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents a rendered map with the mapping from world coordinates to image pixels.
/// </summary>
public sealed class MapImage : IDisposable
{
    /// <summary>
    /// Gets the image.
    /// </summary>
    public required Image<Rgb24> Image { get; init; }

    /// <summary>
    /// Gets the screen point of the image's top left corner, in map pixels with heights subtracted.
    /// </summary>
    public required Vector2 Origin { get; init; }

    /// <summary>
    /// Gets the image pixels per world pixel.
    /// </summary>
    public required float Scale { get; init; }

    /// <summary>
    /// Gets the number of sprites left out because their sprite frame could not be decoded.
    /// </summary>
    public required int Undrawn { get; init; }

    /// <summary>
    /// Converts a point on the floor of the map to a point of the image.
    /// </summary>
    /// <param name="point">The point, in map pixels.</param>
    /// <returns>The point of the image, in image pixels from its top left corner.</returns>
    public PointF ToImage(Vector2 point) => (point - Origin) * Scale;

    /// <summary>
    /// Converts a map position above the floor to a point of the image.
    /// </summary>
    /// <param name="position">The position, in map pixels with its height above the floor.</param>
    /// <returns>The point of the image, in image pixels from its top left corner.</returns>
    public PointF ToImage(Vector3 position) => ToImage(new Vector2(position.X, position.Y - position.Z));

    /// <inheritdoc/>
    public void Dispose() => Image.Dispose();
}
