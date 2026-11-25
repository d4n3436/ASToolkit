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

    private ushort[] _systemTexels = [];
    private ushort[] _deviceTexels = [];

    [EngineRoutine(0x00403290)]
    internal Texture(Size size, VidPixelFormat format, TextureFlags flags, GameSession session)
    {
        Create(size, format, flags, session);
    }

    [EngineRoutine(0x004032e0)]
    internal Texture(ReadOnlySpan<byte> source, Size size, VidPixelFormat format, ReadOnlySpan<uint> palette, TextureFlags flags, GameSession session)
    {
        Create(size, format, flags, session);
        Palette = Format == VidPixelFormat.P8 ? palette.ToArray() : [];
        uint colorKey = Flags.HasFlag(TextureFlags.AlphaPalette) ? 0 : Direct3DTexture.OpaqueKey;
        var area = new Rectangle(Point.Empty, size);
        View.LoadTexels(area, source, format, GetSourcePitch(format, size.Width), size, area, 1, colorKey, palette);
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
    /// Gets the flags the texture was created with.
    /// </summary>
    public TextureFlags Flags { get; private set; }

    /// <summary>
    /// Gets the colors a P8 texture indexes, packed as 0xAARRGGBB, or an empty array.
    /// </summary>
    public uint[] Palette { get; internal set; } = [];

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

    private static ReadOnlySpan<byte> TextureLogTag => "TEXTURE"u8;

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
    internal void Destruct(GameSession session)
    {
        if (_deviceTexels.Length != 0)
        {
            session.TextureMemoryInUse -= GetTexelBytes(Format) * Size.Width * Size.Height;
            _deviceTexels = [];
        }

        _systemTexels = [];
    }

    [EngineRoutine(0x004036b0)]
    private void Create(Size size, VidPixelFormat format, TextureFlags flags, GameSession session)
    {
        _systemTexels = [];
        _deviceTexels = [];
        if (!session.IsTextureDynamicSupported)
        {
            flags &= ~TextureFlags.Dynamic;
        }

        if (!TryFitSize(size, session, out var fitted))
            return;

        Size = fitted;
        int width = fitted.Width;
        int height = fitted.Height;
        Flags = flags;
        if (format is VidPixelFormat.A8R8G8B8 or VidPixelFormat.A4R4G4B4 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5)
        {
            Flags |= TextureFlags.AlphaPalette;
        }

        format = ChooseFormat(format, flags, Flags, session);
        if (flags.HasFlag(TextureFlags.SystemMemory) && format == VidPixelFormat.D16)
        {
            Format = format;
            _systemTexels = new ushort[width * height];
            return;
        }

        while (!IsSupported(format))
        {
            if (!TryGetFallbackFormat(format, Flags, out var fallback))
            {
                Format = format;
                session.Error.Error(TextureLogTag, LogError.Create, Direct3DTexture.InvalidCall);
                return;
            }

            format = fallback;
        }

        Format = format;
        session.TextureMemoryInUse += GetTexelBytes(format) * width * height;
        _deviceTexels = Direct3DTexture.IsDxt(format)
            ? new ushort[GetCompressedPitch(format, width) * ((height + BlockSize - 1) / BlockSize) / sizeof(ushort)]
            : new ushort[width * height * (Direct3DTexture.Is32Bit(format) ? 2 : 1)];
    }

    [EngineRoutine(0x00403970)]
    internal int CopyFromSurface(Screen surface, Rectangle source, Point destination, MyError? error)
    {
        var view = View;
        int result = surface.IsTrueColor
            ? view.LoadTexels(new Rectangle(destination, source.Size), MemoryMarshal.AsBytes(surface.TrueColorPixels), VidPixelFormat.X8R8G8B8, surface.Pitch * sizeof(uint), surface.Size, source, Direct3DTexture.DefaultFilter, 0)
            : view.LoadTexels(new Rectangle(destination, source.Size), MemoryMarshal.AsBytes(surface.HighColorPixels), surface.Format.IsRgb555 ? VidPixelFormat.X1R5G5B5 : VidPixelFormat.R5G6B5, surface.Pitch * sizeof(ushort), surface.Size, source, Direct3DTexture.DefaultFilter, 0);
        if (result != 0)
        {
            error?.Error(TextureLogTag, LogError.Copy, result);
        }

        return result;
    }

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
    /// <param name="blend">How the quad combines with the colors underneath.</param>
    /// <param name="isAlphaModulated">Whether the alpha of the texels is multiplied by the alpha of the gamma's inverted reductions.</param>
    /// <param name="minimumAlpha">The alpha that a texel needs to be drawn, after the modulation.</param>
    /// <exception cref="NotSupportedException">The texture is DXT-compressed.</exception>
    [EngineRoutine(0x00403ae0)]
    public void DrawFlatQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isAlphaModulated = false, int minimumAlpha = 0) =>
        View.DrawFlatQuad(screen, destination, source, gamma, blend, isAlphaModulated, minimumAlpha);

    /// <summary>
    /// Draws a rectangle of the texture to the screen as a quad, with a gamma applied.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="destination">The area of the screen.</param>
    /// <param name="source">The area of the texture.</param>
    /// <param name="gamma">The gamma that darkens the texels and then brightens them.</param>
    /// <param name="blend">How the quad combines with the colors underneath.</param>
    /// <param name="isDoubled">Whether the texels are doubled after the gamma and before they are blended.</param>
    /// <param name="topDepth">The depth of the vertices on the top edge of the quad.</param>
    /// <param name="bottomDepth">The depth of the vertices on the bottom edge of the quad.</param>
    /// <exception cref="NotSupportedException">The texture is DXT-compressed.</exception>
    [EngineRoutine(0x00403dd0)]
    public void DrawQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isDoubled = false, float topDepth = 0, float bottomDepth = 0) =>
        View.DrawQuad(screen, destination, source, gamma, blend, isDoubled, topDepth, bottomDepth);

    private static int GetSourcePitch(VidPixelFormat format, int width) => format switch
    {
        VidPixelFormat.R8G8B8 => 24 * width >> 3,
        VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 32 * width >> 3,
        VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 16 * width >> 3,
        VidPixelFormat.P8 => 8 * width >> 3,
        VidPixelFormat.Dxt1 => 4 * width >> 1,
        VidPixelFormat.Dxt2 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt4 or VidPixelFormat.Dxt5 => 8 * width >> 1,
        _ => 0
    };

    private static int GetCompressedPitch(VidPixelFormat format, int width) => (width + BlockSize - 1) / BlockSize * (format == VidPixelFormat.Dxt1 ? 8 : 16);

    private static int GetTexelBytes(VidPixelFormat format) => format == VidPixelFormat.P8 ? PaletteTexelBytes : ColorTexelBytes;

    private static VidPixelFormat ReplacePalette(VidPixelFormat format, GameSession session) => format == VidPixelFormat.P8 && !session.IsTexturePaletteSupported ? VidPixelFormat.R5G6B5 : format;

    private static bool IsSupported(VidPixelFormat format) => Graphics.DeviceTextureFormats.Contains(format);

    private static bool TryFitSize(Size size, GameSession session, out Size fitted)
    {
        int width = size.Width;
        int height = size.Height;
        if (session.IsTextureSquareRequired)
        {
            width = Math.Max(width, height);
            height = width;
        }

        if (session.IsTexturePowerOfTwoRequired)
        {
            width = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(width, 1));
            height = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(height, 1));
        }

        fitted = new Size(width, height);
        if (width > session.TextureMaxSize.Width || width < 1)
        {
            session.Error.Error(TextureLogTag, LogError.Invalid, width, "initial sizeX"u8);

            return false;
        }

        if (height > session.TextureMaxSize.Height || height < 1)
        {
            session.Error.Error(TextureLogTag, LogError.Invalid, height, "initial sizeY"u8);

            return false;
        }

        return true;
    }

    private static VidPixelFormat ChooseFormat(VidPixelFormat format, TextureFlags flags, TextureFlags stored, GameSession session)
    {
        if (format == VidPixelFormat.P8 && stored.HasFlag(TextureFlags.AlphaPalette) && !session.IsTextureAlphaPaletteSupported)
        {
            format = VidPixelFormat.A8R8G8B8;
        }

        if (!flags.HasFlag(TextureFlags.HasAlpha))
            return ReplacePalette(format, session);

        if (format == VidPixelFormat.P8)
            return stored.HasFlag(TextureFlags.AlphaPalette) ? ReplacePalette(format, session) : VidPixelFormat.A1R5G5B5;

        if (format == VidPixelFormat.R5G6B5)
            return VidPixelFormat.A1R5G5B5;

        if (format == VidPixelFormat.R8G8B8)
            return VidPixelFormat.A8R8G8B8;

        return ReplacePalette(format, session);
    }

    private static bool TryGetFallbackFormat(VidPixelFormat format, TextureFlags stored, out VidPixelFormat fallback)
    {
        fallback = format switch
        {
            VidPixelFormat.P8 => stored.HasFlag(TextureFlags.AlphaPalette) ? VidPixelFormat.A8R8G8B8 : VidPixelFormat.R5G6B5,
            VidPixelFormat.Dxt1 => VidPixelFormat.R5G6B5,
            VidPixelFormat.Dxt3 => VidPixelFormat.A4R4G4B4,
            VidPixelFormat.R5G6B5 => VidPixelFormat.X1R5G5B5,
            VidPixelFormat.X1R5G5B5 => VidPixelFormat.A1R5G5B5,
            VidPixelFormat.R8G8B8 => VidPixelFormat.X8R8G8B8,
            VidPixelFormat.X8R8G8B8 => VidPixelFormat.A8R8G8B8,
            _ => VidPixelFormat.None
        };

        return fallback != VidPixelFormat.None;
    }
}
