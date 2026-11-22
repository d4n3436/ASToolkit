using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

public readonly ref partial struct Texture
{
    private const int InvalidCall = -0x7789f794;
    private const float ByteScale = 0.003921569f;
    private const float FiveBitScale = 0.032258064f;
    private const float SixBitScale = 0.015873017f;
    private const int DitherSize = 4;
    private const int FiveBitMax = 31;
    private const int SixBitMax = 63;
    private const int NibbleMax = 15;

    private static ReadOnlySpan<float> DitherOffsets =>
    [
        0.96875f, 0.46875f, 0.84375f, 0.34375f,
        0.21875f, 0.71875f, 0.09375f, 0.59375f,
        0.78125f, 0.28125f, 0.90625f, 0.40625f,
        0.03125f, 0.53125f, 0.15625f, 0.65625f
    ];

    [EngineRoutine(0x004553cc)]
    private int LoadSurfaceFromSurface(Screen surface, Rectangle source, Point destination)
    {
        if (source.Left < 0 || surface.Size.Width < source.Right || source.Right < source.Left || source.Top < 0 || surface.Size.Height < source.Bottom || source.Bottom < source.Top)
            return InvalidCall;

        return LoadSurfaceFromMemory(surface, source, new Rectangle(destination, source.Size));
    }

    [EngineRoutine(0x00455196)]
    private int LoadSurfaceFromMemory(Screen surface, Rectangle source, Rectangle destination)
    {
        if (destination.Left < 0 || Size.Width < destination.Right || destination.Right < destination.Left || destination.Top < 0 || Size.Height < destination.Bottom || destination.Bottom < destination.Top)
            return InvalidCall;

        Blit(surface, source, destination);
        return 0;
    }

    [EngineRoutine(0x004586f9)]
    private void Blit(Screen surface, Rectangle source, Rectangle destination)
    {
        if (surface is { IsTrueColor: false, Format.IsRgb555: false } && Format == VidPixelFormat.R5G6B5)
        {
            CopyRows(surface, source, destination);
            return;
        }

        ConvertRows(surface, source, destination);
    }

    [EngineRoutine(0x0045707d)]
    private void CopyRows(Screen surface, Rectangle source, Rectangle destination)
    {
        for (int y = 0; y < destination.Height; y++)
        {
            surface.HighColorPixels.Slice((source.Top + y) * surface.Pitch + source.Left, destination.Width).CopyTo(_texels[((destination.Top + y) * Size.Width + destination.Left)..]);
        }
    }

    [EngineRoutine(0x004571af)]
    private void ConvertRows(Screen surface, Rectangle source, Rectangle destination)
    {
        Span<Vector4> colors = destination.Width <= 256 ? stackalloc Vector4[destination.Width] : new Vector4[destination.Width];
        for (int y = 0; y < destination.Height; y++)
        {
            int start = (source.Top + y) * surface.Pitch + source.Left;
            if (surface.IsTrueColor)
            {
                DecodeX8R8G8B8(surface.TrueColorPixels.Slice(start, colors.Length), colors);
            }
            else if (surface.Format.IsRgb555)
            {
                DecodeX1R5G5B5(surface.HighColorPixels.Slice(start, colors.Length), colors);
            }
            else
            {
                DecodeR5G6B5(surface.HighColorPixels.Slice(start, colors.Length), colors);
            }

            var row = _texels.Slice((destination.Top + y) * Size.Width + destination.Left, colors.Length);
            if (Format == VidPixelFormat.A4R4G4B4)
            {
                EncodeA4R4G4B4(colors, row, y);
            }
            else
            {
                EncodeR5G6B5(colors, row, y);
            }
        }
    }

    [EngineRoutine(0x0045a1ca)]
    private static void DecodeX8R8G8B8(ReadOnlySpan<uint> pixels, Span<Vector4> colors)
    {
        for (int x = 0; x < pixels.Length; x++)
        {
            colors[x] = new Vector4((byte)(pixels[x] >> 16) * ByteScale, (byte)(pixels[x] >> 8) * ByteScale, (byte)pixels[x] * ByteScale, 1f);
        }
    }

    [EngineRoutine(0x0045a262)]
    private static void DecodeR5G6B5(ReadOnlySpan<ushort> pixels, Span<Vector4> colors)
    {
        for (int x = 0; x < pixels.Length; x++)
        {
            colors[x] = new Vector4((pixels[x] >> 11) * FiveBitScale, (pixels[x] >> 5 & SixBitMax) * SixBitScale, (pixels[x] & FiveBitMax) * FiveBitScale, 1f);
        }
    }

    [EngineRoutine(0x0045a307)]
    private static void DecodeX1R5G5B5(ReadOnlySpan<ushort> pixels, Span<Vector4> colors)
    {
        for (int x = 0; x < pixels.Length; x++)
        {
            colors[x] = new Vector4((pixels[x] >> 10 & FiveBitMax) * FiveBitScale, (pixels[x] >> 5 & FiveBitMax) * FiveBitScale, (pixels[x] & FiveBitMax) * FiveBitScale, 1f);
        }
    }

    [EngineRoutine(0x004590ce)]
    private static void EncodeR5G6B5(ReadOnlySpan<Vector4> colors, Span<ushort> pixels, int y)
    {
        for (int x = 0; x < colors.Length; x++)
        {
            float offset = GetDitherOffset(x, y);
            pixels[x] = (ushort)((Quantize(colors[x].X, FiveBitMax, offset) << 6 | Quantize(colors[x].Y, SixBitMax, offset)) << 5 | Quantize(colors[x].Z, FiveBitMax, offset));
        }
    }

    [EngineRoutine(0x0045936f)]
    private static void EncodeA4R4G4B4(ReadOnlySpan<Vector4> colors, Span<ushort> pixels, int y)
    {
        for (int x = 0; x < colors.Length; x++)
        {
            float offset = GetDitherOffset(x, y);
            pixels[x] = (ushort)(((Quantize(colors[x].W, NibbleMax, offset) << 4 | Quantize(colors[x].X, NibbleMax, offset)) << 4 | Quantize(colors[x].Y, NibbleMax, offset)) << 4 | Quantize(colors[x].Z, NibbleMax, offset));
        }
    }

    private static float GetDitherOffset(int x, int y) => DitherOffsets[(y & (DitherSize - 1)) * DitherSize + (x & (DitherSize - 1))];

    private static int Quantize(float channel, int max, float offset) => (int)Chop(Chop((double)channel * max) + offset);

    private static float Chop(double value)
    {
        float rounded = (float)value;
        return rounded > value ? MathF.BitDecrement(rounded) : rounded;
    }
}
