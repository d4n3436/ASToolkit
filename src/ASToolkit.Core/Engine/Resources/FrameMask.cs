using System.Collections;
using System.Drawing;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class FrameMask
{
    private readonly BitArray _pixels;

    private FrameMask(Size size, int bottom, BitArray pixels)
    {
        Size = size;
        Bottom = bottom;
        _pixels = pixels;
    }

    public Size Size { get; }

    public int Bottom { get; }

    public static FrameMask Read(VidResource file, int frameIndex)
    {
        var size = file.Size;
        var pixels = new BitArray(size.Width * size.Height);
        int bottom = 0;
        foreach (var run in file.GetFrame(frameIndex))
        {
            if (run.Position.Y < 0 || run.Position.Y >= size.Height)
                continue;

            bottom = Math.Max(bottom, run.Position.Y + 1);
            for (int i = 0; i < run.Count; i++)
            {
                int x = run.Position.X + i;

                if (x >= 0 && x < size.Width)
                {
                    pixels[run.Position.Y * size.Width + x] = true;
                }
            }
        }

        return new FrameMask(size, bottom, pixels);
    }

    public bool Covers(Point pixel) => new Rectangle(Point.Empty, Size).Contains(pixel) && _pixels[pixel.Y * Size.Width + pixel.X];
}
