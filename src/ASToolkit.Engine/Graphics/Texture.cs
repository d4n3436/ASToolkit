using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a texture that holds its texels in the layout the device stores them in.
/// </summary>
public sealed class Texture
{
    private const int PaletteTexelBytes = 1;
    private const int ColorTexelBytes = 2;
    private const int BlockSize = 4;

    private readonly GameSession _session;
    private ushort[] _systemTexels = [];
    private ushort[] _deviceTexels = [];

    [EngineRoutine(0x00403290)]
    internal Texture(Size size, VidPixelFormat format, TextureFlags flags, GameSession session)
    {
        _session = session;
        Create(size, format, flags);
    }

    [EngineRoutine(0x004032e0)]
    internal Texture(ReadOnlySpan<byte> source, Size size, VidPixelFormat format, ReadOnlySpan<uint> palette, TextureFlags flags, GameSession session)
    {
        _session = session;
        Create(size, format, flags);
        if (Format == VidPixelFormat.P8)
        {
            SetPalette(palette);
        }

        uint colorKey = Flags.HasFlag(TextureFlags.AlphaPalette) ? 0 : Direct3DTexture.OpaqueKey;
        var area = new Rectangle(Point.Empty, size);
        int sourcePitch = format switch
        {
            VidPixelFormat.R8G8B8 => 24 * size.Width >> 3,
            VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 32 * size.Width >> 3,
            VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 16 * size.Width >> 3,
            VidPixelFormat.P8 => 8 * size.Width >> 3,
            VidPixelFormat.Dxt1 => 4 * size.Width >> 1,
            VidPixelFormat.Dxt2 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt4 or VidPixelFormat.Dxt5 => 8 * size.Width >> 1,
            _ => 0
        };
        View.LoadTexels(area, source, format, sourcePitch, size, area, 1, colorKey, palette);
    }

    /// <summary>
    /// Gets the layout of the texels.
    /// </summary>
    public VidPixelFormat Format { get; private set; }

    /// <summary>
    /// Gets the size of the texture, in texels.
    /// </summary>
    public Size Size { get; private set; }

    /// <summary>
    /// Gets the number the device gave the palette of a P8 texture.
    /// </summary>
    public int PaletteNumber { get; private set; }

    /// <summary>
    /// Gets the flags the texture was created with.
    /// </summary>
    public TextureFlags Flags { get; private set; }

    /// <summary>
    /// Gets the colors a P8 texture indexes, packed as 0xAARRGGBB, or an empty array.
    /// </summary>
    public uint[] Palette { get; private set; } = [];

    /// <summary>
    /// Gets the texels, where a texel of a 32-bit layout takes two elements and the blocks of a compressed layout are packed.
    /// </summary>
    public Span<ushort> Texels => _deviceTexels.Length != 0 ? _deviceTexels : _systemTexels;

    /// <summary>
    /// Gets a value indicating whether the texture doesn't have any texels.
    /// </summary>
    public bool IsEmpty => _deviceTexels.Length == 0 && _systemTexels.Length == 0;

    /// <summary>
    /// Gets a view of the texture for drawing.
    /// </summary>
    public Direct3DTexture View => new(Texels, Size, Format, Palette);

    internal Graphics Graphics => _session.Graphics;

    private static ReadOnlySpan<byte> TextureLogTag => "TEXTURE"u8;

    /// <summary>
    /// Returns the texels of the texture from a given texel to its end.
    /// </summary>
    /// <param name="origin">The first texel of the area, or <see langword="null"/> for the whole texture.</param>
    /// <param name="pitch">The number of bytes from the start of one row to the next.</param>
    /// <returns>The texels from the first texel of the area to the end of the texture.</returns>
    [EngineRoutine(0x00403a40)]
    public Span<ushort> Lock(Point? origin, out int pitch) => View.Lock(origin, out pitch);

    /// <summary>
    /// Draws a rectangle of the texture to the screen as a quad that lies in front of everything, with a gamma applied.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="destination">The area of the screen.</param>
    /// <param name="source">The area of the texture.</param>
    /// <param name="gamma">The gamma that darkens the texels and then brightens them.</param>
    /// <exception cref="NotSupportedException">The texture is DXT-compressed.</exception>
    [EngineRoutine(0x00403ae0)]
    public void DrawFlatQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma)
    {
        var graphics = _session.Graphics;
        View.DrawFlatQuad(screen, destination, source, gamma, graphics.Blend, graphics.IsAlphaModulated);
    }

    /// <summary>
    /// Draws a rectangle of the texture to the screen as a quad, with a gamma applied.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="destination">The area of the screen.</param>
    /// <param name="source">The area of the texture.</param>
    /// <param name="gamma">The gamma that darkens the texels and then brightens them.</param>
    /// <param name="topDepth">The depth of the vertices on the top edge of the quad.</param>
    /// <param name="bottomDepth">The depth of the vertices on the bottom edge of the quad.</param>
    /// <exception cref="NotSupportedException">The texture is DXT-compressed.</exception>
    [EngineRoutine(0x00403dd0)]
    public void DrawQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, float topDepth = 0, float bottomDepth = 0)
    {
        var graphics = _session.Graphics;
        View.DrawQuad(screen, destination, source, gamma, graphics.Blend, graphics.IsDoubled, topDepth, bottomDepth);
    }

    [EngineRoutine(0x00402fa0)]
    internal static string SetDeviceCapabilities(GameSession session)
    {
        session.TextureMaxSize = Graphics.DeviceMaxTextureSize;
        session.IsTextureSquareRequired = Graphics.AreSquareTexturesRequired;
        session.IsTextureConditionalNonPowerOfTwoSupported = Graphics.IsConditionalNonPowerOfTwoSupported;
        session.IsTextureDynamicSupported = false;
        session.IsTexturePowerOfTwoRequired = Graphics.IsPowerOfTwoRequired;
        session.IsTextureAlphaPaletteSupported = Graphics.IsAlphaPaletteSupported;
        var formats = Graphics.DeviceTextureFormats;
        if (formats.Contains(VidPixelFormat.P8))
        {
            session.IsTexturePaletteSupported = true;
        }
        else
        {
            session.IsTextureAlphaPaletteSupported = false;
        }

        if (formats.Contains(VidPixelFormat.Dxt1))
        {
            session.IsTextureDxt1Supported = true;
        }

        if (formats.Contains(VidPixelFormat.Dxt3))
        {
            session.IsTextureDxt3Supported = true;
        }

        if (formats.Contains(VidPixelFormat.Dxt5))
        {
            session.IsTextureDxt5Supported = true;
        }

        session.IsTexturePaletteSupported = false;
        session.IsTextureAlphaPaletteSupported = false;
        var text = new List<byte>();
        if (session.IsTextureSquareRequired)
        {
            text.AddRange("SQUARE "u8);
        }

        text.AddRange(session.IsTexturePowerOfTwoRequired ? "POWER2 "u8 : "NOTPOWER2 "u8);
        if (session.IsTextureConditionalNonPowerOfTwoSupported)
        {
            text.AddRange("COND_NON_POW2 "u8);
        }

        if (session.IsTextureDynamicSupported)
        {
            text.AddRange("DYNAMIC "u8);
        }

        if (session.IsTexturePaletteSupported)
        {
            text.AddRange("PALETTE "u8);
        }

        if (session.IsTextureAlphaPaletteSupported)
        {
            text.AddRange("ALPHA_PALETTE "u8);
        }

        if (session.IsTextureDxt1Supported)
        {
            text.AddRange("DXT1 "u8);
        }

        if (session.IsTextureDxt3Supported)
        {
            text.AddRange("DXT3 "u8);
        }

        if (session.IsTextureDxt5Supported)
        {
            text.AddRange("DXT5 "u8);
        }

        text.AddRange(Encoding.ASCII.GetBytes($"MAXSIZE={session.TextureMaxSize.Width},{session.TextureMaxSize.Height}"));
        string line = $"TextureCaps={MyError.Decode(CollectionsMarshal.AsSpan(text))}";
        session.IsTexturePowerOfTwoRequired = true;
        return line;
    }

    [EngineRoutine(0x00403620)]
    internal void Destruct()
    {
        if (_deviceTexels.Length != 0)
        {
            _session.TextureMemoryInUse -= GetTexelBytes(Format) * Size.Width * Size.Height;
            _deviceTexels = [];
        }

        _systemTexels = [];
    }

    [EngineRoutine(0x00403970)]
    internal int CopyFromSurface(Screen surface, Rectangle source, Point destination)
    {
        var view = View;
        int result = surface.IsTrueColor
            ? view.LoadTexels(new Rectangle(destination, source.Size), MemoryMarshal.AsBytes(surface.TrueColorPixels), VidPixelFormat.X8R8G8B8, surface.Pitch * sizeof(uint), surface.Size, source, Direct3DTexture.DefaultFilter, 0)
            : view.LoadTexels(new Rectangle(destination, source.Size), MemoryMarshal.AsBytes(surface.HighColorPixels), surface.Format.IsRgb555 ? VidPixelFormat.X1R5G5B5 : VidPixelFormat.R5G6B5, surface.Pitch * sizeof(ushort), surface.Size, source, Direct3DTexture.DefaultFilter, 0);
        if (result != 0)
        {
            _session.Error.Error(TextureLogTag, LogError.Copy, result);
        }

        return result;
    }

    [EngineRoutine(0x004040c0)]
    internal void SetPalette(ReadOnlySpan<uint> colors)
    {
        if (_deviceTexels.Length == 0)
        {
            _session.Error.Error(TextureLogTag, LogError.Set, 0, "palette for non initialized texture"u8);
            return;
        }

        if (Format != VidPixelFormat.P8)
        {
            _session.Error.Error(TextureLogTag, LogError.Set, 0, "palette for non palette texture"u8);
            return;
        }

        Palette = colors.ToArray();
        PaletteNumber = _session.TexturePaletteCount++;
    }

    private static int GetTexelBytes(VidPixelFormat format) => format == VidPixelFormat.P8 ? PaletteTexelBytes : ColorTexelBytes;

    private VidPixelFormat ReplacePalette(VidPixelFormat format) => format == VidPixelFormat.P8 && !_session.IsTexturePaletteSupported ? VidPixelFormat.R5G6B5 : format;

    [EngineRoutine(0x004036b0)]
    private void Create(Size size, VidPixelFormat format, TextureFlags flags)
    {
        _systemTexels = [];
        _deviceTexels = [];
        if (!_session.IsTextureDynamicSupported)
        {
            flags &= ~TextureFlags.Dynamic;
        }

        int width = size.Width;
        int height = size.Height;
        if (_session.IsTextureSquareRequired)
        {
            width = Math.Max(width, height);
            height = width;
        }

        if (_session.IsTexturePowerOfTwoRequired)
        {
            width = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(width, 1));
            height = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(height, 1));
        }

        if (width > _session.TextureMaxSize.Width || width < 1)
        {
            _session.Error.Error(TextureLogTag, LogError.Invalid, width, "initial sizeX"u8);

            return;
        }

        if (height > _session.TextureMaxSize.Height || height < 1)
        {
            _session.Error.Error(TextureLogTag, LogError.Invalid, height, "initial sizeY"u8);

            return;
        }

        Size = new Size(width, height);
        Flags = flags;
        if (format is VidPixelFormat.A8R8G8B8 or VidPixelFormat.A4R4G4B4 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5)
        {
            Flags |= TextureFlags.AlphaPalette;
        }

        if (format == VidPixelFormat.P8 && Flags.HasFlag(TextureFlags.AlphaPalette) && !_session.IsTextureAlphaPaletteSupported)
        {
            format = VidPixelFormat.A8R8G8B8;
        }

        if (!flags.HasFlag(TextureFlags.HasAlpha))
        {
            format = ReplacePalette(format);
        }
        else if (format == VidPixelFormat.P8)
        {
            format = Flags.HasFlag(TextureFlags.AlphaPalette) ? ReplacePalette(format) : VidPixelFormat.A1R5G5B5;
        }
        else if (format == VidPixelFormat.R5G6B5)
        {
            format = VidPixelFormat.A1R5G5B5;
        }
        else if (format == VidPixelFormat.R8G8B8)
        {
            format = VidPixelFormat.A8R8G8B8;
        }
        else
        {
            format = ReplacePalette(format);
        }

        if (flags.HasFlag(TextureFlags.SystemMemory) && format == VidPixelFormat.D16)
        {
            Format = format;
            _systemTexels = new ushort[width * height];
            return;
        }

        while (!Graphics.DeviceTextureFormats.Contains(format))
        {
            var fallback = format switch
            {
                VidPixelFormat.P8 => Flags.HasFlag(TextureFlags.AlphaPalette) ? VidPixelFormat.A8R8G8B8 : VidPixelFormat.R5G6B5,
                VidPixelFormat.Dxt1 => VidPixelFormat.R5G6B5,
                VidPixelFormat.Dxt3 => VidPixelFormat.A4R4G4B4,
                VidPixelFormat.R5G6B5 => VidPixelFormat.X1R5G5B5,
                VidPixelFormat.X1R5G5B5 => VidPixelFormat.A1R5G5B5,
                VidPixelFormat.R8G8B8 => VidPixelFormat.X8R8G8B8,
                VidPixelFormat.X8R8G8B8 => VidPixelFormat.A8R8G8B8,
                _ => VidPixelFormat.None
            };

            if (fallback == VidPixelFormat.None)
            {
                Format = format;
                _session.Error.Error(TextureLogTag, LogError.Create, Direct3DTexture.InvalidCall);
                return;
            }

            format = fallback;
        }

        Format = format;
        _session.TextureMemoryInUse += GetTexelBytes(format) * width * height;
        _deviceTexels = Direct3DTexture.IsDxt(format)
            ? new ushort[(width + BlockSize - 1) / BlockSize * (format == VidPixelFormat.Dxt1 ? 8 : 16) * ((height + BlockSize - 1) / BlockSize) / sizeof(ushort)]
            : new ushort[width * height * (Direct3DTexture.Is32Bit(format) ? 2 : 1)];
    }
}
