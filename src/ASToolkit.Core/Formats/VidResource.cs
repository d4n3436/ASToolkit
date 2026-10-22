using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of a sprite, a set of run-length frames optionally with a palette and a per-pixel depth buffer.
/// </summary>
public readonly struct VidResource
{
    private const int MaxAliasHops = 8;
    private const int FrameHeader = 4;
    private const int FrameVectorSize = 12;
    private const int CadrAlias = 4;
    private const int ModeOffset = 14;
    private const int BlendOffset = 18;

    private readonly VidHeader _header;
    private readonly VidPixelFormat _format;
    private readonly int _mode;
    private readonly VidBlend _blend;
    private readonly ReadOnlyMemory<byte>[] _frames;
    private readonly ReadOnlyMemory<byte>[] _cadrRecords;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidResource"/> structure.
    /// </summary>
    /// <param name="data">The data.</param>
    public VidResource(ReadOnlyMemory<byte> data)
    {
        var container = new ResContainer(data);
        var head = GetFirstRecord(container, "HEAD"u8).Span;
        _header = MemoryMarshal.Read<VidHeader>(head);
        _format = head.Length >= Unsafe.SizeOf<VidHeader>() + 4 ? MemoryMarshal.Read<VidPixelFormat>(head[10..]) : VidPixelFormat.None;
        _mode = head.Length >= ModeOffset + 4 ? MemoryMarshal.Read<int>(head[ModeOffset..]) : 0;
        _blend = head.Length >= BlendOffset + 4 ? (VidBlend)MemoryMarshal.Read<uint>(head[BlendOffset..]) : VidBlend.Alpha;
        Palette = GetFirstRecord(container, "PAL "u8);

        _frames = [];
        _cadrRecords = [];
        foreach (var chunk in container.Chunks)
        {
            if (chunk.Tag.Span.SequenceEqual("CADR"u8) && _cadrRecords.Length == 0)
            {
                _cadrRecords = new ReadOnlyMemory<byte>[chunk.Count];
                int index = 0;
                foreach (var record in chunk.Records)
                {
                    _cadrRecords[index++] = record;
                }
            }
            else if (chunk.Tag.Span.SequenceEqual("DATA"u8) && _frames.Length == 0)
            {
                _frames = new ReadOnlyMemory<byte>[chunk.Count];
                int index = 0;
                foreach (var record in chunk.Records)
                {
                    _frames[index++] = record;
                }
            }
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
    /// Gets the pixel format of the sprite, or none for sprites whose header does not carry one.
    /// </summary>
    public VidPixelFormat Format => _format;

    /// <summary>
    /// Gets the draw mode, or zero for sprites whose header does not carry one.
    /// </summary>
    public int Mode => _mode;

    /// <summary>
    /// Gets the blend mode, or zero for sprites whose header does not carry one.
    /// </summary>
    public VidBlend Blend => _blend;

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
        if (_cadrRecords.Length > 0)
        {
            uint current = (uint)index;
            for (int hop = 0; hop <= MaxAliasHops && current < (uint)_cadrRecords.Length; hop++)
            {
                var cadrRecord = _cadrRecords[current];
                if (cadrRecord.Length != CadrAlias)
                {
                    return GetCadrFrame(cadrRecord);
                }

                current = MemoryMarshal.Read<uint>(cadrRecord.Span);
            }

            return default;
        }

        if ((uint)index >= (uint)_frames.Length)
        {
            return default;
        }

        var record = _frames[index];
        for (int hop = 0; hop < MaxAliasHops && IsAlias(record); hop++)
        {
            int target = MemoryMarshal.Read<short>(record.Span[FrameHeader..]);
            if ((uint)target >= (uint)_frames.Length)
            {
                return default;
            }

            record = _frames[target];
        }

        if (IsAlias(record))
        {
            return default;
        }

        var body = record[FrameHeader..];
        if (body.Length < 2)
        {
            return default;
        }

        int position = 2 + 6 * MemoryMarshal.Read<short>(body.Span);
        if (position < 0 || position + 4 > body.Length)
        {
            return default;
        }

        return new VidFrame(body[(position + 4)..], MemoryMarshal.Read<short>(body.Span[position..]), MemoryMarshal.Read<short>(body.Span[(position + 2)..]),
            HasDepth, HasPalette);
    }

    /// <summary>
    /// Returns the vector a frame stores ahead of its pixels, which places the object attached to the one showing the frame,
    /// or zero when the sprite stores none.
    /// </summary>
    public Vector3 GetFrameVector(int index)
    {
        if (!Flags.HasFlag(VidFlags.FrameVector))
        {
            return Vector3.Zero;
        }

        uint current = (uint)index;
        for (int hop = 0; hop <= MaxAliasHops && current < (uint)_cadrRecords.Length; hop++)
        {
            var span = _cadrRecords[current].Span;
            if (span.Length != CadrAlias)
            {
                return span.Length < FrameVectorSize ? Vector3.Zero : MemoryMarshal.Read<Vector3>(span);
            }

            current = MemoryMarshal.Read<uint>(span);
        }

        return Vector3.Zero;
    }

    private VidFrame GetCadrFrame(ReadOnlyMemory<byte> record)
    {
        int colorSize = HasPalette ? 1 : GetColorSize(Format);
        if (colorSize == 0 || Flags.HasFlag(VidFlags.Dxt))
        {
            return default;
        }

        int start = Flags.HasFlag(VidFlags.FrameVector) ? FrameVectorSize : 0;
        if (record.Length < start + Unsafe.SizeOf<CadrHeader>())
        {
            return default;
        }

        var cadr = MemoryMarshal.Read<CadrHeader>(record.Span[start..]);
        int pixels = start + Unsafe.SizeOf<CadrHeader>() + 2 * cadr.IndexCount + 4 * cadr.VertexCount;
        int rowCount = Math.Min((int)cadr.Height, Height - cadr.Top);
        if (cadr.Width == 0 || rowCount <= 0 || pixels > record.Length)
        {
            return default;
        }

        int stride = colorSize + (Flags.HasFlag(VidFlags.SecondImage) ? 1 : 0) + (Flags.HasFlag(VidFlags.SecondImageWide) ? 1 : 0) + (HasDepth ? 2 : 0);
        int indicesStart = start + Unsafe.SizeOf<CadrHeader>();
        var mesh = new VidMesh(record.Slice(indicesStart, 2 * cadr.IndexCount), record.Slice(indicesStart + 2 * cadr.IndexCount, 4 * cadr.VertexCount));
        return new VidFrame(record[pixels..], cadr.Left, cadr.Top, cadr.Width, rowCount, Width, HasDepth, HasPalette, colorSize, stride, mesh);
    }

    private static int GetColorSize(VidPixelFormat format) => format switch
    {
        VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 4,
        VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 2,
        _ => 0
    };

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

    [StructLayout(LayoutKind.Explicit)]
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

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct CadrHeader
    {
        [FieldOffset(0)]
        public readonly ushort IndexCount;

        [FieldOffset(2)]
        public readonly ushort VertexCount;

        [FieldOffset(4)]
        public readonly ushort Width;

        [FieldOffset(6)]
        public readonly ushort Height;

        [FieldOffset(8)]
        public readonly ushort Left;

        [FieldOffset(10)]
        public readonly ushort Top;
    }
}
