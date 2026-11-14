using System.Drawing;
using ASToolkit.Core.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Provides extension methods for <see cref="CursorImage"/>.
/// </summary>
public static class CursorImageExtensions
{
    /// <summary>
    /// Draws the cursor with its hotspot at a pixel of a frame, clipped to the frame.
    /// </summary>
    /// <param name="cursor">The cursor to draw.</param>
    /// <param name="pixels">The pixels of the frame, row by row.</param>
    /// <param name="frame">The size of the frame in pixels.</param>
    /// <param name="hotspot">The pixel of the frame the hotspot lands on.</param>
    public static void Draw(this CursorImage cursor, Span<Bgra32> pixels, Size frame, Point hotspot)
    {
        if (!cursor.IsValid)
            return;

        var corner = new Point(hotspot.X - cursor.Hotspot.X, hotspot.Y - cursor.Hotspot.Y);
        for (int row = 0; row < cursor.Size.Height; row++)
        {
            for (int column = 0; column < cursor.Size.Width; column++)
            {
                var target = new Point(corner.X + column, corner.Y + row);

                if ((uint)target.X < (uint)frame.Width && (uint)target.Y < (uint)frame.Height && cursor.TryGetPixel(new Point(column, row), out var color))
                {
                    pixels[target.Y * frame.Width + target.X] = new Bgra32(color.R, color.G, color.B, byte.MaxValue);
                }
            }
        }
    }
}
