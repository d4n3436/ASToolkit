using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public readonly ref partial struct Texture
{
    private const uint DefaultFilter = 0xffffffff;
    private const uint DitherFilter = 0x80000;
    private const uint RgbMask = 0x00ffffff;
    private const ushort HighColorAlpha = 0x8000;

    private static uint KeyOpaqueBlack(uint texel, uint colorKey) => texel == colorKey ? 0 : texel;

    private int LoadTexels(Rectangle destination, ReadOnlySpan<byte> data, VidPixelFormat format, int pitch, Size sourceSize, Rectangle source, uint filter, uint colorKey)
    {
        if (source.Left < 0 || sourceSize.Width < source.Right || source.Right < source.Left || source.Top < 0 || sourceSize.Height < source.Bottom || source.Bottom < source.Top)
            return InvalidCall;

        if (destination.Left < 0 || Size.Width < destination.Right || destination.Right < destination.Left || destination.Top < 0 || Size.Height < destination.Bottom || destination.Bottom < destination.Top)
            return InvalidCall;

        if (_texels.IsEmpty)
            throw new NotSupportedException($"Could not load texels into a {Format} texture that wasn't allocated.");

        if (source.Size != destination.Size || colorKey is not (0 or OpaqueKey))
            throw new NotSupportedException($"Could not load a {format} area of {source.Size} into a {Format} area of {destination.Size} with color key 0x{colorKey:x8}.");

        if (format == Format && colorKey == 0)
        {
            CopyTexels(destination, data, format, pitch, source);
            return 0;
        }

        bool dither = filter == DefaultFilter || (filter & DitherFilter) != 0;
        if (dither)
            throw new NotSupportedException($"Converting {format} texels to {Format} with dithering is not supported.");

        for (int y = 0; y < destination.Height; y++)
        {
            var row = data[((source.Top + y) * pitch)..];
            int start = (destination.Top + y) * Size.Width + destination.Left;
            for (int x = 0; x < destination.Width; x++)
            {
                int column = source.Left + x;
                int texel;
                switch (format, Format)
                {
                    case (VidPixelFormat.R8G8B8, VidPixelFormat.A8R8G8B8):
                        _texels32[start + x] = KeyOpaqueBlack(OpaqueKey | (uint)(row[column * 3 + 2] << 16 | row[column * 3 + 1] << 8 | row[column * 3]), colorKey);
                        break;
                    case (VidPixelFormat.X8R8G8B8, VidPixelFormat.X8R8G8B8):
                        _texels32[start + x] = BinaryPrimitives.ReadUInt32LittleEndian(row[(column * 4)..]) & RgbMask;
                        break;
                    case (VidPixelFormat.X8R8G8B8, VidPixelFormat.A8R8G8B8):
                        _texels32[start + x] = KeyOpaqueBlack(OpaqueKey | BinaryPrimitives.ReadUInt32LittleEndian(row[(column * 4)..]) & RgbMask, colorKey);
                        break;
                    case (VidPixelFormat.R5G6B5, VidPixelFormat.A1R5G5B5):
                        texel = BinaryPrimitives.ReadUInt16LittleEndian(row[(column * 2)..]);
                        _texels[start + x] = texel == 0 && colorKey != 0 ? (ushort)0 : (ushort)(HighColorAlpha | (texel >> 11) << 10 | (texel >> 6 & 0x1f) << 5 | texel & 0x1f);
                        break;
                    case (VidPixelFormat.X1R5G5B5, VidPixelFormat.X1R5G5B5):
                        _texels[start + x] = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(row[(column * 2)..]) & ~HighColorAlpha);
                        break;
                    case (VidPixelFormat.X1R5G5B5, VidPixelFormat.A1R5G5B5):
                        texel = BinaryPrimitives.ReadUInt16LittleEndian(row[(column * 2)..]) & ~HighColorAlpha;
                        _texels[start + x] = texel == 0 && colorKey != 0 ? (ushort)0 : (ushort)(HighColorAlpha | texel);
                        break;
                    default:
                        throw new NotSupportedException($"Could not convert {format} texels to {Format}.");
                }
            }
        }

        return 0;
    }

    private void CopyTexels(Rectangle destination, ReadOnlySpan<byte> data, VidPixelFormat format, int pitch, Rectangle source)
    {
        int bytes = format switch
        {
            VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 4,
            VidPixelFormat.R5G6B5 or VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 or VidPixelFormat.A4R4G4B4 => 2,
            _ => throw new NotSupportedException($"Could not copy {format} texels.")
        };
        for (int y = 0; y < destination.Height; y++)
        {
            var row = data.Slice((source.Top + y) * pitch + source.Left * bytes, destination.Width * bytes);
            row.CopyTo(MemoryMarshal.AsBytes(_texels)[(((destination.Top + y) * Size.Width + destination.Left) * bytes)..]);
        }
    }
}
