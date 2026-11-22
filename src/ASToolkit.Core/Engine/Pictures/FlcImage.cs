using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Engine.IO;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a picture read from an FLC animation file, which holds palette indexes with the changes from each frame to the next.
/// </summary>
public sealed class FlcImage : PictureImage
{
    /// <summary>
    /// Represents the extension of FLC files.
    /// </summary>
    public const string Extension = ".flc";
    private const ushort FrameMagic = 0xf1fa;
    private const int FliTicksPerSecond = 70;
    private const int MillisecondsPerSecond = 1000;
    private const int SixBitShift = 2;
    private const uint MaxChannel = 0xff;
    private const int PaletteSize = 256;
    private const uint OpaqueAlpha = 0xff000000;
    private const int RedShift = 16;
    private const int GreenShift = 8;
    private const int SkipLastPixel = 0x8000;
    private const int SkipLines = 0xc000;
    private const int PacketTypeMask = 0xc000;

    private FlcMagic _magic;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlcImage"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder the picture files are in.</param>
    public FlcImage(string gameFolder)
        : this(gameFolder, null)
    {
    }

    internal FlcImage(string gameFolder, GameLogger? logger)
        : base(gameFolder, logger)
    {
    }

    /// <summary>
    /// Opens the animation file, reads its header and goes to the first frame.
    /// </summary>
    /// <param name="name">The path of the file.</param>
    /// <returns><see langword="true"/> if the picture was loaded; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00427780)]
    public override bool Load(string name)
    {
        if (!base.Load(name))
            return false;

        FlcHeader flc = default;
        Input!.ReadAtLeast(MemoryMarshal.AsBytes(new Span<FlcHeader>(ref flc)), Unsafe.SizeOf<FlcHeader>(), false);
        _magic = flc.Magic;
        FrameCount = flc.Frames;
        switch (flc.Magic)
        {
            case FlcMagic.Fli:
                DataOffset = Unsafe.SizeOf<FlcHeader>();
                Speed = (int)((uint)(flc.Speed * MillisecondsPerSecond) / FliTicksPerSecond);
                break;
            case FlcMagic.Flc:
                DataOffset = flc.FirstFrameOffset;
                Speed = flc.Speed;
                break;
            default:
                Reset();
                Report(LogError.Invalid, "flic type"u8);
                return false;
        }

        Resize(flc.Size, 1);
        Rewind();
        return true;
    }

    /// <summary>
    /// Goes to the next frame by applying its chunks to the Pixels, and back to the first frame after the last one.
    /// </summary>
    [EngineRoutine(0x00427890)]
    public override void Advance()
    {
        if (!HasPixels)
        {
            Report(LogError.Text, "Picture has not opened"u8, FrameIndex);
            return;
        }

        FrameIndex++;
        if (FrameIndex >= FrameCount)
        {
            Rewind();
            return;
        }

        int size = 0;
        Input!.ReadAtLeast(MemoryMarshal.AsBytes(new Span<int>(ref size)), sizeof(int), false);
        int length = Math.Max(size, Unsafe.SizeOf<FlcFrameHeader>());
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var frame = buffer.AsSpan(0, length);
            int read = Math.Max(size - sizeof(int), 0);
            Input.ReadAtLeast(frame.Slice(sizeof(int), read), read, false);
            ref readonly var header = ref MemoryMarshal.AsRef<FlcFrameHeader>(frame);
            if (header.Magic != FrameMagic)
            {
                Report(LogError.Invalid, "chunk mark"u8, FrameIndex);
                return;
            }

            int offset = Unsafe.SizeOf<FlcFrameHeader>();
            for (int i = 0; i < header.ChunkCount; i++)
            {
                ref readonly var chunk = ref MemoryMarshal.AsRef<FlcChunkHeader>(frame[offset..]);
                var data = frame[(offset + Unsafe.SizeOf<FlcChunkHeader>())..];
                offset += chunk.Size;
                switch (chunk.Type)
                {
                    case FlcChunkType.Color256:
                    case FlcChunkType.Color64:
                        ReadPalette(data, chunk.Type == FlcChunkType.Color64);
                        break;
                    case FlcChunkType.DeltaFlc:
                        ReadDeltaFlc(data);
                        break;
                    case FlcChunkType.DeltaFli:
                        ReadDeltaFli(data);
                        break;
                    case FlcChunkType.Black:
                        Pixels.Clear();
                        break;
                    case FlcChunkType.ByteRun:
                        ReadByteRun(data);
                        break;
                    case FlcChunkType.Copy:
                        data[..Pixels.Length].CopyTo(Pixels);
                        break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ReadPalette(ReadOnlySpan<byte> data, bool isSixBit)
    {
        int packets = MemoryMarshal.Read<short>(data);
        int position = sizeof(short);
        int i = 0;
        for (; packets > 0; packets--)
        {
            i += data[position];
            int count = data[position + 1];
            if (count == 0)
            {
                count = PaletteSize;
            }

            position += 2;
            for (int entry = i; entry < i + count; entry++)
            {
                uint red = data[position];
                uint green = data[position + 1];
                uint blue = data[position + 2];
                if (isSixBit)
                {
                    red = Math.Min(red << SixBitShift, MaxChannel);
                    green = Math.Min(green << SixBitShift, MaxChannel);
                    blue = Math.Min(blue << SixBitShift, MaxChannel);
                }

                Palette[entry] = OpaqueAlpha | red << RedShift | green << GreenShift | blue;
                position += 3;
            }

            i += count;
        }
    }

    private void ReadDeltaFlc(ReadOnlySpan<byte> data)
    {
        int lines = MemoryMarshal.Read<ushort>(data);
        int position = sizeof(ushort);
        int offset = 0;
        int line = 0;
        if (lines == 0)
            return;

        do
        {
            int word;
            while (true)
            {
                word = MemoryMarshal.Read<short>(data[position..]);
                position += sizeof(short);
                if ((word & PacketTypeMask) == SkipLines)
                {
                    line -= word;
                    offset -= Size.Width * word;
                    lines -= word;
                }
                else if ((word & PacketTypeMask) == SkipLastPixel)
                {
                    Pixels[Size.Width + offset - 1] = (byte)word;
                }
                else
                {
                    break;
                }
            }

            if ((word & PacketTypeMask) == 0 && word >= 1)
            {
                int destination = offset;
                for (int packet = word; packet > 0; packet--)
                {
                    destination += data[position];
                    int count = (sbyte)data[position + 1];
                    position += 2;
                    if (count < 0)
                    {
                        for (int run = -count; run > 0; run--)
                        {
                            data.Slice(position, sizeof(ushort)).CopyTo(Pixels[destination..]);
                            destination += sizeof(ushort);
                        }

                        position += sizeof(ushort);
                    }
                    else
                    {
                        int bytes = count * sizeof(ushort);
                        data.Slice(position, bytes).CopyTo(Pixels[destination..]);
                        position += bytes;
                        destination += bytes;
                    }
                }
            }

            offset += Size.Width;
            line++;
        }
        while (line < lines);
    }

    private void ReadDeltaFli(ReadOnlySpan<byte> data)
    {
        int line = MemoryMarshal.Read<ushort>(data);
        int end = line + MemoryMarshal.Read<ushort>(data[sizeof(ushort)..]);
        int position = 2 * sizeof(ushort);
        for (; line < end; line++)
        {
            int x = 0;
            for (int packets = data[position++]; packets > 0; packets--)
            {
                x += data[position];
                int count = (sbyte)data[position + 1];
                position += 2;
                var destination = Pixels[(Size.Width * line + x)..];
                if (count < 0)
                {
                    count = -count;
                    destination[..count].Fill(data[position]);
                    position++;
                }
                else
                {
                    data.Slice(position, count).CopyTo(destination);
                    position += count;
                }

                x += count;
            }
        }
    }

    private void ReadByteRun(ReadOnlySpan<byte> data)
    {
        int position = 0;
        if (_magic == FlcMagic.Fli)
        {
            for (int line = 0; line < Size.Height; line++)
            {
                int x = 0;
                for (int packets = data[position++]; packets > 0; packets--)
                {
                    int count = (sbyte)data[position];
                    var destination = Pixels[(line * Size.Width + x)..];
                    if (count < 0)
                    {
                        count = -count;
                        data.Slice(position + 1, count).CopyTo(destination);
                        position += 1 + count;
                    }
                    else
                    {
                        destination[..count].Fill(data[position + 1]);
                        position += 2;
                    }

                    x += count;
                }
            }

            return;
        }

        int offset = 0;
        for (int line = 0; line < Size.Height; line++)
        {
            position++;
            for (int x = 0; x < Size.Width;)
            {
                int count = (sbyte)data[position];
                if (count < 0)
                {
                    count = -count;
                    data.Slice(position + 1, count).CopyTo(Pixels[offset..]);
                    position += 1 + count;
                }
                else
                {
                    Pixels.Slice(offset, count).Fill(data[position + 1]);
                    position += 2;
                }

                offset += count;
                x += count;
            }
        }
    }
}
