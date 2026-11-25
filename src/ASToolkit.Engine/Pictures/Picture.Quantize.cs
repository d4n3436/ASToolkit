using System.Buffers;
using System.Drawing;

namespace ASToolkit.Engine;

public partial class Picture
{
    private const int MatrixSize = PaletteSize * PaletteSize;
    private const uint HalfAlphaLimit = 0xef000000;

    [EngineRoutine(0x0042b3a0)]
    private void QuantizePalette()
    {
        _paletteMap = new byte[PaletteMapSize];
        _palette[0] = 0;
        var size = Colors.Image.Size;
        int count = 1;
        Span<int> distances = stackalloc int[PaletteSize];
        int[] matrix = ArrayPool<int>.Shared.Rent(MatrixSize);
        try
        {
            for (int frame = 0; frame < Colors.Image.FrameCount; frame++)
            {
                for (int y = 0; y < size.Height; y++)
                {
                    for (int x = 0; x < size.Width; x++)
                    {
                        var point = new Point(x, y);
                        if (!IsOpaque(point))
                            continue;

                        uint color = GetColor(point);
                        if (_paletteMap[(int)((((color & 0xfc00 | color >> 3 & 0x1f0000) >> 2 | color & 0xf8) >> 3) * 0x10 + (color >> 28))] != 0)
                            continue;

                        int nearest = 0;
                        bool isMatched = false;
                        for (int entry = 0; entry < count; entry++)
                        {
                            uint other = _palette[entry];
                            int red = (int)(other >> 19 & 0x1f) - (int)(color >> 19 & 0x1f);
                            int blue = (int)(other >> 3 & 0x1f) - (int)(color >> 3 & 0x1f);
                            int alpha = (int)(other / 0xf000000) - (int)(color / 0xf000000);
                            int green = (int)(other >> 10 & 0x3f) - (int)(color >> 10 & 0x3f);
                            int distance = red * red * 0x36640 + blue * blue * 0x790 + alpha * alpha * 0x1a5e + green * green * 0x3840;
                            distances[entry] = distance;
                            if (distance == 0)
                            {
                                SetPaletteIndex(color, (byte)entry);
                                isMatched = true;

                                break;
                            }

                            if (distance < distances[nearest])
                            {
                                nearest = entry;
                            }
                        }

                        if (isMatched)
                            continue;

                        if (count < PaletteSize)
                        {
                            for (int entry = 0; entry < count; entry++)
                            {
                                matrix[count * PaletteSize + entry] = distances[entry];
                                matrix[entry * PaletteSize + count] = distances[entry];
                            }

                            _palette[count] = color;
                            SetPaletteIndex(color, (byte)count);
                            count++;
                            continue;
                        }

                        int row = 1;
                        int column = 0;
                        for (int first = 1; first < PaletteSize; first++)
                        {
                            for (int second = 0; second < first; second++)
                            {
                                if (matrix[first * PaletteSize + second] >= matrix[row * PaletteSize + column])
                                    continue;

                                row = first;
                                column = second;
                            }
                        }

                        if (matrix[row * PaletteSize + column] >= distances[nearest])
                        {
                            SetPaletteIndex(color, (byte)nearest);
                            continue;
                        }

                        SetPaletteIndex(_palette[column], (byte)nearest);
                        _palette[column] = color;
                        SetPaletteIndex(color, (byte)column);
                        for (int entry = 0; entry < PaletteSize; entry++)
                        {
                            matrix[column * PaletteSize + entry] = distances[entry];
                            matrix[entry * PaletteSize + column] = distances[entry];
                        }
                    }
                }

                Advance();
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(matrix);
        }

        Rewind();

        Span<int> counts = stackalloc int[PaletteSize];
        Span<int> reds = stackalloc int[PaletteSize];
        Span<int> greens = stackalloc int[PaletteSize];
        Span<int> blues = stackalloc int[PaletteSize];
        Span<int> alphas = stackalloc int[PaletteSize];
        for (int cell = 0; cell < PaletteMapSize; cell++)
        {
            int i = _paletteMap[cell];
            if (i == 0)
                continue;

            counts[i]++;
            int color = cell >> 4;
            reds[i] += color >> 8 & 0xf8;
            greens[i] += color >> 3 & 0xfc;
            blues[i] += (color & 0x1f) << 3;
            alphas[i] += (cell & 0xf) * 0x10 + 0xf;
        }

        for (int i = 0; i < PaletteSize; i++)
        {
            if (counts[i] == 0)
                continue;

            _palette[i] = Graphics.PackColor(alphas[i] / counts[i], reds[i] / counts[i], greens[i] / counts[i], blues[i] / counts[i]);
        }

        if (!Flags.HasFlag(PictureFlags.Alpha))
            return;

        for (int i = 0; i < count; i++)
        {
            uint color = _palette[i];
            if ((color & 0xff000000) > HalfAlphaLimit)
            {
                color |= 0xff000000;
            }
            else
            {
                color = color & 0xffffff | Math.Min((color >> 28) << 4, MaxChannel) << AlphaShift;
            }

            _palette[i] = color;
            if ((color & 0xff000000) == 0)
                continue;

            uint alpha = color >> AlphaShift;
            uint blue = Math.Min((color & ChannelMask) * MaxChannel / alpha, MaxChannel);
            uint green = Math.Min((color >> 8 & ChannelMask) * MaxChannel / alpha, MaxChannel);
            uint red = Math.Min((color >> 16 & ChannelMask) * MaxChannel / alpha, MaxChannel);
            _palette[i] = Graphics.PackColor((int)alpha, (int)red, (int)green, (int)blue);
        }
    }
}
