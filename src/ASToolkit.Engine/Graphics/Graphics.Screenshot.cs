using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const uint TrueColorMask = 0xffffff;
    private const byte TrueColorBits = 24;
    private const byte TgaDescriptor = 1;
    private const int TrueColorPixelSize = 3;

    private static ReadOnlySpan<byte> SaveSizeText => "size in SaveTGA"u8;

    private static ReadOnlySpan<byte> SaveNoPictureText => "SaveTGA-not picture"u8;

    [EngineRoutine(0x00430fc0)]
    internal void SaveScreen(IGameFileSystem fileSystem, string path, Rectangle area)
    {
        uint[] picture = new uint[area.Width * area.Height];
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

                picture[area.Width * y + x] = color & TrueColorMask;
            }
        }

        SaveTga(fileSystem, picture, area.Size, path, new Rectangle(0, 0, -1, -1));
    }

    [EngineRoutine(0x00426f90)]
    private void SaveTga(IGameFileSystem fileSystem, uint[] picture, Size pictureSize, string path, Rectangle area)
    {
        if (picture.Length == 0)
        {
            Logger?.Error("PICTURE ''"u8, LogError.Text, 0, SaveNoPictureText);
            return;
        }

        if (area.Height == -1)
        {
            area.Height = pictureSize.Height;
        }

        if (area.Width == -1)
        {
            area.Width = pictureSize.Width;
        }

        if (area.X + area.Width > pictureSize.Width || area.Y + area.Height > pictureSize.Height)
        {
            Logger?.Error("PICTURE ''"u8, LogError.Invalid, 0, SaveSizeText);
            return;
        }

        var file = path.Length == 0 ? null : fileSystem.OpenWrite(path, false);

        if (file is null)
        {
            Logger?.Error("PICTURE ''"u8, LogError.Open, 0, Encoding.ASCII.GetBytes(path));
            return;
        }

        using (file)
        {
            var header = new TgaHeader(TgaImageType.TrueColor, area.Size, TrueColorBits, TgaDescriptor);
            file.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<TgaHeader>(in header)));

            for (int y = area.Y + area.Height - 1; y >= area.Y; y--)
            {
                for (int x = area.X; x < area.X + area.Width; x++)
                {
                    uint pixel = picture[pictureSize.Width * y + x] & TrueColorMask;
                    file.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(in pixel))[..TrueColorPixelSize]);
                }
            }
        }
    }
}
