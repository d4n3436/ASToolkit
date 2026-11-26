using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a picture read from an FLC animation file that holds palette indexes with the changes from each frame to the next.
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
    private const int SkipLastPixel = 0x8000;
    private const int SkipLines = 0xc000;
    private const int PacketTypeMask = 0xc000;

    private FlcMagic _magic;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlcImage"/> class.
    /// </summary>
    /// <param name="fileSystem">The file system the picture files are in.</param>
    public FlcImage(IGameFileSystem fileSystem)
        : this(fileSystem, null)
    {
    }

    internal FlcImage(IGameFileSystem fileSystem, MyError? error)
        : base(fileSystem, error)
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
    /// Goes to the next frame, and back to the first frame after the last one.
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

            int chunkOffset = Unsafe.SizeOf<FlcFrameHeader>();
            for (int i = 0; i < header.ChunkCount; i++)
            {
                ref readonly var chunk = ref MemoryMarshal.AsRef<FlcChunkHeader>(frame[chunkOffset..]);
                var data = frame[(chunkOffset + Unsafe.SizeOf<FlcChunkHeader>())..];
                chunkOffset += chunk.Size;
                switch (chunk.Type)
                {
                    case FlcChunkType.Color256:
                    case FlcChunkType.Color64:
                        bool isSixBit = chunk.Type == FlcChunkType.Color64;
                        int packets = MemoryMarshal.Read<short>(data);
                        int position = sizeof(short);
                        int index = 0;
                        for (; packets > 0; packets--)
                        {
                            index += data[position];
                            int count = data[position + 1];
                            if (count == 0)
                            {
                                count = PaletteSize;
                            }

                            position += 2;
                            for (int entry = index; entry < index + count; entry++)
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

                                Palette[entry] = Graphics.PackColor((int)red, (int)green, (int)blue);
                                position += 3;
                            }

                            index += count;
                        }

                        break;
                    case FlcChunkType.DeltaFlc:
                        int deltaLines = MemoryMarshal.Read<ushort>(data);
                        int deltaPosition = sizeof(ushort);
                        int deltaOffset = 0;
                        int deltaLine = 0;
                        if (deltaLines == 0)
                            break;

                        do
                        {
                            int word;
                            while (true)
                            {
                                word = MemoryMarshal.Read<short>(data[deltaPosition..]);
                                deltaPosition += sizeof(short);
                                if ((word & PacketTypeMask) == SkipLines)
                                {
                                    deltaLine -= word;
                                    deltaOffset -= Size.Width * word;
                                    deltaLines -= word;
                                    continue;
                                }

                                if ((word & PacketTypeMask) == SkipLastPixel)
                                {
                                    Pixels[Size.Width + deltaOffset - 1] = (byte)word;
                                    continue;
                                }

                                break;
                            }

                            if ((word & PacketTypeMask) == 0 && word >= 1)
                            {
                                int destination = deltaOffset;
                                for (int packet = word; packet > 0; packet--)
                                {
                                    destination += data[deltaPosition];
                                    int count = (sbyte)data[deltaPosition + 1];
                                    deltaPosition += 2;
                                    if (count < 0)
                                    {
                                        for (int run = -count; run > 0; run--)
                                        {
                                            data.Slice(deltaPosition, sizeof(ushort)).CopyTo(Pixels[destination..]);
                                            destination += sizeof(ushort);
                                        }

                                        deltaPosition += sizeof(ushort);
                                        continue;
                                    }

                                    int bytes = count * sizeof(ushort);
                                    data.Slice(deltaPosition, bytes).CopyTo(Pixels[destination..]);
                                    deltaPosition += bytes;
                                    destination += bytes;
                                }
                            }

                            deltaOffset += Size.Width;
                            deltaLine++;
                        }
                        while (deltaLine < deltaLines);
                        break;
                    case FlcChunkType.DeltaFli:
                        int line = MemoryMarshal.Read<ushort>(data);
                        int end = line + MemoryMarshal.Read<ushort>(data[sizeof(ushort)..]);
                        int linePosition = 2 * sizeof(ushort);
                        for (; line < end; line++)
                        {
                            int x = 0;
                            for (int linePackets = data[linePosition++]; linePackets > 0; linePackets--)
                            {
                                x += data[linePosition];
                                int count = (sbyte)data[linePosition + 1];
                                linePosition += 2;
                                var destination = Pixels[(Size.Width * line + x)..];
                                if (count < 0)
                                {
                                    count = -count;
                                    destination[..count].Fill(data[linePosition]);
                                    linePosition++;
                                }
                                else
                                {
                                    data.Slice(linePosition, count).CopyTo(destination);
                                    linePosition += count;
                                }

                                x += count;
                            }
                        }

                        break;
                    case FlcChunkType.Black:
                        Pixels.Clear();
                        break;
                    case FlcChunkType.ByteRun:
                        int runPosition = 0;
                        if (_magic == FlcMagic.Fli)
                        {
                            for (int row = 0; row < Size.Height; row++)
                            {
                                int x = 0;
                                for (int runPackets = data[runPosition++]; runPackets > 0; runPackets--)
                                {
                                    int count = (sbyte)data[runPosition];
                                    var destination = Pixels[(row * Size.Width + x)..];
                                    if (count < 0)
                                    {
                                        count = -count;
                                        data.Slice(runPosition + 1, count).CopyTo(destination);
                                        runPosition += 1 + count;
                                    }
                                    else
                                    {
                                        destination[..count].Fill(data[runPosition + 1]);
                                        runPosition += 2;
                                    }

                                    x += count;
                                }
                            }

                            break;
                        }

                        int runOffset = 0;
                        for (int row = 0; row < Size.Height; row++)
                        {
                            runPosition++;
                            for (int x = 0; x < Size.Width;)
                            {
                                int count = (sbyte)data[runPosition];
                                if (count < 0)
                                {
                                    count = -count;
                                    data.Slice(runPosition + 1, count).CopyTo(Pixels[runOffset..]);
                                    runPosition += 1 + count;
                                }
                                else
                                {
                                    Pixels.Slice(runOffset, count).Fill(data[runPosition + 1]);
                                    runPosition += 2;
                                }

                                runOffset += count;
                                x += count;
                            }
                        }

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
}
