using System.Buffers;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public partial class Picture
{
    private const int MaxRunLength = 0xff;
    private const int BitsPerByte = 8;
    private const uint CrcPolynomial = 0xedb88320;
    private const int CrcTableSize = 256;
    private const int StackChecksumCount = 256;
    private const int MaxRecordSize = 0x200000;
    private const int PackedPaletteSize = 3 * PaletteSize;
    private const int RedShift = 16;
    private const int GreenShift = 8;

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Converts the picture into the bytes of a sprite file with a frame for every frame of the picture.
    /// </summary>
    /// <param name="options">How the sprite file is made.</param>
    /// <returns>The sprite file, or an empty memory if the picture has no colors or the sprite file isn't one that is converted.</returns>
    [EngineRoutine(0x0042c8c0)]
    public ReadOnlyMemory<byte> Convert(PictureConvertOptions options = PictureConvertOptions.None)
    {
        if (options.HasFlag(PictureConvertOptions.HardwareLayer))
        {
            Flags |= PictureFlags.HardwareLayer;
        }
        else if (options.HasFlag(PictureConvertOptions.Hardware))
        {
            Flags |= PictureFlags.Hardware;
        }

        if (options.HasFlag(PictureConvertOptions.RangeCoded))
        {
            Flags |= PictureFlags.RangeCoded;
        }

        if (options.HasFlag(PictureConvertOptions.Outline))
        {
            Flags |= PictureFlags.Outline;
        }

        if (options.HasFlag(PictureConvertOptions.SurfaceVariant))
        {
            Flags |= PictureFlags.SurfaceVariant;
        }

        if (Colors.Image is { HasPixels: true, BytesPerPixel: 1 })
        {
            Flags |= PictureFlags.Palette;
        }

        if (Flags.HasFlag(PictureFlags.Hardware))
        {
            Flags &= ~PictureFlags.Palette;
        }

        if (!Colors.Image.HasPixels && !Alpha.Image.HasPixels)
        {
            Colors.Image.Report(LogError.Invalid, "not picture3"u8);
            return default;
        }

        if (!Flags.HasFlag(PictureFlags.Depth) && Flags.HasFlag(PictureFlags.HardwareLayer))
        {
            Colors.Image.Report(LogError.Text, "Unsupported files combination"u8);
            return default;
        }

        var builder = new ResContainerBuilder { Magic = "RES "u8.ToArray(), Kind = "VID "u8.ToArray() };
        Flags |= PictureFlags.CurrentVersion;
        byte[] head = new byte[Unsafe.SizeOf<VidHeader>()];
        var fields = MemoryMarshal.Cast<byte, short>(head);
        fields[0] = (short)Flags;
        fields[1] = (short)Colors.Image.Speed;
        fields[2] = (short)Colors.Image.FrameCount;
        fields[3] = (short)Colors.Image.Size.Width;
        fields[4] = (short)Colors.Image.Size.Height;
        builder.Chunks.Add(CreateChunk("HEAD", [head]));
        if (options.HasFlag(PictureConvertOptions.CoarseOutline))
        {
            Flags |= PictureFlags.CoarseOutline;
        }

        if (options.HasFlag(PictureConvertOptions.OutlineOnly))
        {
            Flags |= PictureFlags.OutlineOnly;
        }

        if (Flags.HasFlag(PictureFlags.HardwareLayer))
        {
            ConvertHardwareLayer(builder);
        }
        else if (Flags.HasFlag(PictureFlags.Light))
        {
            builder.Chunks.Add(CreateChunk("DATA", [ConvertLight()]));
        }
        else if (!Flags.HasFlag(PictureFlags.Hardware) || Flags.HasFlag(PictureFlags.Alpha) && Flags.HasFlag(PictureFlags.Depth))
        {
            ConvertSoftware(builder);
        }
        else
        {
            ConvertHardware(builder);
        }

        return builder.Build();
    }

    [EngineRoutine(0x0042c730)]
    private byte[] ConvertLight()
    {
        byte[] bytes = new byte[Colors.Image.FrameCount * sizeof(uint)];
        var colors = MemoryMarshal.Cast<byte, uint>(bytes);
        for (int i = 0; i < Colors.Image.FrameCount; i++)
        {
            colors[i] = GetColor(Point.Empty);
            Advance();
        }

        return bytes;
    }

    [EngineRoutine(0x0042bae0)]
    private void ConvertSoftware(ResContainerBuilder builder)
    {
        var coder = Flags.HasFlag(PictureFlags.RangeCoded) ? new RangeCoder(1) : null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxRecordSize);
        try
        {
            if (Flags.HasFlag(PictureFlags.Palette))
            {
                var record = new ResRecordBuilder();
                if (!Flags.HasFlag(PictureFlags.CurrentVersion))
                {
                    Span<byte> packed = stackalloc byte[PackedPaletteSize];
                    for (int i = 0; i < PaletteSize; i++)
                    {
                        uint color = Colors.Image.Palette[i];
                        packed[3 * i] = (byte)(color >> RedShift);
                        packed[3 * i + 1] = (byte)(color >> GreenShift);
                        packed[3 * i + 2] = (byte)color;
                    }

                    record.Write(packed);
                }
                else if (!Flags.HasFlag(PictureFlags.Alpha))
                {
                    record.Write(MemoryMarshal.AsBytes(Colors.Image.Palette));
                }
                else
                {
                    QuantizePalette();
                    record.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(_palette)));
                }

                builder.Chunks.Add(CreateChunk("PAL ", [record.Bytes.ToArray()]));
            }

            var frames = new List<byte[]>();
            int savings = 0;
            int frameCount = Colors.Image.FrameCount;
            Span<uint> checksums = frameCount <= StackChecksumCount ? stackalloc uint[StackChecksumCount] : new uint[frameCount];
            checksums = checksums[..frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                var record = new ResRecordBuilder();
                int length = 0;
                checksums[frame] = GetChecksum();
                int match = checksums[..frame].IndexOf(checksums[frame]);
                if (match >= 0)
                {
                    AddShort(buffer, ref length, match);
                }
                else
                {
                    length = ConvertOutline(buffer);
                    var bounds = GetBounds(true, false);
                    int top = bounds.Top;
                    int bottom = bounds.Bottom;
                    if (Flags.HasFlag(PictureFlags.OutlineOnly))
                    {
                        top = 0;
                        bottom = 0;
                    }

                    AddShort(buffer, ref length, top);
                    AddShort(buffer, ref length, bottom - top);
                    bool isDepth = Flags.HasFlag(PictureFlags.Depth);
                    bool isPalette = Flags.HasFlag(PictureFlags.Palette);
                    for (int y = top; y < bottom; y++)
                    {
                        int x = 0;
                        while (true)
                        {
                            int start = x;
                            while (!IsOpaque(new Point(x, y)))
                            {
                                x++;
                                if (x >= Colors.Image.Size.Width)
                                    break;
                            }

                            if (x >= Colors.Image.Size.Width)
                                break;

                            for (; x - start > MaxRunLength; start += MaxRunLength)
                            {
                                buffer[length++] = MaxRunLength;
                                buffer[length++] = 0;
                            }

                            buffer[length++] = (byte)(x - start);
                            int runStart = x;
                            int lookahead = x + 2;
                            do
                            {
                                if (isDepth)
                                {
                                    if (!IsOpaque(new Point(x, y)) && !IsOpaque(new Point(lookahead - 1, y)) && !IsOpaque(new Point(lookahead, y)))
                                        break;
                                }
                                else if (!IsOpaque(new Point(x, y)))
                                {
                                    break;
                                }

                                x++;
                                lookahead++;
                            }
                            while (x - runStart < MaxRunLength);

                            buffer[length++] = (byte)(x - runStart);
                            if (isDepth)
                            {
                                for (int i = runStart; i < x; i++)
                                {
                                    AddShort(buffer, ref length, (int)GetDepth(new Point(i, y)));
                                }
                            }

                            for (int i = runStart; i < x; i++)
                            {
                                if (isPalette)
                                {
                                    buffer[length++] = (byte)GetHighColor(new Point(i, y));
                                }
                                else
                                {
                                    AddShort(buffer, ref length, (int)GetHighColor(new Point(i, y)));
                                }
                            }
                        }

                        buffer[length++] = 0;
                        buffer[length++] = 0;
                    }
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

    [EngineRoutine(0x00428eb0)]
    private uint GetChecksum()
    {
        uint crc = 0;
        var size = Colors.Image.Size;
        for (int x = 0; x < size.Width; x++)
        {
            for (int y = 0; y < size.Height; y++)
            {
                uint value = GetHighColor(new Point(x, y));
                crc = CrcTable[(crc & 0xff) ^ (byte)value] ^ crc >> BitsPerByte;
                crc = CrcTable[(crc & 0xff) ^ (byte)(value >> BitsPerByte)] ^ crc >> BitsPerByte;
            }
        }

        return crc;
    }

    private static void AddShort(Span<byte> body, ref int length, int value)
    {
        MemoryMarshal.Write(body[length..], (short)value);
        length += sizeof(short);
    }

    private static ResChunkBuilder CreateChunk(string tag, List<byte[]> records, int savings = 0) => new() { Tag = tag, Flags = ResChunkFlags.ExtendedHeader, Records = records, CompressionSavings = savings };

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[CrcTableSize];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < BitsPerByte; bit++)
            {
                value = (value & 1) != 0 ? value >> 1 ^ CrcPolynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
