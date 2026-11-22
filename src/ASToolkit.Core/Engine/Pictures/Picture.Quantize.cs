using System.Buffers;
using System.Drawing;

namespace ASToolkit.Core.Engine;

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
                        if (_paletteMap[GetPaletteMapIndex(color)] != 0)
                            continue;

                        int nearest = 0;
                        bool isExact = false;
                        for (int entry = 0; entry < count; entry++)
                        {
                            int distance = GetColorDistance(_palette[entry], color);
                            distances[entry] = distance;
                            if (distance == 0)
                            {
                                SetPaletteIndex(color, (byte)entry);
                                isExact = true;
                                break;
                            }

                            if (distance < distances[nearest])
                            {
                                nearest = entry;
                            }
                        }

                        if (isExact)
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
                                if (matrix[first * PaletteSize + second] < matrix[row * PaletteSize + column])
                                {
                                    row = first;
                                    column = second;
                                }
                            }
                        }

                        if (matrix[row * PaletteSize + column] < distances[nearest])
                        {
                            SetPaletteIndex(_palette[column], (byte)nearest);
                            _palette[column] = color;
                            SetPaletteIndex(color, (byte)column);
                            for (int entry = 0; entry < PaletteSize; entry++)
                            {
                                matrix[column * PaletteSize + entry] = distances[entry];
                                matrix[entry * PaletteSize + column] = distances[entry];
                            }
                        }
                        else
                        {
                            SetPaletteIndex(color, (byte)nearest);
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

            int blue = Math.Clamp(blues[i] / counts[i], 0, (int)MaxChannel);
            int green = Math.Clamp(greens[i] / counts[i], 0, (int)MaxChannel);
            int red = Math.Clamp(reds[i] / counts[i], 0, (int)MaxChannel);
            int alpha = Math.Clamp(alphas[i] / counts[i], 0, (int)MaxChannel);
            _palette[i] = (uint)(((alpha << 8 | red) << 8 | green) << 8 | blue);
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
            _palette[i] = ((alpha << 8 | red) << 8 | green) << 8 | blue;
        }
    }
}
