using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of an object ID whose sprites are drawn in software into a 32-bit back buffer.
/// </summary>
public partial class VidSoftware : Vid
{
    private protected const int PaletteEntries = 256;
    private protected const int TrueColorPaletteSize = PaletteEntries * sizeof(uint);
    private protected const int HighColorPaletteSize = PaletteEntries * sizeof(ushort);
    private protected const int ArmyPalettes = 4;
    private protected const int AliasBody = sizeof(short);
    private protected const ushort HighColorGreenMask = 0x7fe0;
    private protected const ushort HighColorBlueMask = 0x1f;
    private const int HeadFieldsSize = 5 * sizeof(short);
    private const int PackedPaletteSize = PaletteEntries * 3;
    private const int HeightGridSide = 256;
    private const int HeightGridLimit = 0x800;
    private const int HeightCellShift = 3;
    private const int HeightCellSize = 8;
    private const short EmptyHeight = -32000;
    private const int HeightDepthShift = 3;
    private const int HeightDepthBias = 0x80;
    private const int DepthRunPixelSize = 3;
    private const int DepthSampleSize = 2;

    private int _bufferSize;
    private byte[] _buffer = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="VidSoftware"/> class.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    /// <param name="format">The channel layout of the 16-bit back buffer.</param>
    [EngineRoutine(0x00415410)]
    public VidSoftware(Map map, KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, in VidResource file, int totalFrames, HighColorFormat format)
        : base(map, id, gameObject, weapons, file, totalFrames)
    {
        Format = format;
    }

    [EngineRoutine(0x00415280)]
    private protected VidSoftware(VidSoftware source)
        : base(source)
    {
        Format = source.Format;
        FrameOffsets = source.FrameOffsets;
        _bufferSize = source._bufferSize;
        _buffer = source._buffer;
        CopyHeights(source);
    }

    /// <summary>
    /// Gets the start of each frame record in <see cref="Frames"/>.
    /// </summary>
    public int[] FrameOffsets { get; private set; } = [];

    /// <summary>
    /// Gets the palettes of the sprite file, holding the current palette or one palette per army, followed by the palette as the file stores it.
    /// </summary>
    public ReadOnlySpan<byte> Palettes => _buffer.AsSpan(0, FileType.HasFlag(VidType.Palette) ? GetPaletteSize() * (FileType.HasFlag(VidType.PaletteVariants) ? ArmyPalettes + 1 : 2) : 0);

    /// <summary>
    /// Gets the palettes followed by the frame records.
    /// </summary>
    public ReadOnlySpan<byte> Frames => _buffer;

    /// <summary>
    /// Gets the channel layout of the 16-bit back buffer.
    /// </summary>
    public HighColorFormat Format { get; }

    /// <summary>
    /// Gets a value indicating whether the sprite file draws to the 16-bit back buffer.
    /// </summary>
    protected virtual bool IsHighColor => false;

    /// <summary>
    /// Gets the size of one palette in bytes.
    /// </summary>
    /// <returns>The size of one palette.</returns>
    [EngineRoutine(0x004126b0)]
    public virtual int GetPaletteSize() => TrueColorPaletteSize;

    /// <inheritdoc/>
    public override void SetLayer() => Layer = GetLayer();

    /// <summary>
    /// Applies a gamma to a palette.
    /// </summary>
    /// <param name="palette">The palette, 256 entries of the size the sprite file stores them in.</param>
    /// <param name="gamma">The gamma.</param>
    [EngineRoutine(0x00415f10)]
    public virtual void ApplyGamma(Span<byte> palette, Gamma gamma)
    {
        if (palette.IsEmpty || gamma is { Negative: 0, Positive: 0 })
            return;

        var entries = MemoryMarshal.Cast<byte, uint>(palette);
        for (int i = 0; i < PaletteEntries; i++)
        {
            entries[i] = gamma.Apply(entries[i]);
        }
    }

    [EngineRoutine(0x004153e0)]
    internal override Vid CreateMirror() => new VidSoftware(this);

    [EngineRoutine(0x00415440)]
    internal override void Destruct()
    {
        if (NextMirror == this)
        {
            _buffer = [];
            FrameOffsets = [];
            _map.Session.VidMemoryInUse -= _bufferSize;
            _bufferSize = 0;
        }

        base.Destruct();
    }

    [EngineRoutine(0x004154b0)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data)
    {
        var resource = default(Resource);
        resource.Open(data, Resource.AnyTag);
        resource.GoBegin("HEAD"u8);
        resource.Read(stackalloc byte[HeadFieldsSize]);
        var coder = FileType.HasFlag(VidType.RangeCoded) ? new RangeCoder(1) : null;
        bool hasPalette = FileType.HasFlag(VidType.Palette);
        Span<uint> colors = stackalloc uint[PaletteEntries];
        if (hasPalette && resource.GoNext("PAL "u8) != Resource.Found)
        {
            Report(LogError.Load, 0, "PAL "u8);
            throw new InvalidDataException($"There is no palette in '{Path}'.");
        }

        if (hasPalette)
        {
            Span<byte> record = stackalloc byte[TrueColorPaletteSize];
            resource.Read(record[..(FileType.HasFlag(VidType.CurrentVersion) ? TrueColorPaletteSize : PackedPaletteSize)]);
            if (FileType.HasFlag(VidType.CurrentVersion))
            {
                MemoryMarshal.Cast<byte, uint>(record[..TrueColorPaletteSize]).CopyTo(colors);
            }
            else
            {
                var packed = MemoryMarshal.Cast<byte, VidPackedColor>(record);
                for (int i = 0; i < PaletteEntries; i++)
                {
                    colors[i] = Graphics.PackColor(packed[i].Red, packed[i].Green, packed[i].Blue);
                }
            }

            var gamma = ObjectGamma;
            if (gamma.Negative != 0 || gamma.Positive != 0)
            {
                for (int i = 0; i < PaletteEntries; i++)
                {
                    colors[i] = gamma.Apply(colors[i]);
                }
            }
        }

        if (resource.GoNext("DATA"u8) != Resource.Found)
        {
            Report(LogError.Load, 0, "DATA"u8);
        }

        int paletteSize = GetPaletteSize();
        _bufferSize = resource.ChunkSize;
        if (hasPalette)
        {
            _bufferSize += paletteSize * 2;
        }

        _buffer = new byte[_bufferSize];
        FrameOffsets = new int[FileFrameCount];
        int position = 0;
        if (hasPalette)
        {
            if (paletteSize == TrueColorPaletteSize)
            {
                MemoryMarshal.Cast<uint, byte>(colors).CopyTo(_buffer);
            }
            else
            {
                var pixels = MemoryMarshal.Cast<byte, ushort>(_buffer.AsSpan(0, paletteSize));
                for (int i = 0; i < PaletteEntries; i++)
                {
                    pixels[i] = Format.Pack(colors[i]);
                }
            }

            _buffer.AsSpan(0, paletteSize).CopyTo(_buffer.AsSpan(paletteSize));
            position = paletteSize * 2;
        }


        bool isConverted = !FileType.HasFlag(VidType.Palette) && Format.IsRgb555 && IsHighColor && !FileType.HasFlag(VidType.Blended);
        int length = 0;
        for (int frame = 0; frame < FileFrameCount; frame++)
        {
            resource.Read(MemoryMarshal.AsBytes(new Span<int>(ref length)));
            int missing = 0;
            if (length != 0)
            {
                int count = coder is null ? (int)Math.Min((uint)length, (uint)resource.Remaining) : length;
                if (count < 0 || position < 0 || count > _buffer.Length - position)
                    throw new InvalidDataException($"Frame {frame} of '{Path}' does not fit in its frame buffer.");

                var destination = _buffer.AsSpan(position, count);
                missing = coder is null ? length - count + resource.Read(destination) : resource.ReadPacked(destination, coder);
            }

            if (missing != 0)
            {
                Report(LogError.Load, length - missing, "Can't decode software"u8);
            }

            if (length == AliasBody)
            {
                int target = position >= 0 && position <= _buffer.Length - sizeof(short) ? MemoryMarshal.Read<short>(_buffer.AsSpan(position)) : 0;
                // TODO: Port the unchecked alias read
                FrameOffsets[frame] = target >= 0 && target < FileFrameCount ? FrameOffsets[target] : 0;
            }
            else
            {
                if (isConverted)
                {
                    var runs = new RunWalker(_buffer, FrameRows.Read(_buffer, position), sizeof(ushort));
                    while (runs.MoveNext())
                    {
                        var pixels = MemoryMarshal.Cast<byte, ushort>(_buffer.AsSpan(runs.Pixels, runs.Length * sizeof(ushort)));
                        for (int pixel = 0; pixel < pixels.Length; pixel++)
                        {
                            pixels[pixel] = (ushort)(pixels[pixel] >> 1 & HighColorGreenMask | pixels[pixel] & HighColorBlueMask);
                        }
                    }
                }

                FrameOffsets[frame] = position;
                position += length;
            }

            resource.GoNextSub("DATA"u8);
        }
        _map.Session.VidMemoryInUse += _bufferSize;
        if (!Properties.HasFlag(ObjectProperties.BuildVidZToGridZ) || !FileType.HasFlag(VidType.Palette) || !FileType.HasFlag(VidType.Runs8Bit))
            return;

        int[] starts = new int[FileFrameCount];
        Span<short> grid = stackalloc short[HeightGridSide * HeightGridSide];
        for (int frame = 0; frame < FileFrameCount; frame++)
        {
            grid.Fill(EmptyHeight);
            starts[frame] = HeightPoints.Count;
            var rows = FrameRows.Read(Frames, FrameOffsets[frame]);
            if (!FileType.HasFlag(VidType.Runs8Bit) || !FileType.HasFlag(VidType.ZBuffer))
            {
                var runs = new RunWalker(Frames, rows, sizeof(byte));
                while (runs.MoveNext())
                {
                    int row = runs.Row;
                    for (int pixel = runs.Start; pixel < runs.Start + runs.Length; pixel++)
                    {
                        if (row is < 0 or >= HeightGridLimit || pixel is < 0 or >= HeightGridLimit)
                            continue;

                        grid[(row >> HeightCellShift) * HeightGridSide + (pixel >> HeightCellShift)] = 0;
                    }
                }
            }
            else
            {
                var runs = new RunWalker(Frames, rows, DepthRunPixelSize);
                while (runs.MoveNext())
                {
                    for (int i = 0; i < runs.Length; i++)
                    {
                        int height = (MemoryMarshal.Read<ushort>(Frames[(runs.Pixels + i * DepthSampleSize)..]) >> HeightDepthShift) - HeightDepthBias;
                        int cellRow = runs.Row + height;
                        int pixel = runs.Start + i;
                        if (cellRow is < 0 or >= HeightGridLimit || pixel is < 0 or >= HeightGridLimit)
                            continue;

                        int cell = (cellRow >> HeightCellShift) * HeightGridSide + (pixel >> HeightCellShift);
                        if (grid[cell] < height)
                        {
                            grid[cell] = (short)height;
                        }
                    }
                }
            }

            int halfWidth = FrameSize.Width / 2;
            int halfHeight = FrameSize.Height / 2;
            for (int cellRow = FrameSize.Height / HeightCellSize - 1; cellRow >= 0; cellRow--)
            {
                for (int column = 0; column < FrameSize.Width / HeightCellSize; column++)
                {
                    short height = grid[cellRow * HeightGridSide + column];
                    if (height == EmptyHeight)
                        continue;

                    HeightPoints.Add(new Vector3(column * (float)HeightCellSize - halfWidth, cellRow * (float)HeightCellSize - halfHeight, height));
                }
            }
        }

        SetHeightFrameStarts(starts);
    }

    [EngineRoutine(0x00415e90)]
    internal ObjectLayer GetLayer()
    {
        if (IsBuiltTerrainClass)
            return FileType.HasFlag(VidType.Blended) ? ObjectLayer.BuiltAlpha : ObjectLayer.Built;

        if (Properties.HasFlag(ObjectProperties.Ground))
            return ObjectLayer.SoftwareGround;

        if (Properties.HasFlag(ObjectProperties.AlwaysTop))
            return ObjectLayer.SoftwareTop;

        if (FileType.HasFlag(VidType.Blended))
            return ObjectLayer.SoftwareAlpha;

        return !Properties.HasFlag(ObjectProperties.BuildSizeToGridZ) && !Properties.HasFlag(ObjectProperties.BuildVidZToGridZ) ? ObjectLayer.Software : ObjectLayer.SoftwareGrid;
    }

    [EngineRoutine(0x004162b0)]
    internal override void SetGamma(Gamma gamma, int army)
    {
        int size = GetPaletteSize();
        if (_buffer.Length == 0 || !FileType.HasFlag(VidType.Palette))
            return;

        if ((uint)army > AllArmies)
        {
            Report(LogError.Invalid, army, "n_gamma in VID_SOFTWARE::SetGamma"u8);

            return;
        }

        bool ambientApplies = Class != SpriteClass.BuiltTerrain && !Properties.HasFlag(ObjectProperties.Gamma);
        if (army == AllArmies)
        {
            if (FileType.HasFlag(VidType.PaletteVariants))
            {
                for (int i = 0; i < ArmyPalettes; i++)
                {
                    SetGamma(Gammas[i], i);
                }

                return;
            }

            _buffer.AsSpan(size, size).CopyTo(_buffer);
            if (ambientApplies)
            {
                ApplyGamma(_buffer.AsSpan(0, size), gamma);
            }

            return;
        }

        Gammas[army] = gamma;
        if (!FileType.HasFlag(VidType.PaletteVariants))
        {
            _bufferSize += size * 3;
            _map.Session.VidMemoryInUse += size * 3;
            byte[] split = new byte[_bufferSize];
            _buffer.AsSpan(size).CopyTo(split.AsSpan(size * ArmyPalettes));
            split.AsSpan(size * ArmyPalettes, size).CopyTo(split.AsSpan(0, size));
            split.AsSpan(size * ArmyPalettes, size).CopyTo(split.AsSpan(size, size));
            split.AsSpan(0, size * 2).CopyTo(split.AsSpan(size * 2, size * 2));
            _buffer = split;
            for (int frame = 0; frame < FrameOffsets.Length; frame++)
            {
                FrameOffsets[frame] += size * 3;
            }

            FileType |= VidType.PaletteVariants;
            for (var mirror = NextMirror; mirror != this; mirror = mirror.NextMirror)
            {
                mirror.FileType |= VidType.PaletteVariants;
                ((VidSoftware)mirror)._buffer = _buffer;
            }
        }

        var slot = _buffer.AsSpan(size * army, size);
        _buffer.AsSpan(size * ArmyPalettes, size).CopyTo(slot);
        ApplyGamma(slot, ambientApplies ? gamma.Add(_map.Session.Graphics.Gamma) : gamma);
    }
}
