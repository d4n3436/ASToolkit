using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a read-only view of an Alien Shooter demo, the recording of one level.
/// </summary>
public readonly struct DemoResource
{
    private const int EndMarker = -1;

    private readonly ReadOnlyMemory<byte> _record;
    private readonly int _mapNameLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoResource"/> structure.
    /// </summary>
    /// <param name="data">The data.</param>
    public DemoResource(ReadOnlyMemory<byte> data)
    {
        _record = new ResContainer(data).Chunks.First(x => x.Tag.Span.SequenceEqual("DEMO"u8)).Records.First();
        var record = _record.Span;
        _mapNameLength = record.IndexOf((byte)0);
        ref readonly var header = ref MemoryMarshal.AsRef<DemoHeader>(record[(_mapNameLength + 1)..]);
        Seed = header.Seed;
        StartTime = header.Time;
    }

    /// <summary>
    /// Gets the map file name the demo plays, relative to the game folder.
    /// </summary>
    public ReadOnlySpan<byte> MapName => _record.Span[.._mapNameLength];

    /// <summary>
    /// Gets the seed of the random number generator when the level starts.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Gets the game time in milliseconds the level starts at.
    /// </summary>
    public int StartTime { get; }

    /// <summary>
    /// Gets the stream that follows the header, the script state followed by the frames.
    /// </summary>
    public ReadOnlyMemory<byte> Body => _record[(_mapNameLength + 1 + Unsafe.SizeOf<DemoHeader>())..];

    /// <summary>
    /// Attempts to read the frame at an offset of the body.
    /// </summary>
    /// <param name="offset">The offset of the frame, moved past what was read.</param>
    /// <param name="frame">The frame that was read; otherwise, the default frame.</param>
    /// <returns><see langword="true"/> if a frame was read; <see langword="false"/> at the end marker or the end of the body.</returns>
    public bool TryReadFrame(ref int offset, out DemoFrame frame)
    {
        frame = default;
        var body = Body.Span;
        if (offset + sizeof(int) > body.Length)
            return false;

        if (BinaryPrimitives.ReadInt32LittleEndian(body[offset..]) == EndMarker || offset + Unsafe.SizeOf<DemoFrame>() > body.Length)
        {
            offset += sizeof(int);
            return false;
        }

        frame = MemoryMarshal.Read<DemoFrame>(body[offset..]);
        offset += Unsafe.SizeOf<DemoFrame>();

        return true;
    }
}
