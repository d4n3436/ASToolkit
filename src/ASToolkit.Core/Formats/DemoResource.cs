using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of an Alien Shooter demo, the recording of one level.
/// </summary>
public readonly struct DemoResource
{
    private const int EndMarker = -1;
    private const int MaxEntries = 100000;
    private const byte HasValue = 0x08;
    private const byte IsString = 0x01;
    private const int EntryTail = 20;

    private readonly ReadOnlyMemory<byte> _body;
    private readonly int _frames;
    private readonly int _frameCount;
    private readonly int _mapNameLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoResource"/> structure.
    /// </summary>
    /// <param name="data">The data.</param>
    public DemoResource(ReadOnlyMemory<byte> data)
    {
        _body = new ResContainer(data).Chunks.First(x => x.Tag.Span.SequenceEqual("DEMO"u8)).Records.First();
        var body = _body.Span;
        int nameEnd = body.IndexOf((byte)0);
        _mapNameLength = nameEnd;
        Seed = BinaryPrimitives.ReadUInt32LittleEndian(body[(nameEnd + 1)..]);
        StartTime = BinaryPrimitives.ReadInt32LittleEndian(body[(nameEnd + 5)..]);
        ScriptStateOffset = nameEnd + 9;

        // Try each variable count until the rest reads as the function table and whole frames
        int offset = ScriptStateOffset;
        while (offset < body.Length)
        {
            int frames = MeasureFrames(body, offset);

            if (frames >= 0)
            {
                _frames = frames;
                _frameCount = (body.Length - sizeof(int) - frames) / Unsafe.SizeOf<DemoFrame>();
                return;
            }

            offset = SkipVariable(body, offset);
            ScriptVariableCount++;
        }

        throw new InvalidDataException("Expected script state followed by demo frames.");
    }

    /// <summary>
    /// Gets the map file name the demo plays, relative to the game folder.
    /// </summary>
    public ReadOnlySpan<byte> MapName => _body.Span[.._mapNameLength];

    /// <summary>
    /// Gets the seed of the random number generator when the level starts.
    /// </summary>
    public uint Seed { get; }

    /// <summary>
    /// Gets the game time in milliseconds the level starts at.
    /// </summary>
    public int StartTime { get; }

    /// <summary>
    /// Gets the offset of the script state within the demo body.
    /// </summary>
    public int ScriptStateOffset { get; }

    /// <summary>
    /// Gets the number of script variables the script state holds.
    /// </summary>
    public int ScriptVariableCount { get; }

    /// <summary>
    /// Gets the script state the level starts with, the variables followed by the functions.
    /// </summary>
    public ReadOnlySpan<byte> ScriptState => _body.Span[ScriptStateOffset.._frames];

    /// <summary>
    /// Gets the frames of the demo in play order.
    /// </summary>
    public ReadOnlySpan<DemoFrame> Frames => MemoryMarshal.Cast<byte, DemoFrame>(_body.Span.Slice(_frames, _frameCount * Unsafe.SizeOf<DemoFrame>()));

    private static int SkipVariable(ReadOnlySpan<byte> body, int offset)
    {
        byte type = body[offset++];

        if ((type & HasValue) == 0)
            return offset;

        if ((type & IsString) == 0)
            return offset + sizeof(int);

        int end = body[offset..].IndexOf((byte)0);

        return end < 0 ? body.Length : offset + end + 1;
    }

    private static int MeasureFrames(ReadOnlySpan<byte> body, int offset)
    {
        if (offset + sizeof(int) > body.Length)
            return -1;

        int count = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);

        if (count is < 0 or > MaxEntries)
            return -1;

        offset += sizeof(int);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int index = 0; index < count; index++)
            {
                int end = body[offset..].IndexOf((byte)0);

                if (end < 0)
                    return -1;

                offset += end + 1 + (pass == 0 ? EntryTail : 0);

                if (offset > body.Length)
                    return -1;
            }
        }

        int bytes = body.Length - sizeof(int) - offset;

        if (bytes < 0 || bytes % Unsafe.SizeOf<DemoFrame>() != 0 || BinaryPrimitives.ReadInt32LittleEndian(body[^sizeof(int)..]) != EndMarker)
            return -1;

        var frames = MemoryMarshal.Cast<byte, DemoFrame>(body.Slice(offset, bytes));
        for (int index = 1; index < frames.Length; index++)
        {
            if (frames[index].Time < frames[index - 1].Time)
                return -1;
        }

        return offset;
    }
}
