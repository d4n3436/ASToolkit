using System.Buffers;
using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public partial class Picture
{
    private const int TileSize = 8;
    private const int TileNeighborhood = TileSize + 1;
    private const int LayerHeaderSize = 8;

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
                var record = new ResRecordBuilder();
                int length = 0;
                checksums[frame] = GetChecksum();
                int match = checksums[..frame].IndexOf(checksums[frame]);
                if (match >= 0)
                {
                    AddInt(buffer, ref length, match);
                }
                else
                {
                    var bounds = GetBounds(true, true);
                    var header = MemoryMarshal.Read<VidLayerHeader>(buffer);
                    if (Flags.HasFlag(PictureFlags.Palette))
                    {
                        header.Format = VidPixelFormat.P8;
                    }
                    else if (Flags.HasFlag(PictureFlags.Alpha))
                    {
                        header.Format = VidPixelFormat.A8R8G8B8;
                    }
                    else
                    {
                        header.Format = VidPixelFormat.A1R5G5B5;
                    }

                    header.Width = (short)bounds.Width;
                    header.Height = (short)bounds.Height;
                    MemoryMarshal.Write(buffer, header);
                    length = LayerHeaderSize;
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
                                AddInt(buffer, ref length, (int)GetHighColor(new Point(x, y)));
                            }
                            else
                            {
                                AddShort(buffer, ref length, (int)GetHighColor(new Point(x, y)));
                            }
                        }
                    }

                    int tileStart = length;
                    AddInt(buffer, ref length, 0);
                    AddInt(buffer, ref length, 0);
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
                            AddShort(buffer, ref length, x);
                            AddShort(buffer, ref length, y);
                            AddShort(buffer, ref length, (int)depth);
                            AddShort(buffer, ref length, x - bounds.Left);
                            AddShort(buffer, ref length, y - bounds.Top);
                            grid[(y - bounds.Top) * stride - bounds.Left + x] = (short)tileCount;
                            tileCount++;
                        }
                    }

                    MemoryMarshal.Write(buffer.AsSpan(tileStart), tileCount);
                    for (int y = bounds.Top; y < bounds.Bottom + TileSize; y += TileSize)
                    {
                        for (int x = bounds.Left; x < bounds.Right + TileSize; x += TileSize)
                        {
                            if (!HasOpaque(new Rectangle(x, y, TileSize, TileSize)))
                                continue;

                            int upper = (y - bounds.Top) * stride - bounds.Left + x;
                            int lower = (y - bounds.Top + TileSize) * stride - bounds.Left + x;
                            AddShort(buffer, ref length, grid[upper]);
                            AddShort(buffer, ref length, grid[upper + TileSize]);
                            AddShort(buffer, ref length, grid[lower]);
                            AddShort(buffer, ref length, grid[lower + TileSize]);
                            AddShort(buffer, ref length, grid[upper + TileSize]);
                            AddShort(buffer, ref length, grid[lower]);
                            indexCount += 6;
                        }
                    }

                    MemoryMarshal.Write(buffer.AsSpan(tileStart + sizeof(int)), indexCount);
                }

                record.Write(length);
                record.WriteBody(buffer.AsSpan(0, length), coder);
                frames.Add(record.Bytes.ToArray());
                savings += record.CompressionSavings;
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

    private static void AddInt(Span<byte> body, ref int length, int value)
    {
        MemoryMarshal.Write(body[length..], value);
        length += sizeof(int);
    }
}
