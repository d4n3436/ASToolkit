using System.Drawing;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const uint TrueColorMask = 0xffffff;
    private const int TrueColorPixelSize = 3;

    [EngineRoutine(0x00430fc0)]
    internal void SaveScreen(IGameFileSystem fileSystem, string path, Rectangle area)
    {
        var picture = new PictureImage(string.Empty, Error);
        picture.Resize(area.Size, TrueColorPixelSize);
        for (int y = 0; y < area.Height; y++)
        {
            for (int x = 0; x < area.Width; x++)
            {
                LockDepth();
                uint color;

                if (x + area.X < Viewport.Left || Viewport.Right <= x + area.X || y + area.Y < Viewport.Top || Viewport.Bottom <= y + area.Y)
                {
                    color = OpaqueBlack;
                }
                else if (!Flags.HasFlag(GraphicsFlags.Color32))
                {
                    color = Screen!.Format.Expand(Screen.HighColorPixels[Screen.Pitch * (y + area.Y) + x + area.X]);
                }
                else
                {
                    color = Screen!.TrueColorPixels[Screen.Pitch * (y + area.Y) + x + area.X];
                }

                picture.SetPixel(new Point(x, y), color & TrueColorMask);
            }
        }

        picture.SaveTga(fileSystem, path, new Rectangle(0, 0, -1, -1));
    }
}
