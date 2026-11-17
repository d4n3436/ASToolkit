using System.Buffers;
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
    private const int SurfaceHeader = 8;

    private readonly VidHeader _header;
    private readonly ReadOnlyMemory<byte>[] _frames;
    private readonly ReadOnlyMemory<byte>[] _cadrRecords;
    private readonly ReadOnlyMemory<byte> _surfaces;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidResource"/> structure.
    /// </summary>
    /// <param name="data">The data.</param>
    public VidResource(ReadOnlyMemory<byte> data)
    {
        var container = new ResContainer(data);
        var head = GetFirstRecord(container, "HEAD"u8).Span;
        _header = MemoryMarshal.Read<VidHeader>(head);
        Format = head.Length >= Unsafe.SizeOf<VidHeader>() + 4 ? MemoryMarshal.Read<VidPixelFormat>(head[10..]) : VidPixelFormat.None;
        MeshType = head.Length >= ModeOffset + 4 ? MemoryMarshal.Read<MeshType>(head[ModeOffset..]) : MeshType.Upright;
        BlendProcedure = head.Length >= BlendOffset + 4 ? MemoryMarshal.Read<BlendProcedure>(head[BlendOffset..]) : BlendProcedure.Alpha;
        Palette = GetFirstRecord(container, "PAL "u8);
        _surfaces = GetFirstRecord(container, "SURF"u8);

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
    /// Gets the type flags of the sprite.
    /// </summary>
    public VidType Type => _header.Type;

    /// <summary>
    /// Gets the milliseconds each frame of an animation lasts.
    /// </summary>
    public short Speed => _header.Speed;

    /// <summary>
    /// Gets the number of frames.
    /// </summary>
    public short FrameCount => _header.Frames;

    /// <summary>
    /// Gets the canvas width.
    /// </summary>
    public short Width => _header.Width;

    /// <summary>
    /// Gets the canvas height.
    /// </summary>
    public short Height => _header.Height;

    /// <summary>
    /// Gets the pixel format of the sprite, or none for sprites whose header does not carry one.
    /// </summary>
    public VidPixelFormat Format { get; }

    /// <summary>
    /// Gets the mesh type of the sprite, or upright for sprites whose header does not carry one.
    /// </summary>
    public MeshType MeshType { get; }

    /// <summary>
    /// Gets the blend procedure of the sprite, or alpha for sprites whose header does not carry one.
    /// </summary>
    public BlendProcedure BlendProcedure { get; }

    /// <summary>
    /// Gets a value indicating whether the sprite blends with what lies under it instead of covering it.
    /// </summary>
    public bool IsBlended => Type.HasFlag(VidType.Blended);

    /// <summary>
    /// Gets a value indicating whether the pixels of the sprite carry depth values for the depth buffer.
    /// </summary>
    public bool HasZBuffer => Type.HasFlag(VidType.ZBuffer);

    /// <summary>
    /// Gets a value indicating whether the sprite keeps its pixels in a <c>SURF</c> chunk.
    /// </summary>
    public bool HasSurfaceChunk => !_surfaces.IsEmpty;

    /// <summary>
    /// Gets the number of surfaces the <c>SURF</c> chunk declares, or zero when the sprite has none.
    /// </summary>
    public short SurfaceCount => _surfaces.Length < sizeof(short) ? (short)0 : MemoryMarshal.Read<short>(_surfaces.Span);

    /// <summary>
    /// Gets a value indicating whether the pixel colors are palette indices.
    /// </summary>
    public bool HasPalette => Type.HasFlag(VidType.Palette);

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
            int current = index;
            for (int hop = 0; hop <= MaxAliasHops && current >= 0 && current < _cadrRecords.Length; hop++)
            {
                var cadrRecord = _cadrRecords[current];
                if (cadrRecord.Length != CadrAlias)
                {
                    return GetCadrFrame(cadrRecord);
                }

                current = MemoryMarshal.Read<int>(cadrRecord.Span);
            }

            return default;
        }

        return GetRunFrame(index);
    }

    /// <summary>
    /// Returns the run-length pixels of a frame from the <c>DATA</c> records, which sprites with <c>CADR</c> records keep as well,
    /// following frames that repeat another one.
    /// </summary>
    /// <param name="index">The frame index.</param>
    public VidFrame GetRunFrame(int index)
    {
        if (index < 0 || index >= _frames.Length)
        {
            return default;
        }

        var record = _frames[index];
        for (int hop = 0; hop < MaxAliasHops && IsAlias(record); hop++)
        {
            int target = MemoryMarshal.Read<short>(record.Span[FrameHeader..]);
            if (target < 0 || target >= _frames.Length)
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
            HasZBuffer, HasPalette);
    }

    /// <summary>
    /// Returns the vector a frame stores ahead of its pixels, which places the object attached to the one showing the frame,
    /// or zero when the sprite stores none.
    /// </summary>
    public Vector3 GetFrameVector(int index)
    {
        if (!Type.HasFlag(VidType.FrameVector))
        {
            return Vector3.Zero;
        }

        int current = index;
        for (int hop = 0; hop <= MaxAliasHops && current >= 0 && current < _cadrRecords.Length; hop++)
        {
            var span = _cadrRecords[current].Span;
            if (span.Length != CadrAlias)
            {
                return span.Length < FrameVectorSize ? Vector3.Zero : MemoryMarshal.Read<Vector3>(span);
            }

            current = MemoryMarshal.Read<int>(span);
        }

        return Vector3.Zero;
    }

    /// <summary>
    /// Returns the textures of a sprite with surfaces, decompressing range coded ones, or an empty array when the sprite has
    /// none or they cannot be read.
    /// </summary>
    public VidTexture[] DecodeSurfaces()
    {
        if (!Type.HasFlag(VidType.Hardware) || Type.HasFlag(VidType.Shadow) || _surfaces.Length < sizeof(short))
        {
            return [];
        }

        if (!Type.HasFlag(VidType.RangeCoded))
        {
            return ReadSurfaces([]);
        }

        var models = ArrayPool<FrequencyModel>.Shared.Rent(RangeDecoder.ModelCount);
        try
        {
            var span = models.AsSpan(0, RangeDecoder.ModelCount);
            foreach (ref var model in span)
            {
                model.Reset();
            }

            return ReadSurfaces(span);
        }
        finally
        {
            ArrayPool<FrequencyModel>.Shared.Return(models);
        }
    }

    private VidTexture[] ReadSurfaces(Span<FrequencyModel> models)
    {
        var data = _surfaces.Span;
        var surfaces = new VidTexture[MemoryMarshal.Read<short>(data)];
        int position = sizeof(short);
        for (int index = 0; index < surfaces.Length; index++)
        {
            if (position + SurfaceHeader > data.Length)
            {
                return [];
            }

            int width = MemoryMarshal.Read<short>(data[position..]);
            int height = MemoryMarshal.Read<short>(data[(position + 2)..]);
            int size = MemoryMarshal.Read<int>(data[(position + 4)..]);
            position += SurfaceHeader;
            if (width == 0 || height == 0 || size != width * height * sizeof(ushort))
            {
                return [];
            }

            if (models.IsEmpty)
            {
                if (position + size > data.Length)
                {
                    return [];
                }

                surfaces[index] = new VidTexture(width, height, _surfaces.Slice(position, size));
                position += size;
                continue;
            }

            byte[] pixels = new byte[size];
            if (!RangeDecoder.TryDecode(data[position..], pixels, models, out int used))
            {
                return [];
            }

            surfaces[index] = new VidTexture(width, height, pixels);
            position += used;
        }

        return surfaces;
    }

    /// <summary>
    /// Attempts to get where a frame of a sprite with surfaces lies in its textures, or false when the sprite has no SURF chunk or no such frame.
    /// </summary>
    public bool TryGetTextureFrame(int index, out VidTextureFrame frame)
    {
        frame = default;
        int size = Unsafe.SizeOf<VidTextureFrame>();
        if (!HasSurfaceChunk || _frames.Length == 0 || index < 0 || _frames[0].Length < (index + 1) * size)
        {
            return false;
        }

        frame = MemoryMarshal.Read<VidTextureFrame>(_frames[0].Span[(index * size)..]);
        return true;
    }

    /// <summary>
    /// Returns the color of an omni-light frame as a packed red, green and blue value, or zero when the sprite is not an
    /// omni-light.
    /// </summary>
    public uint GetLightColor(int index)
    {
        if (!Type.HasFlag(VidType.Light) || _frames.Length == 0 || index < 0 || _frames[0].Length < (index + 1) * sizeof(uint))
        {
            return 0;
        }

        return MemoryMarshal.Read<uint>(_frames[0].Span[(index * sizeof(uint))..]) & 0xffffff;
    }

    private VidFrame GetCadrFrame(ReadOnlyMemory<byte> record)
    {
        int colorSize = HasPalette ? 1 : GetColorSize(Format);
        if (colorSize == 0 || Type.HasFlag(VidType.Shadow))
        {
            return default;
        }

        int start = Type.HasFlag(VidType.FrameVector) ? FrameVectorSize : 0;
        if (record.Length < start + Unsafe.SizeOf<CadrData>())
        {
            return default;
        }

        var cadr = MemoryMarshal.Read<CadrData>(record.Span[start..]);
        int pixels = start + Unsafe.SizeOf<CadrData>() + 2 * cadr.IndexCount + 4 * cadr.VertexCount;
        int rowCount = Math.Min(cadr.Height, Height - cadr.Top);
        if (cadr.Width == 0 || rowCount <= 0 || pixels > record.Length)
        {
            return default;
        }

        int stride = colorSize + (Type.HasFlag(VidType.Normal) ? 1 : 0) + (Type.HasFlag(VidType.Material) ? 1 : 0) + (HasZBuffer ? 2 : 0);
        int indicesStart = start + Unsafe.SizeOf<CadrData>();
        var mesh = new VidMesh(record.Slice(indicesStart, 2 * cadr.IndexCount), record.Slice(indicesStart + 2 * cadr.IndexCount, 4 * cadr.VertexCount));
        return new VidFrame(record[pixels..], cadr.Left, cadr.Top, cadr.Width, rowCount, Width, HasZBuffer, HasPalette, colorSize, stride, mesh);
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
        public readonly VidType Type;
        
        [FieldOffset(2)]
        public readonly short Speed;
        
        [FieldOffset(4)]
        public readonly short Frames;
        
        [FieldOffset(6)]
        public readonly short Width;
        
        [FieldOffset(8)]
        public readonly short Height;
    }

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct CadrData
    {
        [FieldOffset(0)]
        public readonly ushort IndexCount;

        [FieldOffset(2)]
        public readonly ushort VertexCount;

        [FieldOffset(4)]
        public readonly short Width;

        [FieldOffset(6)]
        public readonly short Height;

        [FieldOffset(8)]
        public readonly short Left;

        [FieldOffset(10)]
        public readonly short Top;
    }
}
