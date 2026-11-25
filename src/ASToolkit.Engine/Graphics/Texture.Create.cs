using System.Drawing;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public readonly ref partial struct Texture
{
    private const uint OpaqueKey = 0xff000000;

    [EngineRoutine(0x004032e0)]
    internal static TextureData Create(ReadOnlySpan<byte> source, Size size, VidPixelFormat format, ReadOnlySpan<uint> palette, TextureFlags flags, MyError? error)
    {
        var textureSize = size;
        ushort[] texels = Allocate(ref textureSize, format, flags, error, out var stored, out var result);
        uint[] texturePalette = result == VidPixelFormat.P8 ? palette.ToArray() : [];
        int pitch = GetSourcePitch(format, size.Width);
        uint colorKey = stored.HasFlag(TextureFlags.AlphaPalette) ? 0 : OpaqueKey;
        var texture = new Texture(texels, textureSize, result, texturePalette);
        var area = new Rectangle(Point.Empty, size);
        texture.LoadTexels(area, source, format, pitch, size, area, 1, colorKey, palette);
        return new TextureData(texels, textureSize, result, texturePalette);
    }

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
}
