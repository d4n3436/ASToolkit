using System.Drawing;
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
    private const int PalettePixelSize = 768;

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
        Data = data;
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
                _cadrRecords = ReadRecords(chunk);
            }
            else if (chunk.Tag.Span.SequenceEqual("DATA"u8) && _frames.Length == 0)
            {
                _frames = ReadRecords(chunk);
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
    /// Gets the size of the canvas, in pixels.
    /// </summary>
    public Size Size => _header.Size;

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
    /// Gets a value indicating whether the pixels of the sprite carry depth values for the depth buffer.
    /// </summary>
    public bool HasZBuffer => Type.HasFlag(VidType.ZBuffer);

    /// <summary>
    /// Gets a value indicating whether the sprite keeps its pixels in a <c>SURF</c> chunk.
    /// </summary>
    public bool HasSurfaceChunk => !_surfaces.IsEmpty;

    /// <summary>
    /// Gets the number of surfaces the <c>SURF</c> chunk declares, or zero when the sprite doesn't have any.
    /// </summary>
    public short SurfaceCount => _surfaces.Length < sizeof(short) ? (short)0 : MemoryMarshal.Read<short>(_surfaces.Span);

    /// <summary>
    /// Gets a value indicating whether the pixel colors are palette indexes.
    /// </summary>
    public bool HasPalette => Type.HasFlag(VidType.Palette);

    /// <summary>
    /// Gets the bytes the sprite file was read from.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Gets the records of the <c>DATA</c> chunk, one for each frame, whose records that repeat another frame hold only its index.
    /// </summary>
    public ReadOnlySpan<ReadOnlyMemory<byte>> FrameRecords => _frames;

    /// <summary>
    /// Gets the 256 palette entries as blue, green, red and alpha bytes, or an empty value when the sprite doesn't have a palette.
    /// </summary>
    public ReadOnlyMemory<byte> Palette { get; }

    /// <summary>
    /// Returns the pixels of a frame and follows frames that repeat another one.
    /// </summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The pixels of the frame.</returns>
    public VidFrame GetFrame(int index)
    {
        if (_cadrRecords.Length == 0)
            return GetRunFrame(index);

        return TryGetCadrRecord(index, out var record) ? GetCadrFrame(record) : default;
    }

    /// <summary>
    /// Returns the run-length pixels of a frame from the <c>DATA</c> records, which sprites with <c>CADR</c> records keep as well,
    /// and follows frames that repeat another one.
    /// </summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The run-length pixels of the frame.</returns>
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

        int position = MemoryMarshal.AsRef<VidFrameHeader>(body.Span).Size;
        if (position < 0 || position + Unsafe.SizeOf<VidRowsHeader>() > body.Length)
        {
            return default;
        }

        ref readonly var rows = ref MemoryMarshal.AsRef<VidRowsHeader>(body.Span[position..]);
        return new VidFrame(body[(position + Unsafe.SizeOf<VidRowsHeader>())..], rows.First, rows.Count, HasZBuffer, HasPalette);
    }

    /// <summary>
    /// Returns the vector a frame stores ahead of its pixels, which places the object attached to the object that shows the frame.
    /// </summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The vector of the frame, or zero if the sprite doesn't store one.</returns>
    public Vector3 GetFrameVector(int index)
    {
        if (!Type.HasFlag(VidType.FrameVector))
        {
            return Vector3.Zero;
        }

        if (!TryGetCadrRecord(index, out var record))
        {
            return Vector3.Zero;
        }

        var span = record.Span;
        return span.Length < FrameVectorSize ? Vector3.Zero : MemoryMarshal.Read<Vector3>(span);
    }

    /// <summary>
    /// Returns the textures of a sprite with surfaces and decompresses range coded ones. Sprites with a depth buffer list the depth of each texture right after it.
    /// </summary>
    /// <returns>The textures, or an empty array if the sprite doesn't have any or they can't be read.</returns>
    public VidTexture[] DecodeSurfaces()
    {
        if (!Type.HasFlag(VidType.Hardware) || _surfaces.Length < sizeof(short))
        {
            return [];
        }

        if (!Type.HasFlag(VidType.RangeCoded))
        {
            return ReadSurfaces([], []);
        }

        var colors = ArrayPool<FrequencyModel>.Shared.Rent(RangeDecoder.ModelCount);
        var depths = ArrayPool<FrequencyModel>.Shared.Rent(RangeDecoder.ModelCount);
        try
        {
            var colorModels = colors.AsSpan(0, RangeDecoder.ModelCount);
            var depthModels = depths.AsSpan(0, RangeDecoder.ModelCount);
            foreach (ref var model in colorModels)
            {
                model.Reset();
            }

            foreach (ref var model in depthModels)
            {
                model.Reset();
            }

            return ReadSurfaces(colorModels, depthModels);
        }
        finally
        {
            ArrayPool<FrequencyModel>.Shared.Return(colors);
            ArrayPool<FrequencyModel>.Shared.Return(depths);
        }
    }

    private VidTexture[] ReadSurfaces(Span<FrequencyModel> colorModels, Span<FrequencyModel> depthModels)
    {
        var data = _surfaces.Span;
        var surfaces = new VidTexture[MemoryMarshal.Read<short>(data)];
        int position = sizeof(short);
        bool hasPalette = Type.HasFlag(VidType.Palette);
        bool hasDepth = Type.HasFlag(VidType.ZBuffer);
        int planes = Type.HasFlag(VidType.SurfaceVariant) ? 1 : 2;
        for (int index = 0; index < surfaces.Length; index++)
        {
            if (position + Unsafe.SizeOf<VidSurfaceSize>() > data.Length)
            {
                return [];
            }

            var area = MemoryMarshal.AsRef<VidSurfaceSize>(data[position..]).Size;
            position += Unsafe.SizeOf<VidSurfaceSize>();
            if (area.Width <= 0 || area.Height <= 0)
            {
                return [];
            }

            ReadOnlyMemory<byte> palette = default;
            if (hasPalette)
            {
                if (position + PalettePixelSize > data.Length)
                {
                    return [];
                }

                palette = _surfaces.Slice(position, PalettePixelSize);
                position += PalettePixelSize;
            }

            var color = ReadSurface(ref position, colorModels, planes);
            if (color.IsEmpty || hasPalette && color.Length < area.Width * area.Height)
            {
                return [];
            }

            surfaces[index] = new VidTexture(area, color, palette);
            if (!hasDepth || index + 1 >= surfaces.Length)
            {
                continue;
            }

            index++;
            var depth = ReadSurface(ref position, depthModels, 2);
            if (depth.Length != area.Width * area.Height * sizeof(ushort))
            {
                return [];
            }

            surfaces[index] = new VidTexture(area, depth);
        }

        return surfaces;
    }

    private ReadOnlyMemory<byte> ReadSurface(ref int position, Span<FrequencyModel> models, int planes)
    {
        if (position + sizeof(int) > _surfaces.Span.Length)
        {
            return default;
        }

        int size = MemoryMarshal.Read<int>(_surfaces.Span[position..]);
        position += sizeof(int);
        if (size <= 0)
        {
            return default;
        }

        if (models.IsEmpty)
        {
            if (position + size > _surfaces.Span.Length)
            {
                return default;
            }

            var raw = _surfaces.Slice(position, size);
            position += size;
            return raw;
        }

        byte[] pixels = new byte[size];
        if (!RangeDecoder.TryDecode(_surfaces.Span[position..], pixels, models, planes, out int used))
        {
            return default;
        }

        position += used;
        return pixels;
    }

    /// <summary>
    /// Gets how many frames of a sprite with surfaces the data chunk locates in its textures.
    /// </summary>
    public int TextureFrameCount => !HasSurfaceChunk || _frames.Length == 0 ? 0 : _frames[0].Length / GetTextureFrameSize();

    /// <summary>
    /// Attempts to get where a frame of a sprite with surfaces lies in its textures.
    /// </summary>
    /// <param name="index">The frame index.</param>
    /// <param name="frame">The location of the frame if it exists; otherwise, the default value.</param>
    /// <returns><see langword="true"/> if the sprite has a SURF chunk and a frame with the index; otherwise, <see langword="false"/>.</returns>
    public bool TryGetTextureFrame(int index, out VidTextureFrame frame)
    {
        frame = default;
        int size = GetTextureFrameSize();
        if (!HasSurfaceChunk || _frames.Length == 0 || index < 0 || _frames[0].Length < (index + 1) * size)
        {
            return false;
        }

        var span = _frames[0].Span[(index * size)..];
        frame = Type.HasFlag(VidType.CurrentVersion) ? MemoryMarshal.Read<VidTextureFrame>(span) : new VidTextureFrame(MemoryMarshal.Read<OldVidTextureFrame>(span));
        return true;
    }

    private int GetTextureFrameSize() => Type.HasFlag(VidType.CurrentVersion) ? Unsafe.SizeOf<VidTextureFrame>() : Unsafe.SizeOf<OldVidTextureFrame>();

    /// <summary>
    /// Returns the color of an omni-light frame as a packed red, green and blue value.
    /// </summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The packed color, or zero if the sprite isn't an omni-light.</returns>
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
        if (colorSize == 0 || Type.HasFlag(VidType.SurfaceVariant))
        {
            return default;
        }

        int start = Type.HasFlag(VidType.FrameVector) ? FrameVectorSize : 0;
        if (record.Length < start + Unsafe.SizeOf<CadrData>())
        {
            return default;
        }

        ref readonly var cadr = ref MemoryMarshal.AsRef<CadrData>(record.Span[start..]);
        int pixels = start + Unsafe.SizeOf<CadrData>() + 2 * cadr.IndexCount + 4 * cadr.VertexCount;
        int rowCount = Math.Min(cadr.Size.Height, Size.Height - cadr.Origin.Y);
        if (cadr.Size.Width == 0 || rowCount <= 0 || pixels > record.Length)
        {
            return default;
        }

        int stride = colorSize + (Type.HasFlag(VidType.Normals) ? 1 : 0) + (Type.HasFlag(VidType.Material) ? 1 : 0) + (HasZBuffer ? 2 : 0);
        int indexesStart = start + Unsafe.SizeOf<CadrData>();
        var mesh = new VidMesh(record.Slice(indexesStart, 2 * cadr.IndexCount), record.Slice(indexesStart + 2 * cadr.IndexCount, 4 * cadr.VertexCount));
        return new VidFrame(record[pixels..], new Rectangle(cadr.Origin, new Size(cadr.Size.Width, rowCount)), Size.Width, HasZBuffer, HasPalette, colorSize, stride, mesh);
    }

    private static int GetColorSize(VidPixelFormat format) => format switch
    {
        VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 4,
        VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 2,
        _ => 0
    };

    private static bool IsAlias(ReadOnlyMemory<byte> record) => record.Length == FrameHeader + 2;

    private static ReadOnlyMemory<byte>[] ReadRecords(ResChunk chunk)
    {
        var records = new ReadOnlyMemory<byte>[chunk.Count];
        int i = 0;
        foreach (var record in chunk.Records)
        {
            records[i++] = record;
        }

        return records;
    }

    private bool TryGetCadrRecord(int index, out ReadOnlyMemory<byte> record)
    {
        record = default;
        int current = index;
        for (int hop = 0; hop <= MaxAliasHops && current >= 0 && current < _cadrRecords.Length; hop++)
        {
            record = _cadrRecords[current];
            if (record.Length != CadrAlias)
            {
                return true;
            }

            current = MemoryMarshal.Read<int>(record.Span);
        }

        return false;
    }

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
}
