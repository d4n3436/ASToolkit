using System.Buffers;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Picture
{
    private const int TileSize = 8;
    private const int TileNeighborhood = TileSize + 1;

    [EngineRoutine(0x0042c070)]
    private void ConvertHardwareLayer(ResContainerBuilder builder)
    {
        int frameCount = Colors.Image.FrameCount;
        var coder = Flags.HasFlag(PictureFlags.RangeCoded) ? new RangeCoder(1) : null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxRecordSize);
        try
        {
            var checksums = frameCount <= StackChecksumCount ? stackalloc uint[StackChecksumCount] : new uint[frameCount];
            checksums = checksums[..frameCount];
            if (Flags.HasFlag(PictureFlags.Alpha))
            {
                Flags &= ~PictureFlags.Palette;
            }

            if (Flags.HasFlag(PictureFlags.Palette))
            {
                var palette = new ResRecordBuilder();
                palette.Write(MemoryMarshal.AsBytes(Colors.Image.Palette));
                builder.Chunks.Add(CreateChunk("PAL ", [palette.Bytes.ToArray()]));
            }

            var frames = new List<byte[]>();
            int savings = 0;
            for (int frame = 0; frame < frameCount; frame++)
            {
                int length = 0;
                int match = FindRepeatedFrame(checksums, frame);
                if (match >= 0)
                {
                    Append(buffer, ref length, match);
                }
                else
                {
                    length = ConvertLayer(buffer);
                }

                AddFrame(frames, ref savings, buffer.AsSpan(0, length), coder);
                Advance();
            }

            if (frameCount > 0)
            {
                builder.Chunks.Add(CreateChunk("DATA", frames, savings));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private int ConvertLayer(byte[] buffer)
    {
        var bounds = GetBounds(true, true);
        VidPixelFormat format;
        if (Flags.HasFlag(PictureFlags.Palette))
        {
            format = VidPixelFormat.P8;
        }
        else if (Flags.HasFlag(PictureFlags.Alpha))
        {
            format = VidPixelFormat.A8R8G8B8;
        }
        else
        {
            format = VidPixelFormat.A1R5G5B5;
        }

        MemoryMarshal.Write(buffer, new VidLayerHeader(format, (short)bounds.Width, (short)bounds.Height));
        int length = Unsafe.SizeOf<VidLayerHeader>();
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                if (Flags.HasFlag(PictureFlags.Palette))
                {
                    buffer[length++] = (byte)GetHighColor(new Point(x, y));
                }
                else if (Flags.HasFlag(PictureFlags.Alpha))
                {
                    Append(buffer, ref length, (int)GetHighColor(new Point(x, y)));
                }
                else
                {
                    Append(buffer, ref length, (short)GetHighColor(new Point(x, y)));
                }
            }
        }

        AppendLayerMesh(buffer, ref length, bounds);
        return length;
    }

    private void AppendLayerMesh(byte[] buffer, ref int length, Rectangle bounds)
    {
        int meshStart = length;
        length += Unsafe.SizeOf<VidLayerMeshHeader>();
        int tileCount = 0;
        int indexCount = 0;
        int stride = bounds.Width + TileNeighborhood;
        short[] grid = new short[stride * (bounds.Height + TileNeighborhood)];
        for (int y = bounds.Top; y < bounds.Bottom + TileSize; y += TileSize)
        {
            for (int x = bounds.Left; x < bounds.Right + TileSize; x += TileSize)
            {
                var area = new Rectangle(x - TileSize, y - TileSize, TileNeighborhood, TileNeighborhood);
                if (!HasOpaque(area))
                    continue;

                uint depth = GetRegionDepth(area);
                Append(buffer, ref length, new VidLayerVertex((short)x, (short)y, (short)depth, (short)(x - bounds.Left), (short)(y - bounds.Top)));
                grid[(y - bounds.Top) * stride - bounds.Left + x] = (short)tileCount;
                tileCount++;
            }
        }

        for (int y = bounds.Top; y < bounds.Bottom + TileSize; y += TileSize)
        {
            for (int x = bounds.Left; x < bounds.Right + TileSize; x += TileSize)
            {
                if (!HasOpaque(new Rectangle(x, y, TileSize, TileSize)))
                    continue;

                int upper = (y - bounds.Top) * stride - bounds.Left + x;
                int lower = (y - bounds.Top + TileSize) * stride - bounds.Left + x;
                ReadOnlySpan<short> corners = [grid[upper], grid[upper + TileSize], grid[lower], grid[lower + TileSize], grid[upper + TileSize], grid[lower]];
                MemoryMarshal.AsBytes(corners).CopyTo(buffer.AsSpan(length));
                length += corners.Length * sizeof(short);
                indexCount += corners.Length;
            }
        }

        MemoryMarshal.Write(buffer.AsSpan(meshStart), new VidLayerMeshHeader(tileCount, indexCount));
    }
}
