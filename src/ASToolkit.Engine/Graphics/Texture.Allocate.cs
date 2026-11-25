using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public readonly ref partial struct Texture
{
    private const int PaletteTexelBytes = 1;
    private const int ColorTexelBytes = 2;
    private const int InvalidCall = -0x7789f794;
    private const int BlockSize = 4;

    private static ReadOnlySpan<byte> TextureLogTag => "TEXTURE"u8;

    [EngineRoutine(0x004036b0)]
    internal static ushort[] Allocate(ref Size size, VidPixelFormat format, TextureFlags flags, GameSession session, out TextureFlags stored, out VidPixelFormat result)
    {
        result = VidPixelFormat.None;
        stored = default;
        if (!session.IsTextureDynamicSupported)
        {
            flags &= ~TextureFlags.Dynamic;
        }

        if (!TryFitSize(size, session, out var fitted))
            return [];

        size = fitted;
        int width = fitted.Width;
        int height = fitted.Height;
        stored = flags;
        if (format is VidPixelFormat.A8R8G8B8 or VidPixelFormat.A4R4G4B4 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5)
        {
            stored |= TextureFlags.AlphaPalette;
        }

        format = ChooseFormat(format, flags, stored, session);
        if (flags.HasFlag(TextureFlags.SystemMemory) && format == VidPixelFormat.D16)
        {
            result = format;

            return new ushort[width * height];
        }

        while (!IsSupported(format))
        {
            if (!TryGetFallbackFormat(format, stored, out var fallback))
            {
                result = format;
                session.Error.Error(TextureLogTag, LogError.Create, InvalidCall);

                return [];
            }

            format = fallback;
        }

        result = format;
        session.TextureMemoryInUse += GetTexelBytes(format) * width * height;

        return IsDxt(format)
            ? new ushort[GetCompressedPitch(format, width) * ((height + BlockSize - 1) / BlockSize) / sizeof(ushort)]
            : new ushort[width * height * (Is32Bit(format) ? 2 : 1)];
    }

    [EngineRoutine(0x00403620)]
    internal static void Release(GameSession session, Size size, VidPixelFormat format)
    {
        if (format != VidPixelFormat.D16)
        {
            session.TextureMemoryInUse -= GetTexelBytes(format) * size.Width * size.Height;
        }
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

    private static bool IsDxt(VidPixelFormat format) => format is VidPixelFormat.Dxt1 or VidPixelFormat.Dxt2 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt4 or VidPixelFormat.Dxt5;

    private static int GetCompressedPitch(VidPixelFormat format, int width) => (width + BlockSize - 1) / BlockSize * (format == VidPixelFormat.Dxt1 ? 8 : 16);

    private static int GetTexelBytes(VidPixelFormat format) => format == VidPixelFormat.P8 ? PaletteTexelBytes : ColorTexelBytes;

    private static bool Is32Bit(VidPixelFormat format) => format is VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 or VidPixelFormat.R8G8B8 or VidPixelFormat.X8L8V8U8 or VidPixelFormat.Q8W8V8U8 or VidPixelFormat.V16U16 or VidPixelFormat.W11V11U10;

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
