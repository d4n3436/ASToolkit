using System.Collections;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class FrameMask
{
    private readonly BitArray _pixels;

    private FrameMask(int width, int height, int bottom, BitArray pixels)
    {
        Width = width;
        Height = height;
        Bottom = bottom;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Bottom { get; }

    public static FrameMask Read(VidResource file, int frameIndex)
    {
        var pixels = new BitArray(file.Width * file.Height);
        int bottom = 0;
        foreach (var run in file.GetFrame(frameIndex))
        {
            if ((uint)run.Y >= file.Height)
                continue;

            bottom = Math.Max(bottom, run.Y + 1);
            for (int index = 0; index < run.Count; index++)
            {
                int x = run.X + index;

                if ((uint)x < file.Width)
                {
                    pixels[run.Y * file.Width + x] = true;
                }
            }
        }

        return new FrameMask(file.Width, file.Height, bottom, pixels);
    }

    public bool Covers(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height && _pixels[y * Width + x];
}
