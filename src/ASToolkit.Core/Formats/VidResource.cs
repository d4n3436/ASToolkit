using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of a sprite, a set of run-length frames optionally with a palette and a per-pixel depth buffer.
/// </summary>
public readonly struct VidResource
{
    private const int MaxAliasHops = 8;
    private const int FrameHeader = 4;

    private readonly VidHeader _header;
    private readonly ReadOnlyMemory<byte>[] _frames;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidResource"/> structure.
    /// </summary>
    /// <param name="data">The data.</param>
    public VidResource(ReadOnlyMemory<byte> data)
    {
        var container = new ResContainer(data);
        _header = MemoryMarshal.Read<VidHeader>(GetFirstRecord(container, "HEAD"u8).Span);
        Palette = GetFirstRecord(container, "PAL "u8);

        _frames = [];
        foreach (var chunk in container.Chunks)
        {
            if (!chunk.Tag.Span.SequenceEqual("DATA"u8))
                continue;
            
            _frames = new ReadOnlyMemory<byte>[chunk.Count];
            int index = 0;
            foreach (var record in chunk.Records)
            {
                _frames[index++] = record;
            }

            break;
        }
    }

    /// <summary>
    /// Gets the sprite flags.
    /// </summary>
    public VidFlags Flags => _header.Flags;

    /// <summary>
    /// Gets the milliseconds each frame of an animation lasts.
    /// </summary>
    public ushort Speed => _header.Speed;

    /// <summary>
    /// Gets the number of frames.
    /// </summary>
    public ushort FrameCount => _header.Frames;

    /// <summary>
    /// Gets the canvas width.
    /// </summary>
    public ushort Width => _header.Width;

    /// <summary>
    /// Gets the canvas height.
    /// </summary>
    public ushort Height => _header.Height;

    /// <summary>
    /// Gets a value indicating whether the sprite blends with what lies under it instead of covering it.
    /// </summary>
    public bool IsBlended => Flags.HasFlag(VidFlags.Blended);

    /// <summary>
    /// Gets a value indicating whether the frames carry a per-pixel depth buffer.
    /// </summary>
    public bool HasDepth => Flags.HasFlag(VidFlags.Depth);

    /// <summary>
    /// Gets a value indicating whether the pixel colors are palette indices.
    /// </summary>
    public bool HasPalette => Flags.HasFlag(VidFlags.Palette);

    /// <summary>
    /// Gets the 256 palette entries as blue, green, red and alpha bytes, or an empty value when the sprite has no palette.
    /// </summary>
    public ReadOnlyMemory<byte> Palette { get; }

    /// <summary>
    /// Returns the pixels of a frame, following frames that repeat another one.
    /// </summary>
    public VidFrame GetFrame(int index)
    {
        var record = _frames[index];
        for (int hop = 0; hop < MaxAliasHops && IsAlias(record); hop++)
        {
            record = _frames[MemoryMarshal.Read<short>(record.Span[FrameHeader..])];
        }

        if (IsAlias(record))
        {
            return default;
        }

        var body = record[FrameHeader..];
        int position = 2 + 6 * MemoryMarshal.Read<short>(body.Span);
        if (position + 4 > body.Length)
        {
            return default;
        }

        return new VidFrame(body[(position + 4)..], MemoryMarshal.Read<short>(body.Span[position..]), MemoryMarshal.Read<short>(body.Span[(position + 2)..]),
            HasDepth, HasPalette);
    }

    // Alias frames hold only the index of the frame they repeat
    private static bool IsAlias(ReadOnlyMemory<byte> record) => record.Length == FrameHeader + 2;

    private static ReadOnlyMemory<byte> GetFirstRecord(ResContainer container, ReadOnlySpan<byte> tag)
    {
        foreach (var chunk in container.Chunks)
        {
            if (!chunk.Tag.Span.SequenceEqual(tag))
                continue;
            
            foreach (var record in chunk.Records)
            {
                return record;
            }
        }

        return default;
    }

    [StructLayout(LayoutKind.Explicit, Size = 10)]
    private readonly struct VidHeader
    {
        [FieldOffset(0)]
        public readonly VidFlags Flags;
        
        [FieldOffset(2)]
        public readonly ushort Speed;
        
        [FieldOffset(4)]
        public readonly ushort Frames;
        
        [FieldOffset(6)]
        public readonly ushort Width;
        
        [FieldOffset(8)]
        public readonly ushort Height;
    }
}
