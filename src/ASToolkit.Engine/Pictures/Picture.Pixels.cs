using System.Drawing;

namespace ASToolkit.Engine;

public partial class Picture
{
    private const uint ChannelMask = 0xff;
    private const int AlphaShift = 24;
    private const uint OpaqueMask = 0xffffff;
    private const uint MaxChannel = 0xff;
    private const uint FourBitMax = 0xf;
    private const short NoDepth = 0x400;
    private const ushort EmptyDepth = 0x8000;
    private const int PaletteSize = 256;
    private const int PaletteMapSize = 0x100000;

    private readonly uint[] _palette = new uint[PaletteSize];
    private byte[] _paletteMap = [];

    [EngineRoutine(0x00428fc0)]
    private uint GetColor(Point point)
    {
        if (!Colors.Image.Contains(point))
            return 0xff000000;

        if (!Alpha.Image.HasPixels)
            return Colors.Image.GetColor(point);

        return (Alpha.Image.GetColor(point) & ChannelMask) << AlphaShift | Colors.Image.GetColor(point) & OpaqueMask;
    }

    [EngineRoutine(0x004290f0)]
    private uint GetAlpha(Point point)
    {
        if (!Colors.Image.Contains(point))
            return 0;

        if (Alpha.Image.HasPixels)
            return Alpha.Image.GetColor(point) & ChannelMask;

        return Colors.Image.BytesPerPixel == 4 ? Colors.Image.GetPixel(point) >> AlphaShift : MaxChannel;
    }

    [EngineRoutine(0x00429160)]
    private short GetDepth(Point point)
    {
        if (!Colors.Image.Contains(point))
            return NoDepth;

        uint depth = 0;
        if (Depth.Image.HasPixels && Depth.Format == PictureFormat.Z)
        {
            depth = Depth.Image.GetPixel(point);
            if ((ushort)depth == EmptyDepth)
                return 0;
        }

        return (short)(depth + NoDepth);
    }

    [EngineRoutine(0x004291d0)]
    private short GetRegionDepth(Rectangle area)
    {
        var corner = new Point(area.Right - 1, area.Bottom - 1);
        if (IsOpaque(corner))
            return GetDepth(corner);

        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                if (IsOpaque(new Point(x, y)))
                    return GetDepth(new Point(x, y));
            }
        }

        return 0;
    }

    [EngineRoutine(0x00429270)]
    private int FindPaletteIndex(uint color)
    {
        if (_paletteMap.Length == 0)
        {
            int index = 0;
            int nearest = 9999999;
            for (int entry = 0; entry < PaletteSize; entry++)
            {
                uint other = _palette[entry];
                int red = (int)(color >> 19 & 0x1f) - (int)(other >> 19 & 0x1f);
                int blue = (int)(color >> 3 & 0x1f) - (int)(other >> 3 & 0x1f);
                int alpha = (int)(color / 0xf000000) - (int)(other / 0xf000000);
                int green = (int)(color >> 10 & 0x3f) - (int)(other >> 10 & 0x3f);
                int distance = red * red * 0x36640 + blue * blue * 0x790 + alpha * alpha * 0x1a5e + green * green * 0x3840;
                if (distance < nearest)
                {
                    index = entry;
                    nearest = distance;
                }
            }

            return index;
        }

        return _paletteMap[(int)((((color & 0xfc00 | color >> 3 & 0x1f0000) >> 2 | color & 0xf8) >> 3) * 0x10 + (color >> 28))];
    }

    [EngineRoutine(0x004293f0)]
    private void SetPaletteIndex(uint color, byte index)
    {
        if (_paletteMap.Length == 0)
            return;

        int position = (int)((((color & 0xfc00 | color >> 3 & 0x1f0000) >> 2 | color & 0xf8) >> 3) * 0x10 + (color >> 28));
        byte current = _paletteMap[position];
        if (current != 0 && current != index)
        {
            for (int cell = 0; cell < PaletteMapSize; cell++)
            {
                if (_paletteMap[cell] == current)
                {
                    _paletteMap[cell] = index;
                }
            }

            return;
        }

        _paletteMap[position] = index;
    }

    [EngineRoutine(0x00429480)]
    private uint GetHighColor(Point point)
    {
        if (!Colors.Image.Contains(point))
            return 0;

        if (!Flags.HasFlag(PictureFlags.HardwareLayer))
        {
            if (!Flags.HasFlag(PictureFlags.Palette))
            {
                uint color = GetColor(point);
                uint alpha = color >> AlphaShift;
                uint red = color >> 16 & ChannelMask;
                uint green = color >> 8 & ChannelMask;
                uint blue = color & ChannelMask;
                if (Flags.HasFlag(PictureFlags.Color) && Flags.HasFlag(PictureFlags.Alpha))
                {
                    if ((color & 0xf0000000) == 0)
                        return 0;

                    uint red4 = Math.Min(red * MaxChannel / alpha >> 4, FourBitMax);
                    uint green4 = Math.Min(green * MaxChannel / alpha >> 4, FourBitMax);
                    uint blue4 = Math.Min(blue * MaxChannel / alpha >> 4, FourBitMax);
                    return (((red4 | alpha & 0xf0) << 4 | green4) << 4) | blue4;
                }

                if (Flags.HasFlag(PictureFlags.Depth) && Flags.HasFlag(PictureFlags.Alpha))
                    return blue >> 4 | (red & 0xf0) << 4 | green & 0xf0;

                return ((red & 0xf8) << 5 | green & 0xfc) << 3 | blue >> 3;
            }

            if (Flags.HasFlag(PictureFlags.Alpha))
                return (uint)FindPaletteIndex(GetColor(point));
        }
        else if (!Flags.HasFlag(PictureFlags.Palette))
        {
            uint color = GetColor(point);
            uint alpha = color >> AlphaShift;
            uint blue = color & ChannelMask;
            if (!IsOpaque(point))
            {
                alpha = 0;
            }

            if (Flags.HasFlag(PictureFlags.Color) && Flags.HasFlag(PictureFlags.Alpha))
            {
                uint red = Math.Min((color >> 16 & ChannelMask) * MaxChannel / alpha, MaxChannel);
                uint green = Math.Min((color >> 8 & ChannelMask) * MaxChannel / alpha, MaxChannel);
                blue = Math.Min(blue * MaxChannel / alpha, MaxChannel);
                return ((alpha << 8 | red) << 8 | green) << 8 | blue;
            }

            return (((alpha & 0xffffff80) << 1 | color >> 16 & 0xf8) << 5 | color >> 8 & 0xf8) << 2 | blue >> 3;
        }

        return Colors.Image.GetPixel(point);
    }

    [EngineRoutine(0x00429760)]
    private Rectangle GetBounds(bool hasRows, bool hasColumns)
    {
        var size = Colors.Image.Size;
        int left = 0;
        int top = 0;
        int right = 0;
        int bottom = 0;
        if (hasRows)
        {
            top = -1;
            for (int y = 0; y < size.Height && top == -1; y++)
            {
                for (int x = 0; x < size.Width; x++)
                {
                    if (IsOpaque(new Point(x, y)))
                    {
                        top = y;
                    }
                }
            }

            bottom = -1;
            for (int y = size.Height - 1; y >= 0 && bottom == -1; y--)
            {
                for (int x = 0; x < size.Width; x++)
                {
                    if (IsOpaque(new Point(x, y)))
                    {
                        bottom = y;
                    }
                }
            }

            if (bottom >= 0)
            {
                bottom++;
            }

            if (bottom <= top || top < 0 || bottom < 0)
            {
                top = 0;
                bottom = 0;
            }
        }

        if (hasColumns)
        {
            left = -1;
            for (int x = 0; x < size.Width && left == -1; x++)
            {
                for (int y = 0; y < size.Height; y++)
                {
                    if (IsOpaque(new Point(x, y)))
                    {
                        left = x;
                    }
                }
            }

            right = -1;
            for (int x = size.Width - 1; x >= 0 && right == -1; x--)
            {
                for (int y = 0; y < size.Height; y++)
                {
                    if (IsOpaque(new Point(x, y)))
                    {
                        right = x;
                    }
                }
            }

            if (right >= 0)
            {
                right++;
            }

            if (right <= left || left < 0 || right < 0)
            {
                top = 0;
                bottom = 0;
            }
        }

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    [EngineRoutine(0x0042c790)]
    private bool IsOpaque(Point point)
    {
        if (!Colors.Image.Contains(point))
            return false;

        if (Depth.Image.HasPixels && Depth.Format == PictureFormat.Z)
        {
            if ((ushort)Depth.Image.GetPixel(point) == EmptyDepth)
                return false;

            if (Flags.HasFlag(PictureFlags.Hardware) && Flags.HasFlag(PictureFlags.Alpha) && Flags.HasFlag(PictureFlags.Depth))
                return Flags.HasFlag(PictureFlags.Color) ? GetAlpha(point) >> 4 != 0 : (GetHighColor(point) & 0xfff) != 0;

            return true;
        }

        if (Flags.HasFlag(PictureFlags.Alpha) && Flags.HasFlag(PictureFlags.Color))
            return GetAlpha(point) >> 4 != 0;

        return (GetColor(point) & OpaqueMask) != 0;
    }

    [EngineRoutine(0x0042c860)]
    private bool HasOpaque(Rectangle area)
    {
        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                if (IsOpaque(new Point(x, y)))
                    return true;
            }
        }

        return false;
    }
}
