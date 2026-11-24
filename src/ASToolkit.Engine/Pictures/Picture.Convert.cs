using System.Buffers;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Picture
{
    private const int MaxRunLength = 0xff;
    private const int BitsPerByte = 8;
    private const uint CrcPolynomial = 0xedb88320;
    private const int CrcTableSize = 256;
    private const int StackChecksumCount = 256;
    private const int MaxRecordSize = 0x200000;

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Converts the picture into the bytes of a sprite file with a frame for every frame of the picture.
    /// </summary>
    /// <param name="options">How the sprite file is made.</param>
    /// <returns>The sprite file, or an empty memory if the picture doesn't have colors or alpha values, or if a hardware layer sprite file is asked for without depth values.</returns>
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
        var head = new VidHeader((VidType)(short)Flags, (short)Colors.Image.Speed, (short)Colors.Image.FrameCount, Colors.Image.Size);
        builder.Chunks.Add(CreateChunk("HEAD", [MemoryMarshal.AsBytes(new ReadOnlySpan<VidHeader>(in head)).ToArray()]));
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

    private static ResChunkBuilder CreateChunk(string tag, List<byte[]> records, int savings = 0) => new() { Tag = tag, Flags = ResChunkFlags.ExtendedHeader, Records = records, CompressionSavings = savings };

    private static void Append<T>(byte[] buffer, ref int length, T value)
        where T : unmanaged
    {
        MemoryMarshal.Write(buffer.AsSpan(length), in value);
        length += Unsafe.SizeOf<T>();
    }

    private static void AddFrame(List<byte[]> frames, ref int savings, ReadOnlySpan<byte> body, RangeCoder? coder)
    {
        var record = new ResRecordBuilder();
        record.Write(body.Length);
        record.WriteBody(body, coder);
        frames.Add(record.Bytes.ToArray());
        savings += record.CompressionSavings;
    }

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
                WriteSoftwarePalette(builder);
            }

            var frames = new List<byte[]>();
            int savings = 0;
            int frameCount = Colors.Image.FrameCount;
            var checksums = frameCount <= StackChecksumCount ? stackalloc uint[StackChecksumCount] : new uint[frameCount];
            checksums = checksums[..frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                int length = 0;
                int match = FindRepeatedFrame(checksums, frame);
                if (match >= 0)
                {
                    Append(buffer, ref length, (short)match);
                }
                else
                {
                    length = ConvertRuns(buffer);
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

    [EngineRoutine(0x00428eb0)]
    private uint GetChecksum()
    {
        uint crc = 0;
        var size = Colors.Image.Size;
        for (int x = 0; x < size.Width; x++)
        {
            for (int y = 0; y < size.Height; y++)
            {
                var point = new Point(x, y);
                uint value;
                if (Flags.HasFlag(PictureFlags.Depth))
                {
                    value = (uint)(short)GetDepth(point);
                    crc = CrcTable[(crc & 0xff) ^ (byte)value] ^ crc >> BitsPerByte;
                    crc = CrcTable[(crc & 0xff) ^ (byte)(value >> BitsPerByte)] ^ crc >> BitsPerByte;
                }

                if (Flags.HasFlag(PictureFlags.Palette))
                {
                    Flags &= ~PictureFlags.Palette;
                    value = GetHighColor(point);
                    Flags |= PictureFlags.Palette;
                }
                else
                {
                    value = GetHighColor(point);
                }

                crc = CrcTable[(crc & 0xff) ^ (byte)value] ^ crc >> BitsPerByte;
                crc = CrcTable[(crc & 0xff) ^ (byte)(value >> BitsPerByte)] ^ crc >> BitsPerByte;
            }
        }

        return crc;
    }

    private int FindRepeatedFrame(Span<uint> checksums, int frame)
    {
        checksums[frame] = GetChecksum();
        return checksums[..frame].IndexOf(checksums[frame]);
    }

    private void WriteSoftwarePalette(ResContainerBuilder builder)
    {
        var record = new ResRecordBuilder();
        if (!Flags.HasFlag(PictureFlags.CurrentVersion))
        {
            Span<VidPackedColor> packed = stackalloc VidPackedColor[PaletteSize];
            for (int i = 0; i < PaletteSize; i++)
            {
                packed[i] = new VidPackedColor(Colors.Image.Palette[i]);
            }

            record.Write(MemoryMarshal.AsBytes(packed));
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

    private int ConvertRuns(byte[] buffer)
    {
        int length = ConvertOutline(buffer);
        var bounds = GetBounds(true, false);
        int top = bounds.Top;
        int bottom = bounds.Bottom;
        if (Flags.HasFlag(PictureFlags.OutlineOnly))
        {
            top = 0;
            bottom = 0;
        }

        Append(buffer, ref length, new VidRowsHeader((short)top, (short)(bottom - top)));
        bool isDepth = Flags.HasFlag(PictureFlags.Depth);
        bool isPalette = Flags.HasFlag(PictureFlags.Palette);
        for (int y = top; y < bottom; y++)
        {
            AppendRow(buffer, ref length, y, isDepth, isPalette);
        }

        return length;
    }

    private void AppendRow(byte[] buffer, ref int length, int y, bool isDepth, bool isPalette)
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
                Append(buffer, ref length, new VidRunHeader(MaxRunLength, 0));
            }

            byte skip = (byte)(x - start);
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

            Append(buffer, ref length, new VidRunHeader(skip, (byte)(x - runStart)));
            AppendRunPixels(buffer, ref length, y, runStart, x, isDepth, isPalette);
        }

        Append(buffer, ref length, new VidRunHeader(0, 0));
    }

    private void AppendRunPixels(byte[] buffer, ref int length, int y, int start, int end, bool isDepth, bool isPalette)
    {
        if (isDepth)
        {
            for (int i = start; i < end; i++)
            {
                Append(buffer, ref length, (short)GetDepth(new Point(i, y)));
            }
        }

        for (int i = start; i < end; i++)
        {
            if (isPalette)
            {
                buffer[length++] = (byte)GetHighColor(new Point(i, y));
            }
            else
            {
                Append(buffer, ref length, (short)GetHighColor(new Point(i, y)));
            }
        }
    }
}
