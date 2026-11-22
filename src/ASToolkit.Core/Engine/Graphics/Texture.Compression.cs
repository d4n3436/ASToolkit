using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Drawing;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public readonly ref partial struct Texture
{
    private const int BlockSize = 4;
    private const int ColorCount = 4;

    private static void DecodeCompressed(ReadOnlySpan<byte> source, Size size, VidPixelFormat format, Span<uint> target)
    {
        int blockBytes = format == VidPixelFormat.Dxt1 ? 8 : 16;
        int columns = (size.Width + BlockSize - 1) / BlockSize;
        int rows = (size.Height + BlockSize - 1) / BlockSize;
        Span<uint> colors = stackalloc uint[ColorCount];
        Span<int> alphas = stackalloc int[BlockSize * BlockSize];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int offset = (row * columns + column) * blockBytes;
                if (offset + blockBytes > source.Length)
                    return;

                var block = source.Slice(offset, blockBytes);
                ref readonly var colorBlock = ref MemoryMarshal.AsRef<DxtColorBlock>(block[(blockBytes - Unsafe.SizeOf<DxtColorBlock>())..]);
                ReadAlphas(block, format, alphas);
                BuildColors(colorBlock, format == VidPixelFormat.Dxt1, colors);
                uint indexes = colorBlock.Indexes;
                for (int i = 0; i < BlockSize * BlockSize; i++)
                {
                    int x = column * BlockSize + i % BlockSize;
                    int y = row * BlockSize + i / BlockSize;
                    uint color = colors[(int)(indexes >> (i * 2) & 3)];
                    if (format != VidPixelFormat.Dxt1)
                    {
                        color = color & 0xffffff | (uint)alphas[i] << 24;
                    }

                    if (x < size.Width && y < size.Height)
                    {
                        target[y * size.Width + x] = color;
                    }
                }
            }
        }
    }

    private static void ReadAlphas(ReadOnlySpan<byte> block, VidPixelFormat format, Span<int> alphas)
    {
        if (format == VidPixelFormat.Dxt3)
        {
            for (int i = 0; i < alphas.Length; i++)
            {
                int nibble = block[i / 2] >> (i % 2 * 4) & 0xf;
                alphas[i] = nibble * 17;
            }
        }
        else if (format == VidPixelFormat.Dxt5)
        {
            int first = block[0];
            int second = block[1];
            Span<int> table = stackalloc int[8];
            table[0] = first;
            table[1] = second;
            for (int step = 1; step < (first > second ? 7 : 5); step++)
            {
                table[step + 1] = first > second ? ((7 - step) * first + step * second) / 7 : ((5 - step) * first + step * second) / 5;
            }

            if (first <= second)
            {
                table[6] = 0;
                table[7] = ChannelMax;
            }

            ulong bits = 0;
            for (int i = 0; i < 6; i++)
            {
                bits |= (ulong)block[2 + i] << (i * 8);
            }

            for (int i = 0; i < alphas.Length; i++)
            {
                alphas[i] = table[(int)(bits >> (i * 3) & 7)];
            }
        }
        else
        {
            alphas.Fill(ChannelMax);
        }
    }

    private static void BuildColors(in DxtColorBlock block, bool isOnePixelAlpha, Span<uint> colors)
    {
        colors[0] = Expand(block.First);
        colors[1] = Expand(block.Second);
        if (block.First > block.Second || !isOnePixelAlpha)
        {
            colors[2] = Mix(colors[0], 2, colors[1], 1, 3);
            colors[3] = Mix(colors[0], 1, colors[1], 2, 3);
        }
        else
        {
            colors[2] = Mix(colors[0], 1, colors[1], 1, 2);
            colors[3] = 0;
        }
    }

    private static uint Expand(ushort color)
    {
        uint red = (uint)(color >> 11);
        uint green = (uint)(color >> 5 & 0x3f);
        uint blue = (uint)(color & 0x1f);
        return OpaqueBlack | (red << 3 | red >> 2) << 16 | (green << 2 | green >> 4) << 8 | blue << 3 | blue >> 2;
    }

    private static uint Mix(uint first, uint firstWeight, uint second, uint secondWeight, uint divisor)
    {
        uint result = OpaqueBlack;
        for (int shift = 0; shift < 24; shift += 8)
        {
            result |= ((first >> shift & ChannelMax) * firstWeight + (second >> shift & ChannelMax) * secondWeight) / divisor << shift;
        }

        return result;
    }
}
