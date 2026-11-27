using System.Drawing;
using ASToolkit.Engine;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Bot.Rendering;

/// <summary>
/// Provides extension methods for <see cref="Screen"/>.
/// </summary>
public static class ScreenExtensions
{
    /// <param name="screen">The screen to copy.</param>
    extension(Screen screen)
    {
        /// <summary>
        /// Writes the back buffer as opaque pixels, row by row from the top.
        /// </summary>
        /// <param name="pixels">The pixels to write, at least as many as the screen holds.</param>
        /// <exception cref="ArgumentException"><paramref name="pixels"/> is smaller than the screen.</exception>
        public void CopyTo(Span<Bgra32> pixels) => screen.CopyTo(pixels, new Rectangle(Point.Empty, screen.Size));

        /// <summary>
        /// Writes an area of the back buffer as opaque pixels, row by row from the top.
        /// </summary>
        /// <param name="pixels">The pixels to write, at least as many as the area holds.</param>
        /// <param name="area">The area of the screen to copy, in pixels.</param>
        /// <exception cref="ArgumentException"><paramref name="pixels"/> is smaller than <paramref name="area"/>, or <paramref name="area"/> is not inside the screen.</exception>
        public void CopyTo(Span<Bgra32> pixels, Rectangle area)
        {
            if (pixels.Length < area.Width * area.Height)
                throw new ArgumentException("The pixel buffer is smaller than the area.", nameof(pixels));

            if (!new Rectangle(Point.Empty, screen.Size).Contains(area))
                throw new ArgumentException("The area is not inside the screen.", nameof(area));

            for (int y = 0; y < area.Height; y++)
            {
                int row = (area.Top + y) * screen.Pitch + area.Left;
                var target = pixels.Slice(y * area.Width, area.Width);
                if (screen.IsTrueColor)
                {
                    var colors = screen.TrueColorPixels.Slice(row, area.Width);
                    for (int x = 0; x < area.Width; x++)
                    {
                        target[x] = ToBgra(colors[x]);
                    }
                }
                else
                {
                    var colors = screen.HighColorPixels.Slice(row, area.Width);
                    for (int x = 0; x < area.Width; x++)
                    {
                        target[x] = ToBgra(screen.Format.Expand(colors[x]));
                    }
                }
            }
        }
    }

    private static Bgra32 ToBgra(uint color) => new((byte)(color >> 16), (byte)(color >> 8), (byte)color, byte.MaxValue);
}
