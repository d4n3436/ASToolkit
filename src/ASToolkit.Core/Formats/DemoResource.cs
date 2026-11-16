using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of an Alien Shooter demo, the recording of one level.
/// </summary>
public readonly struct DemoResource
{
    private const int EndMarker = -1;
    private const int HeaderLength = sizeof(uint) + sizeof(int);

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
        Seed = BinaryPrimitives.ReadUInt32LittleEndian(record[(_mapNameLength + 1)..]);
        StartTime = BinaryPrimitives.ReadInt32LittleEndian(record[(_mapNameLength + 1 + sizeof(uint))..]);
    }

    /// <summary>
    /// Gets the map file name the demo plays, relative to the game folder.
    /// </summary>
    public ReadOnlySpan<byte> MapName => _record.Span[.._mapNameLength];

    /// <summary>
    /// Gets the seed of the random number generator when the level starts.
    /// </summary>
    public uint Seed { get; }

    /// <summary>
    /// Gets the game time in milliseconds the level starts at.
    /// </summary>
    public int StartTime { get; }

    /// <summary>
    /// Gets the stream that follows the header, the script state followed by the frames.
    /// </summary>
    public ReadOnlyMemory<byte> Body => _record[(_mapNameLength + 1 + HeaderLength)..];

    /// <summary>
    /// Reads the frame at an offset of the body, returning false at the end marker or the end of the body.
    /// </summary>
    /// <param name="offset">The offset of the frame, moved past what was read.</param>
    /// <param name="frame">The frame.</param>
    /// <returns><see langword="true"/> when a frame was read; otherwise, <see langword="false"/>.</returns>
    public bool TryReadFrame(ref int offset, out DemoFrame frame)
    {
        frame = default;
        var body = Body.Span;
        if (offset + sizeof(int) > body.Length)
            return false;

        int time = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        offset += sizeof(int);
        if (time == EndMarker || offset + Marshal.SizeOf<DemoInput>() > body.Length)
            return false;

        frame = new DemoFrame(time, MemoryMarshal.Read<DemoInput>(body[offset..]));
        offset += Marshal.SizeOf<DemoInput>();

        return true;
    }
}
