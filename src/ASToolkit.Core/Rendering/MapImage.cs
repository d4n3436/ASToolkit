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
    /// Gets the world x of the image's left edge.
    /// </summary>
    public required double OriginX { get; init; }

    /// <summary>
    /// Gets the screen y (world y minus height) of the image's top edge.
    /// </summary>
    public required double OriginY { get; init; }

    /// <summary>
    /// Gets the image pixels per world pixel.
    /// </summary>
    public required double Scale { get; init; }

    /// <summary>
    /// Gets the number of sprites left out because their sprite file has a format the renderer doesn't draw.
    /// </summary>
    public required int Undrawn { get; init; }

    /// <summary>
    /// Returns the image position of a world point at a height.
    /// </summary>
    public PointF ToImage(double x, double y, double z = 0) => new((float)((x - OriginX) * Scale), (float)((y - z - OriginY) * Scale));

    /// <inheritdoc/>
    public void Dispose() => Image.Dispose();
}
