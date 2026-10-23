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
    public static unsafe WriteableBitmap ToBitmap(Image<Rgb24> image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
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
}
