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

    private static Size _maxSize;
    private static bool _isSquareRequired;
    private static bool _isConditionalNonPowerOfTwo;
    private static bool _isDynamicSupported;
    private static bool _isPowerOfTwoRequired;
    private static bool _isAlphaPaletteSupported;
    private static bool _isPaletteSupported;
    private static bool _isDxt1Supported;
    private static bool _isDxt3Supported;
    private static bool _isDxt5Supported;
    private static int _memoryInUse;

    internal static string CapabilitiesText { get; } = ReadCapabilities();

    internal static int MemoryInUse => _memoryInUse;

    private static ReadOnlySpan<byte> TextureLogTag => "TEXTURE"u8;

    [EngineRoutine(0x004036b0)]
    internal static ushort[] Allocate(ref Size size, VidPixelFormat format, TextureFlags flags, MyError? error, out TextureFlags stored, out VidPixelFormat result)
    {
        result = VidPixelFormat.None;
        stored = default;
        if (!_isDynamicSupported)
        {
            flags &= ~TextureFlags.Dynamic;
        }

        if (!TryFitSize(size, error, out var fitted))
            return [];

        size = fitted;
        int width = fitted.Width;
        int height = fitted.Height;
        stored = flags;
        if (format is VidPixelFormat.A8R8G8B8 or VidPixelFormat.A4R4G4B4 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5)
        {
            stored |= TextureFlags.AlphaPalette;
        }

        format = ChooseFormat(format, flags, stored);
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
                error?.Error(TextureLogTag, LogError.Create, InvalidCall);

                return [];
            }

            format = fallback;
        }

        result = format;
        _memoryInUse += GetTexelBytes(format) * width * height;

        return IsDxt(format)
            ? new ushort[GetCompressedPitch(format, width) * ((height + BlockSize - 1) / BlockSize) / sizeof(ushort)]
            : new ushort[width * height * (Is32Bit(format) ? 2 : 1)];
    }

    [EngineRoutine(0x00403620)]
    internal static void Release(Size size, VidPixelFormat format)
    {
        if (format != VidPixelFormat.D16)
        {
            _memoryInUse -= GetTexelBytes(format) * size.Width * size.Height;
        }
    }

    [EngineRoutine(0x00402fa0)]
    private static string ReadCapabilities()
    {
        _maxSize = Graphics.DeviceMaxTextureSize;
        _isSquareRequired = Graphics.AreSquareTexturesRequired;
        _isConditionalNonPowerOfTwo = Graphics.IsConditionalNonPowerOfTwoSupported;
        _isDynamicSupported = false;
        _isPowerOfTwoRequired = Graphics.IsPowerOfTwoRequired;
        _isAlphaPaletteSupported = Graphics.IsAlphaPaletteSupported;
        var formats = Graphics.DeviceTextureFormats;
        if (formats.Contains(VidPixelFormat.P8))
        {
            _isPaletteSupported = true;
        }
        else
        {
            _isAlphaPaletteSupported = false;
        }

        if (formats.Contains(VidPixelFormat.Dxt1))
        {
            _isDxt1Supported = true;
        }

        if (formats.Contains(VidPixelFormat.Dxt3))
        {
            _isDxt3Supported = true;
        }

        if (formats.Contains(VidPixelFormat.Dxt5))
        {
            _isDxt5Supported = true;
        }

        _isPaletteSupported = false;
        _isAlphaPaletteSupported = false;
        var text = new List<byte>();
        if (_isSquareRequired)
        {
            text.AddRange("SQUARE "u8);
        }

        text.AddRange(_isPowerOfTwoRequired ? "POWER2 "u8 : "NOTPOWER2 "u8);
        if (_isConditionalNonPowerOfTwo)
        {
            text.AddRange("COND_NON_POW2 "u8);
        }

        if (_isDynamicSupported)
        {
            text.AddRange("DYNAMIC "u8);
        }

        if (_isPaletteSupported)
        {
            text.AddRange("PALETTE "u8);
        }

        if (_isAlphaPaletteSupported)
        {
            text.AddRange("ALPHA_PALETTE "u8);
        }

        if (_isDxt1Supported)
        {
            text.AddRange("DXT1 "u8);
        }

        if (_isDxt3Supported)
        {
            text.AddRange("DXT3 "u8);
        }

        if (_isDxt5Supported)
        {
            text.AddRange("DXT5 "u8);
        }

        text.AddRange(Encoding.ASCII.GetBytes($"MAXSIZE={_maxSize.Width},{_maxSize.Height}"));
        string line = $"TextureCaps={MyError.Decode(CollectionsMarshal.AsSpan(text))}";
        _isPowerOfTwoRequired = true;
        return line;
    }

    private static bool IsDxt(VidPixelFormat format) => format is VidPixelFormat.Dxt1 or VidPixelFormat.Dxt2 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt4 or VidPixelFormat.Dxt5;

    private static int GetCompressedPitch(VidPixelFormat format, int width) => (width + BlockSize - 1) / BlockSize * (format == VidPixelFormat.Dxt1 ? 8 : 16);

    private static int GetTexelBytes(VidPixelFormat format) => format == VidPixelFormat.P8 ? PaletteTexelBytes : ColorTexelBytes;

    private static bool Is32Bit(VidPixelFormat format) => format is VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 or VidPixelFormat.R8G8B8 or VidPixelFormat.X8L8V8U8 or VidPixelFormat.Q8W8V8U8 or VidPixelFormat.V16U16 or VidPixelFormat.W11V11U10;

    private static VidPixelFormat ReplacePalette(VidPixelFormat format) => format == VidPixelFormat.P8 && !_isPaletteSupported ? VidPixelFormat.R5G6B5 : format;

    private static bool IsSupported(VidPixelFormat format) => Graphics.DeviceTextureFormats.Contains(format);

    private static bool TryFitSize(Size size, MyError? error, out Size fitted)
    {
        int width = size.Width;
        int height = size.Height;
        if (_isSquareRequired)
        {
            width = Math.Max(width, height);
            height = width;
        }

        if (_isPowerOfTwoRequired)
        {
            width = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(width, 1));
            height = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(height, 1));
        }

        fitted = new Size(width, height);
        if (width > _maxSize.Width || width < 1)
        {
            error?.Error(TextureLogTag, LogError.Invalid, width, "initial sizeX"u8);

            return false;
        }

        if (height > _maxSize.Height || height < 1)
        {
            error?.Error(TextureLogTag, LogError.Invalid, height, "initial sizeY"u8);

            return false;
        }

        return true;
    }

    private static VidPixelFormat ChooseFormat(VidPixelFormat format, TextureFlags flags, TextureFlags stored)
    {
        if (format == VidPixelFormat.P8 && stored.HasFlag(TextureFlags.AlphaPalette) && !_isAlphaPaletteSupported)
        {
            format = VidPixelFormat.A8R8G8B8;
        }

        if (!flags.HasFlag(TextureFlags.HasAlpha))
            return ReplacePalette(format);

        if (format == VidPixelFormat.P8)
            return stored.HasFlag(TextureFlags.AlphaPalette) ? ReplacePalette(format) : VidPixelFormat.A1R5G5B5;

        if (format == VidPixelFormat.R5G6B5)
            return VidPixelFormat.A1R5G5B5;

        if (format == VidPixelFormat.R8G8B8)
            return VidPixelFormat.A8R8G8B8;

        return ReplacePalette(format);
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
