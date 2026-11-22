using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents an image of a whole map with the mapping from map coordinates to image pixels.
/// </summary>
public sealed class MapImage : IDisposable
{
    private const uint Black = 0xff000000;

    /// <summary>
    /// Gets the image.
    /// </summary>
    public required Image<Rgb24> Image { get; init; }

    /// <summary>
    /// Gets the point of the map at the image's top-left corner, in map pixels.
    /// </summary>
    public required Vector2 Origin { get; init; }

    /// <summary>
    /// Gets the scale of the image, in image pixels per map pixel.
    /// </summary>
    public required float Scale { get; init; }

    /// <summary>
    /// Draws a whole map one screen at a time, with the camera on that screen.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="scale">The scale of the image, in image pixels per map pixel, above 0 and at most 1.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not above 0 and at most 1.</exception>
    public static MapImage Render(Map map, float scale)
    {
        return Render(map, GetMapBounds(map), scale);
    }

    /// <summary>
    /// Draws an area of a map one screen at a time, with the camera on that screen.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="area">The area, in map pixels. The part outside the map is drawn black.</param>
    /// <param name="scale">The scale of the image, in image pixels per map pixel, above 0 and at most 1.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not above 0 and at most 1, or <paramref name="area"/> is empty.</exception>
    public static MapImage Render(Map map, System.Drawing.Rectangle area, float scale)
    {
        if (scale is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be above 0 and at most 1.");

        if (area.Width <= 0 || area.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(area), area, "The area must not be empty.");

        var bounds = System.Drawing.Rectangle.Intersect(area, GetMapBounds(map));
        var tile = System.Drawing.Size.Round(map.Session.ScreenSize);
        var screen = new Screen(tile, true);
        var pixels = new Bgra32[tile.Width * tile.Height];
        var image = new Image<Rgb24>(Math.Max(1, (int)MathF.Ceiling(area.Width * scale)), Math.Max(1, (int)MathF.Ceiling(area.Height * scale)));

        for (int top = bounds.Top / tile.Height * tile.Height; top < bounds.Bottom; top += tile.Height)
        {
            for (int left = bounds.Left / tile.Width * tile.Width; left < bounds.Right; left += tile.Width)
            {
                var part = System.Drawing.Rectangle.Intersect(new System.Drawing.Rectangle(left, top, tile.Width, tile.Height), bounds);
                if (part.IsEmpty)
                    continue;

                var camera = new Vector2(left, top);
                Graphics.Clear(screen, Black);
                for (var layer = ObjectLayer.Ground; layer < ObjectLayer.Menu; layer++)
                {
                    map.DrawLayer(screen, camera, layer);
                }

                screen.CopyTo(pixels);

                var offset = new Point((int)MathF.Round((part.Left - area.Left) * scale), (int)MathF.Round((part.Top - area.Top) * scale));
                var target = new Size(
                    Math.Max(1, (int)MathF.Round((part.Right - area.Left) * scale) - offset.X),
                    Math.Max(1, (int)MathF.Round((part.Bottom - area.Top) * scale) - offset.Y));

                using var drawn = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(pixels, tile.Width, tile.Height);
                drawn.Mutate(context =>
                {
                    context.Crop(new Rectangle(part.Left - left, part.Top - top, part.Width, part.Height));
                    if (target.Width != part.Width || target.Height != part.Height)
                    {
                        context.Resize(target);
                    }
                });

                using var opaque = drawn.CloneAs<Rgb24>();
                image.Mutate(context => context.DrawImage(opaque, offset, 1f));
            }
        }

        return new MapImage { Image = image, Origin = new Vector2(area.Left, area.Top), Scale = scale };
    }

    /// <summary>
    /// Converts a point on the map to its place in the image.
    /// </summary>
    /// <param name="point">The point, in map pixels.</param>
    /// <returns>The place in the image, in image pixels.</returns>
    public PointF ToImage(Vector2 point) => (point - Origin) * Scale;

    private static System.Drawing.Rectangle GetMapBounds(Map map)
    {
        var size = map.Movement.MapSize;
        return new System.Drawing.Rectangle(0, 0, (int)MathF.Ceiling(size.Width), (int)MathF.Ceiling(size.Height));
    }

    /// <inheritdoc/>
    public void Dispose() => Image.Dispose();
}
