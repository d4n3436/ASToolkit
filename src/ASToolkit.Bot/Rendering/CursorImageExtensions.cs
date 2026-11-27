using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Bot.Rendering;

/// <summary>
/// Provides extension methods for <see cref="CursorImage"/>.
/// </summary>
public static class CursorImageExtensions
{
    private const int MaxStackPixels = 4096;
    private const int AlphaShift = 24;

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

        var size = cursor.Size;
        int count = size.Width * size.Height;
        var image = count <= MaxStackPixels ? stackalloc uint[count] : new uint[count];
        cursor.CopyTo(image);
        var target = MemoryMarshal.Cast<Bgra32, uint>(pixels);
        var corner = new Point(hotspot.X - cursor.Hotspot.X, hotspot.Y - cursor.Hotspot.Y);
        for (int row = 0; row < size.Height; row++)
        {
            for (int column = 0; column < size.Width; column++)
            {
                var point = new Point(corner.X + column, corner.Y + row);
                uint color = image[row * size.Width + column];

                if (color >> AlphaShift != 0 && point.X >= 0 && point.X < frame.Width && point.Y >= 0 && point.Y < frame.Height)
                {
                    target[point.Y * frame.Width + point.X] = color;
                }
            }
        }
    }
}
