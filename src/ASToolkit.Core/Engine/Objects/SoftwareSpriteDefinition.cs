using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Engine.IO;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a definition drawn in software from runs of pixels into a 32-bit back buffer, whose buffer holds the palettes of the armies followed by the frame records.
/// </summary>
public partial class SoftwareSpriteDefinition : SpriteDefinition
{
    private protected const int PaletteEntries = 256;
    private protected const int TrueColorPaletteSize = PaletteEntries * sizeof(uint);
    private protected const int HighColorPaletteSize = PaletteEntries * sizeof(ushort);
    private protected const int ArmyPalettes = 4;
    private protected const int FrameHeader = 4;
    private protected const int AliasBody = sizeof(short);
    private protected const ushort HighColorGreenMask = 0x7fe0;
    private protected const ushort HighColorBlueMask = 0x1f;

    private static readonly ConditionalWeakTable<byte[], ConcurrentDictionary<(bool IsHighColor, HighColorFormat Format), DecodedSoftwareFile>> DecodedFiles = new();

    private byte[] _buffer = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareSpriteDefinition"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    /// <param name="format">The channel layout of the 16-bit back buffer.</param>
    public SoftwareSpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames, HighColorFormat format)
        : base(id, gameObject, weapon, file, totalFrames)
    {
        Format = format;
        NextMirror = this;
    }

    [EngineRoutine(0x004154b0)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, GameLogger? logger)
    {
        if (FileType.HasFlag(VidType.Palette) && file.Palette.IsEmpty)
        {
            Report(logger, LogError.Load, 0, "PAL "u8);
        }

        if (file.FrameRecords.IsEmpty)
        {
            Report(logger, LogError.Load, 0, "DATA"u8);
        }

        var decoded = MemoryMarshal.TryGetArray(data, out var source) && source.Array is not null && source.Offset == 0 && source.Count == source.Array.Length
            ? DecodedFiles.GetValue(source.Array, _ => new ConcurrentDictionary<(bool IsHighColor, HighColorFormat Format), DecodedSoftwareFile>()) : null;
        if (decoded is not null && decoded.TryGetValue((IsHighColor, Format), out var copy))
        {
            _buffer = copy.Buffer.ToArray();
            FrameOffsets = copy.FrameOffsets.ToArray();
            if (FileType.HasFlag(VidType.Palette))
            {
                Span<uint> colors = stackalloc uint[PaletteEntries];
                ReadPalette(file.Palette.Span, colors);
                StorePalette(colors, GetPaletteSize());
            }
        }
        else
        {
            int paletteSize = GetPaletteSize();
            int start = 0;

            Span<uint> colors = stackalloc uint[PaletteEntries];
            if (FileType.HasFlag(VidType.Palette))
            {
                ReadPalette(file.Palette.Span, colors);
                start = paletteSize * 2;
            }

            var records = file.FrameRecords;
            int total = start;
            foreach (var record in records)
            {
                total += Math.Max(0, record.Length - FrameHeader);
            }

            _buffer = new byte[total];
            FrameOffsets = new int[FileFrameCount];
            if (FileType.HasFlag(VidType.Palette))
            {
                StorePalette(colors, paletteSize);
            }

            int position = start;
            for (int frame = 0; frame < FileFrameCount && frame < records.Length; frame++)
            {
                var body = records[frame].Span[FrameHeader..];
                if (body.Length == AliasBody)
                {
                    int target = MemoryMarshal.Read<short>(body);
                    FrameOffsets[frame] = target >= 0 && target < FileFrameCount ? FrameOffsets[target] : 0;
                    continue;
                }

                body.CopyTo(_buffer.AsSpan(position));
                if (!FileType.HasFlag(VidType.Palette) && Format.IsRgb555 && IsHighColor && !FileType.HasFlag(VidType.Blended))
                {
                    ConvertRowsToRgb555(position);
                }

                FrameOffsets[frame] = position;
                position += body.Length;
            }

            decoded?.TryAdd((IsHighColor, Format), new DecodedSoftwareFile(_buffer.ToArray(), FrameOffsets.ToArray()));
        }

        if (!Object.IsSolid || !FileType.HasFlag(VidType.Palette) || !FileType.HasFlag(VidType.Runs8Bit))
            return;

        int[] starts = new int[FileFrameCount];
        var cells = FrameSize / MovementMap.CellSize * MovementMap.CellSize;
        var half = FrameSize / 2;
        for (int frame = 0; frame < FileFrameCount; frame++)
        {
            starts[frame] = HeightPoints.Count;
            foreach (var point in SpritePartReader.ReadHeightPoints(file, frame))
            {
                if (point.X + half.Width < cells.Width && point.Y + half.Height < cells.Height)
                {
                    HeightPoints.Add(point);
                }
            }
        }

        SetHeightFrameStarts(starts);
    }

    /// <summary>
    /// Gets a value indicating whether the sprite file draws to the 16-bit back buffer.
    /// </summary>
    protected virtual bool IsHighColor => false;

    /// <summary>
    /// Gets the channel layout of the 16-bit back buffer.
    /// </summary>
    public HighColorFormat Format { get; }

    /// <summary>
    /// Gets the palettes followed by the frame records.
    /// </summary>
    public ReadOnlySpan<byte> Buffer => _buffer;

    /// <summary>
    /// Gets the start of each frame record in <see cref="Buffer"/>.
    /// </summary>
    public int[] FrameOffsets { get; private set; } = [];

    /// <summary>
    /// Gets or sets the next sprite file in the ring of files that share one buffer.
    /// </summary>
    public SoftwareSpriteDefinition NextMirror { get; set; }

    /// <summary>
    /// Gets the size of one palette in bytes.
    /// </summary>
    /// <returns>The size of one palette.</returns>
    [EngineRoutine(0x004126b0)]
    public virtual int GetPaletteSize() => TrueColorPaletteSize;

    /// <inheritdoc/>
    public override void SetLayer() => Layer = GetLayer(Object, FileType);

    /// <summary>
    /// Sets the gamma of the palette of an army, or of every army.
    /// </summary>
    /// <param name="gamma">The gamma.</param>
    /// <param name="army">The army, or <see cref="SpriteDefinition.AllArmies"/> for every army.</param>
    /// <param name="ambient">The ambient gamma of the level.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="army"/> is above <see cref="SpriteDefinition.AllArmies"/>.</exception>
    [EngineRoutine(0x004162b0)]
    public override void SetGamma(Gamma gamma, int army, Gamma ambient)
    {
        int size = GetPaletteSize();
        if (_buffer.Length == 0 || !FileType.HasFlag(VidType.Palette))
            return;

        if ((uint)army > AllArmies)
            throw new ArgumentOutOfRangeException(nameof(army));

        bool ambientApplies = Object.SpriteClass != SpriteClass.BuiltTerrain && !Object.Properties.HasFlag(ObjectProperties.Gamma);
        if (army == AllArmies)
        {
            if (FileType.HasFlag(VidType.PaletteVariants))
            {
                for (int i = 0; i < ArmyPalettes; i++)
                {
                    SetGamma(Gammas[i], i, ambient);
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
            SplitPalettes(size);
        }

        var slot = _buffer.AsSpan(size * army, size);
        _buffer.AsSpan(size * ArmyPalettes, size).CopyTo(slot);
        ApplyGamma(slot, ambientApplies ? gamma.Add(ambient) : gamma);
    }

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

    /// <summary>
    /// Returns the update list of a definition drawn in software.
    /// </summary>
    /// <param name="gameObject">The object record.</param>
    /// <param name="file">The flags of the sprite file.</param>
    /// <returns>The update list.</returns>
    [EngineRoutine(0x00415e90)]
    public static ObjectLayer GetLayer(in GameObject gameObject, VidType file)
    {
        if (gameObject.SpriteClass == SpriteClass.BuiltTerrain)
            return file.HasFlag(VidType.Blended) ? ObjectLayer.BuiltAlpha : ObjectLayer.Built;

        if (gameObject.Properties.HasFlag(ObjectProperties.Ground))
            return ObjectLayer.SoftwareGround;

        if (gameObject.Properties.HasFlag(ObjectProperties.AlwaysTop))
            return ObjectLayer.SoftwareTop;

        if (file.HasFlag(VidType.Blended))
            return ObjectLayer.SoftwareAlpha;

        return !gameObject.Properties.HasFlag(ObjectProperties.BuildSizeToGridZ) && !gameObject.Properties.HasFlag(ObjectProperties.BuildVidZToGridZ) ? ObjectLayer.Software : ObjectLayer.SoftwareGrid;
    }

    private void SplitPalettes(int size)
    {
        int length = _buffer.Length + size * (ArmyPalettes - 1);
        byte[] split = new byte[length];
        _buffer.AsSpan(size).CopyTo(split.AsSpan(size * ArmyPalettes));
        split.AsSpan(size * ArmyPalettes, size).CopyTo(split.AsSpan(0, size));
        split.AsSpan(size * ArmyPalettes, size).CopyTo(split.AsSpan(size, size));
        split.AsSpan(0, size * 2).CopyTo(split.AsSpan(size * 2, size * 2));
        _buffer = split;

        for (int frame = 0; frame < FrameOffsets.Length; frame++)
        {
            FrameOffsets[frame] += size * (ArmyPalettes - 1);
        }

        FileType |= VidType.PaletteVariants;
        for (var mirror = NextMirror; mirror != this; mirror = mirror.NextMirror)
        {
            mirror.FileType |= VidType.PaletteVariants;
            mirror._buffer = _buffer;
        }
    }

    private void ReadPalette(ReadOnlySpan<byte> record, Span<uint> colors)
    {
        if (FileType.HasFlag(VidType.CurrentVersion))
        {
            MemoryMarshal.Cast<byte, uint>(record[..TrueColorPaletteSize]).CopyTo(colors);
        }
        else
        {
            var packed = MemoryMarshal.Cast<byte, VidPackedColor>(record);
            for (int i = 0; i < PaletteEntries; i++)
            {
                colors[i] = Gamma.OpaqueAlpha | (uint)packed[i].Red << (2 * Gamma.ChannelBits) | (uint)packed[i].Green << Gamma.ChannelBits | packed[i].Blue;
            }
        }

        var gamma = Object.Gamma.Pack();
        if (gamma.Negative != 0 || gamma.Positive != 0)
        {
            for (int i = 0; i < PaletteEntries; i++)
            {
                colors[i] = gamma.Apply(colors[i]);
            }
        }
    }

    private void StorePalette(ReadOnlySpan<uint> colors, int paletteSize)
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
    }

    private void ConvertRowsToRgb555(int record)
    {
        var buffer = _buffer.AsSpan();
        int rows = record + MemoryMarshal.Read<VidFrameHeader>(buffer[record..]).Size;
        int count = MemoryMarshal.Read<VidRowsHeader>(buffer[rows..]).Count;
        int position = rows + Unsafe.SizeOf<VidRowsHeader>();
        for (int row = 0; row < count; row++)
        {
            while (!RunAt(buffer, position).IsEnd)
            {
                int run = RunAt(buffer, position).Count;
                position += RunHeader;
                var pixels = MemoryMarshal.Cast<byte, ushort>(buffer.Slice(position, run * sizeof(ushort)));
                for (int pixel = 0; pixel < pixels.Length; pixel++)
                {
                    pixels[pixel] = (ushort)(pixels[pixel] >> 1 & HighColorGreenMask | pixels[pixel] & HighColorBlueMask);
                }

                position += run * sizeof(ushort);
            }

            position += RunHeader;
        }
    }
}
