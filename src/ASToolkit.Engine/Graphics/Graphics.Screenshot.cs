using System.Drawing;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{

    [EngineRoutine(0x00430fc0)]
    internal void SaveScreen(string path, Rectangle area)
    {
        var channel = new PictureChannel(_session.Map!.FileSystem, Error, area.Size, PictureFormat.Tga);
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

                channel.SetColor(new Point(x, y), color);
            }
        }

        channel.Image.SaveTga(path, new Rectangle(0, 0, -1, -1));
        channel.Dispose();
    }
}
