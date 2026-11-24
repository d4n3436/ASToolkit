using System.Drawing;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Screen
{
    internal const int ColorTableLength = 256;
    internal const int TextureSide = 256;
    internal const int ColorTableGroup = 8;

    internal static ReadOnlySpan<int> ColorTableOffsets =>
    [
        -8, -8, -8,
        -7, -7, 0,
        0, -6, -6,
        0, -5, 0,
        -4, 0, -4,
        -3, 0, 0,
        0, 0, -2,
        0, 0, 0
    ];

    private const int GrayLength = 256;

    private static readonly ushort[] DefaultTable = CreateColorTable(HighColorFormat.Rgb565);

    private DeviceBuffers? _buffers;

    /// <summary>
    /// Gets the size of the scratch texture, in pixels.
    /// </summary>
    public Size ScratchSize => new(TextureSide, TextureSide);

    /// <summary>
    /// Gets the pixels of the scratch texture that sprite frames are blended from, 256 by 256 in ARGB4444, row by row from the top.
    /// </summary>
    public Span<ushort> Scratch => EnsureBuffers().Alpha;

    /// <summary>
    /// Gets the 256 by 256 texture used for the ground snow, the fog and the lights of the first light buffer.
    /// </summary>
    public Span<ushort> TextureBuffer => EnsureBuffers().Light;

    /// <summary>
    /// Gets the layout of <see cref="TextureBuffer"/>.
    /// </summary>
    public VidPixelFormat TextureFormat => EnsureBuffers().LightFormat;

    /// <summary>
    /// Gets the palette of <see cref="TextureBuffer"/>, 256 shades of gray, or empty if its layout isn't a palette layout.
    /// </summary>
    public ReadOnlySpan<uint> TexturePalette => EnsureBuffers().LightFormat == VidPixelFormat.P8 ? GrayPalette : default;

    /// <summary>
    /// Gets the 256 by 256 light buffer that the next light is drawn to, one of two buffers used in turns.
    /// </summary>
    public Span<ushort> LightBuffer
    {
        get
        {
            var textures = EnsureBuffers();

            return LightPhase == 0 ? textures.Light : textures.High;
        }
    }

    /// <summary>
    /// Gets the layout of <see cref="LightBuffer"/>.
    /// </summary>
    public VidPixelFormat LightFormat
    {
        get
        {
            var textures = EnsureBuffers();

            return LightPhase == 0 ? textures.LightFormat : textures.HighFormat;
        }
    }

    internal static ReadOnlySpan<ushort> DefaultColorTable => DefaultTable;

    internal MyError? Error { get; set; }

    internal Graphics? Owner { get; set; }

    internal ReadOnlySpan<ushort> ColorTable => EnsureBuffers().ColorTable;

    private static uint[] GrayPalette { get; } = CreateGrayPalette();

    private static uint[] CreateGrayPalette()
    {
        uint[] palette = new uint[GrayLength];
        for (int i = 0; i < palette.Length; i++)
        {
            int level = Math.Clamp(i, 0, byte.MaxValue);
            palette[i] = 0xff000000 | (uint)(level << 16 | level << 8 | level);
        }

        return palette;
    }

    private static ushort[] CreateColorTable(HighColorFormat format)
    {
        ushort[] table = new ushort[ColorTableLength];
        int index = 0;
        for (int top = ColorTableGroup; top <= byte.MaxValue + 1; top += ColorTableGroup)
        {
            for (int i = 0; i < ColorTableGroup; i++)
            {
                int red = Math.Clamp(top + ColorTableOffsets[i * 3], 0, byte.MaxValue);
                int green = Math.Clamp(top + ColorTableOffsets[i * 3 + 1], 0, byte.MaxValue);
                int blue = Math.Clamp(top + ColorTableOffsets[i * 3 + 2], 0, byte.MaxValue);
                table[index++] = format.Pack((uint)(red << 16 | green << 8 | blue));
            }
        }

        return table;
    }

    private DeviceBuffers EnsureBuffers()
    {
        var textures = Owner?.Buffers ?? (_buffers ??= new DeviceBuffers());
        if (textures.IsCreated)
            return textures;

        textures.IsCreated = true;
        textures.Light = CreateTexture(VidPixelFormat.P8, "light buffer"u8, out var format);
        textures.LightFormat = format;
        if (textures.Light.Length == 0)
            return textures;

        textures.High = CreateTexture(VidPixelFormat.R5G6B5, "hiBuffer"u8, out format);
        textures.HighFormat = format;
        if (textures.High.Length == 0)
            return textures;

        textures.Alpha = CreateTexture(VidPixelFormat.A4R4G4B4, "alphaBuffer"u8, out _);
        if (textures.Alpha.Length == 0)
            return textures;

        textures.ColorTable = CreateColorTable(textures.HighFormat == VidPixelFormat.R5G6B5 ? HighColorFormat.Rgb565 : HighColorFormat.Rgb555);
        return textures;
    }

    private ushort[] CreateTexture(VidPixelFormat format, ReadOnlySpan<byte> name, out VidPixelFormat result)
    {
        ushort[] texels = Graphics.CreateTexture(Error, format, out result);
        if (texels.Length == 0)
        {
            Error?.Error("GRAPH"u8, LogError.Create, 0, name);
        }

        return texels;
    }
}
