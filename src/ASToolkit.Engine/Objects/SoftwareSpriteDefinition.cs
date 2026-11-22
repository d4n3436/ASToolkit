using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using ASToolkit.Engine.IO;
using ASToolkit.Core;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a definition drawn in software from runs of pixels into a 32-bit back buffer.
/// </summary>
public partial class SoftwareSpriteDefinition : SpriteDefinition
{
    private protected const int PaletteEntries = 256;
    private protected const int TrueColorPaletteSize = PaletteEntries * sizeof(uint);
    private protected const int HighColorPaletteSize = PaletteEntries * sizeof(ushort);
    private protected const int ArmyPalettes = 4;
    private protected const int FrameHeader = 4;
    private protected const int AliasBody = sizeof(short);
    private static readonly int MissingFramesLength = Unsafe.SizeOf<VidFrameHeader>() + Unsafe.SizeOf<VidRowsHeader>();
    private protected const ushort HighColorGreenMask = 0x7fe0;
    private protected const ushort HighColorBlueMask = 0x1f;

    private byte[] _palettes = [];
    private ReadOnlyMemory<byte> _frames;

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

        if (FileType.HasFlag(VidType.Palette))
        {
            int paletteSize = GetPaletteSize();
            Span<uint> colors = stackalloc uint[PaletteEntries];
            ReadPalette(file.Palette.Span, colors);
            _palettes = new byte[paletteSize * 2];
            StorePalette(colors, paletteSize);
        }

        var records = file.FrameRecords;
        FrameOffsets = new int[FileFrameCount];
        bool isConverted = !FileType.HasFlag(VidType.Palette) && Format.IsRgb555 && IsHighColor && !FileType.HasFlag(VidType.Blended);
        byte[] converted = [];
        if (isConverted)
        {
            int total = 0;
            foreach (var record in records)
            {
                total += Math.Max(0, record.Length - FrameHeader);
            }

            converted = new byte[total];
        }

        var source = file.Data.Span;
        int position = 0;
        for (int frame = 0; frame < FileFrameCount && frame < records.Length; frame++)
        {
            var body = records[frame].Span[FrameHeader..];
            if (body.Length == AliasBody)
            {
                int target = MemoryMarshal.Read<short>(body);
                // TODO: Port the unchecked alias read
                FrameOffsets[frame] = target >= 0 && target < FileFrameCount ? FrameOffsets[target] : 0;
                continue;
            }

            if (isConverted)
            {
                body.CopyTo(converted.AsSpan(position));
                ConvertRowsToRgb555(converted, position);
                FrameOffsets[frame] = position;
                position += body.Length;
            }
            else
            {
                FrameOffsets[frame] = (int)Unsafe.ByteOffset(ref MemoryMarshal.GetReference(source), ref MemoryMarshal.GetReference(body));
            }
        }

        // TODO: Port the uninitialized frame buffer of a file without DATA
        _frames = records.IsEmpty ? new byte[MissingFramesLength] : isConverted ? converted : file.Data;

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
    /// Gets the palettes of the sprite file, holding the current palette or one palette per army, followed by the palette as the file stores it.
    /// </summary>
    public ReadOnlySpan<byte> Palettes => _palettes;

    /// <summary>
    /// Gets the data that holds the frame records.
    /// </summary>
    public ReadOnlySpan<byte> Frames => _frames.Span;

    /// <summary>
    /// Gets the start of each frame record in <see cref="Frames"/>.
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
        if (_palettes.Length == 0 || !FileType.HasFlag(VidType.Palette))
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

            _palettes.AsSpan(size, size).CopyTo(_palettes);
            if (ambientApplies)
            {
                ApplyGamma(_palettes.AsSpan(0, size), gamma);
            }

            return;
        }

        Gammas[army] = gamma;
        if (!FileType.HasFlag(VidType.PaletteVariants))
        {
            SplitPalettes(size);
        }

        var slot = _palettes.AsSpan(size * army, size);
        _palettes.AsSpan(size * ArmyPalettes, size).CopyTo(slot);
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
        byte[] split = new byte[size * (ArmyPalettes + 1)];
        _palettes.AsSpan(size, size).CopyTo(split.AsSpan(size * ArmyPalettes));
        split.AsSpan(size * ArmyPalettes, size).CopyTo(split.AsSpan(0, size));
        split.AsSpan(size * ArmyPalettes, size).CopyTo(split.AsSpan(size, size));
        split.AsSpan(0, size * 2).CopyTo(split.AsSpan(size * 2, size * 2));
        _palettes = split;

        FileType |= VidType.PaletteVariants;
        for (var mirror = NextMirror; mirror != this; mirror = mirror.NextMirror)
        {
            mirror.FileType |= VidType.PaletteVariants;
            mirror._palettes = _palettes;
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
            MemoryMarshal.Cast<uint, byte>(colors).CopyTo(_palettes);
        }
        else
        {
            var pixels = MemoryMarshal.Cast<byte, ushort>(_palettes.AsSpan(0, paletteSize));
            for (int i = 0; i < PaletteEntries; i++)
            {
                pixels[i] = Format.Pack(colors[i]);
            }
        }

        _palettes.AsSpan(0, paletteSize).CopyTo(_palettes.AsSpan(paletteSize));
    }

    private static void ConvertRowsToRgb555(Span<byte> buffer, int record)
    {
        int rows = record + MemoryMarshal.AsRef<VidFrameHeader>(buffer[record..]).Size;
        int count = MemoryMarshal.AsRef<VidRowsHeader>(buffer[rows..]).Count;
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
