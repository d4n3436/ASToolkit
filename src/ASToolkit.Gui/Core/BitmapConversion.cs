using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Gui.Core;

/// <summary>
/// Provides conversions of toolkit images into Avalonia bitmaps.
/// </summary>
public static class BitmapConversion
{
    /// <summary>
    /// Copies an opaque image into a new bitmap.
    /// </summary>
    /// <param name="image">The image to copy.</param>
    /// <returns>The bitmap that holds the copy.</returns>
    public static unsafe WriteableBitmap ToBitmap(Image<Rgb24> image)
    {
        var bitmap = CreateOpaque(new PixelSize(image.Width, image.Height));
        using var buffer = bitmap.Lock();
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var source = rows.GetRowSpan(y);
                var target = new Span<Bgra32>((byte*)buffer.Address + y * buffer.RowBytes, rows.Width);
                for (int x = 0; x < source.Length; x++)
                {
                    target[x] = new Bgra32(source[x].R, source[x].G, source[x].B);
                }
            }
        });

        return bitmap;
    }

    /// <summary>
    /// Creates an opaque 32-bit bitmap at the default resolution.
    /// </summary>
    /// <param name="size">The size of the bitmap in pixels.</param>
    /// <returns>The new bitmap.</returns>
    public static WriteableBitmap CreateOpaque(PixelSize size) => new(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    /// <summary>
    /// Copies rows of pixels into a bitmap of the same width.
    /// </summary>
    /// <param name="bitmap">The bitmap to write to.</param>
    /// <param name="pixels">The pixels, row by row.</param>
    public static unsafe void Write(WriteableBitmap bitmap, ReadOnlySpan<Bgra32> pixels)
    {
        int width = bitmap.PixelSize.Width;
        using var buffer = bitmap.Lock();
        for (int row = 0; row < bitmap.PixelSize.Height; row++)
        {
            pixels.Slice(row * width, width).CopyTo(new Span<Bgra32>((byte*)buffer.Address + row * buffer.RowBytes, width));
        }
    }
}
