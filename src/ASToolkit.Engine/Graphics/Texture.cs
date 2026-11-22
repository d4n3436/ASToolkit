using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a texture whose quads are drawn into the back buffer in software.
/// </summary>
public readonly ref partial struct Texture
{
    private const int RedShift = 16;
    private const float TexelCenter = 0.5f;
    private const int GreenShift = 8;
    private const int ChannelMax = byte.MaxValue;
    private const int NibbleScale = 17;
    private const int AlphaShift = 24;
    private const uint OpaqueBlack = 0xff000000;
    private const int NibbleShift = 4;

    private readonly Span<ushort> _texels;
    private readonly ReadOnlySpan<uint> _palette;

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture"/> structure over the texels of a texture and the palette its P8 texels index.
    /// </summary>
    /// <param name="texels">The texels of the texture.</param>
    /// <param name="size">The size of the texture, in texels.</param>
    /// <param name="format">The layout of the texels.</param>
    /// <param name="palette">The colors of a P8 texture, packed as 0xAARRGGBB.</param>
    public Texture(Span<ushort> texels, Size size, VidPixelFormat format, ReadOnlySpan<uint> palette = default)
    {
        _texels = texels;
        _palette = palette;
        Size = size;
        Format = format;
    }

    /// <summary>
    /// Gets the size of the texture, in texels.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets the layout of the texels.
    /// </summary>
    public VidPixelFormat Format { get; }

    /// <summary>
    /// Returns the area of the texture that a lock covers.
    /// </summary>
    /// <param name="origin">The first texel of the area, or <see langword="null"/> for the whole texture.</param>
    /// <param name="pitch">The number of bytes from the start of one row to the next.</param>
    /// <returns>The texels from the first texel of the area to the end of the texture.</returns>
    [EngineRoutine(0x00403a40)]
    public Span<ushort> Lock(Point? origin, out int pitch)
    {
        pitch = Size.Width * sizeof(ushort);
        return origin.HasValue ? _texels[Math.Clamp(origin.Value.Y * Size.Width + origin.Value.X, 0, _texels.Length)..] : _texels;
    }

    /// <summary>
    /// Draws a rectangle of the texture to the screen as a quad, with a gamma applied.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="destination">The area of the screen.</param>
    /// <param name="source">The area of the texture.</param>
    /// <param name="gamma">The gamma that darkens the texels and then brightens them.</param>
    /// <param name="blend">How the quad combines with the colors underneath.</param>
    /// <param name="isDoubled">Whether the texels are doubled after the gamma and before they are blended.</param>
    [EngineRoutine(0x00403dd0)]
    public void DrawQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isDoubled = false)
    {
        screen.IsLinearFiltered = destination.Size != source.Size;
        BlitQuad(screen, destination, source, gamma, blend, isDoubled, false, 0);
    }

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
    [EngineRoutine(0x00403ae0)]
    public void DrawFlatQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isAlphaModulated = false, int minimumAlpha = 0)
    {
        screen.IsLinearFiltered = destination.Size != source.Size;
        BlitQuad(screen, destination, source, gamma, blend, false, isAlphaModulated, minimumAlpha);
    }

    /// <summary>
    /// Copies an area of a surface into the texture and converts the colors to the layout of the texture.
    /// </summary>
    /// <param name="surface">The surface to copy from.</param>
    /// <param name="source">The area of the surface.</param>
    /// <param name="destination">The first texel to write.</param>
    /// <returns>Zero if the area was copied; otherwise, a nonzero error code.</returns>
    [EngineRoutine(0x00403970)]
    public int CopyFromSurface(Screen surface, Rectangle source, Point destination) => LoadSurfaceFromSurface(surface, source, destination);

    /// <summary>
    /// Converts the texels of a sprite file to the layout of textures that keep the colors in 16 bits.
    /// </summary>
    /// <param name="source">The texels as the sprite file stores them.</param>
    /// <param name="size">The size of the texture, in texels.</param>
    /// <param name="format">The layout of the stored texels.</param>
    /// <param name="palette">The palette of the texels as 256 entries of blue, green, red and alpha bytes.</param>
    /// <param name="result">The layout of the returned texels: R5G6B5, or A4R4G4B4 when the stored texels have an alpha channel.</param>
    /// <returns>The texels row by row.</returns>
    [EngineRoutine(0x004032e0)]
    public static ushort[] Create(ReadOnlySpan<byte> source, Size size, VidPixelFormat format, ReadOnlySpan<byte> palette, out VidPixelFormat result)
    {
        int count = Math.Max(size.Width * size.Height, 0);
        ushort[] texels = new ushort[count];
        result = format == VidPixelFormat.A4R4G4B4 ? VidPixelFormat.A4R4G4B4 : VidPixelFormat.R5G6B5;
        if (format is VidPixelFormat.R5G6B5 or VidPixelFormat.A4R4G4B4)
        {
            MemoryMarshal.Cast<byte, ushort>(source[..Math.Min(source.Length / 2 * 2, count * 2)]).CopyTo(texels);
            return texels;
        }

        uint[] colors = new uint[count];
        if (format is VidPixelFormat.Dxt1 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5)
        {
            DecodeCompressed(source, size, format, colors);
        }
        else
        {
            int stride = GetStride(format);
            for (int i = 0; stride != 0 && (i + 1) * stride <= source.Length && i < count; i++)
            {
                colors[i] = ReadColor(source.Slice(i * stride, stride), format, palette);
            }
        }

        bool hasAlpha = format is VidPixelFormat.A1R5G5B5 or VidPixelFormat.A8R8G8B8 or VidPixelFormat.A8 or VidPixelFormat.Dxt3 or VidPixelFormat.Dxt5 ||
            format == VidPixelFormat.P8 && HasAlpha(palette);
        result = hasAlpha ? VidPixelFormat.A4R4G4B4 : VidPixelFormat.R5G6B5;
        for (int i = 0; i < count; i++)
        {
            uint color = colors[i];
            texels[i] = hasAlpha
                ? (ushort)((color >> (AlphaShift + NibbleShift)) << 12 | (color >> (RedShift + NibbleShift) & 0xf) << 8 | (color >> (GreenShift + NibbleShift) & 0xf) << 4 | color >> NibbleShift & 0xf)
                : HighColorFormat.Rgb565.Pack(color);
        }

        return texels;
    }

    /// <summary>
    /// Draws a list of triangles to the screen, textured, with the transform of the device and without a depth test.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="vertices">The vertices.</param>
    /// <param name="indexes">The vertex indexes, three for each triangle.</param>
    /// <param name="transform">The world, view and projection transforms of the device multiplied in this order.</param>
    /// <param name="blend">How the triangles combine with the colors underneath.</param>
    /// <param name="factor">The texture factor, a color multiplied with the texels.</param>
    /// <param name="alphaReference">The alpha that a texel must exceed to be drawn.</param>
    public void DrawTriangles(Screen screen, ReadOnlySpan<HardwareLayerVertex> vertices, ReadOnlySpan<ushort> indexes, in Matrix4x4 transform, QuadBlend blend, uint factor, int alphaReference)
    {
        var clip = screen.Clip;
        var projected = vertices.Length <= 256 ? stackalloc Vector3[vertices.Length] : new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var position = Vector4.Transform(new Vector4(vertices[i].Position, 1f), transform);
            projected[i] = new Vector3(
                clip.Left + (position.X / position.W + 1f) * clip.Width * 0.5f,
                clip.Top + (1f - position.Y / position.W) * clip.Height * 0.5f,
                position.Z / position.W);
        }

        for (int i = 0; i + 2 < indexes.Length; i += 3)
        {
            if (indexes[i] >= vertices.Length || indexes[i + 1] >= vertices.Length || indexes[i + 2] >= vertices.Length)
                continue;

            RasterizeTriangle(screen, vertices, projected, indexes.Slice(i, 3), blend, factor, alphaReference);
        }
    }

    private static int GetStride(VidPixelFormat format) => format switch
    {
        VidPixelFormat.R8G8B8 => 3,
        VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => 4,
        VidPixelFormat.X1R5G5B5 or VidPixelFormat.A1R5G5B5 => 2,
        VidPixelFormat.P8 or VidPixelFormat.L8 or VidPixelFormat.A8 => 1,
        _ => 0
    };

    private static uint ReadColor(ReadOnlySpan<byte> texel, VidPixelFormat format, ReadOnlySpan<byte> palette)
    {
        switch (format)
        {
            case VidPixelFormat.R8G8B8:
                return OpaqueBlack | MemoryMarshal.AsRef<Pixel24>(texel).Value;
            case VidPixelFormat.A8R8G8B8:
                return MemoryMarshal.Read<uint>(texel);
            case VidPixelFormat.X8R8G8B8:
                return OpaqueBlack | MemoryMarshal.Read<uint>(texel);
            case VidPixelFormat.X1R5G5B5:
            case VidPixelFormat.A1R5G5B5:
            {
                uint value = MemoryMarshal.Read<ushort>(texel);
                uint red = value >> 10 & 0x1f;
                uint green = value >> 5 & 0x1f;
                uint blue = value & 0x1f;
                uint alpha = format == VidPixelFormat.A1R5G5B5 && (value & 0x8000) == 0 ? 0u : 0xffu;
                return alpha << AlphaShift | (red << 3 | red >> 2) << RedShift | (green << 3 | green >> 2) << GreenShift | blue << 3 | blue >> 2;
            }

            case VidPixelFormat.P8:
                return texel[0] < MemoryMarshal.Cast<byte, uint>(palette).Length ? MemoryMarshal.Cast<byte, uint>(palette)[texel[0]] : OpaqueBlack;
            case VidPixelFormat.L8:
                return 0xff000000 | (uint)texel[0] * 0x010101;
            default:
                return (uint)texel[0] << AlphaShift | 0xffffff;
        }
    }

    private static bool HasAlpha(ReadOnlySpan<byte> palette)
    {
        for (int i = 3; i < palette.Length; i += 4)
        {
            if (palette[i] != ChannelMax)
                return true;
        }

        return false;
    }

    private void RasterizeTriangle(Screen screen, ReadOnlySpan<HardwareLayerVertex> vertices, ReadOnlySpan<Vector3> projected, ReadOnlySpan<ushort> corners, QuadBlend blend, uint factor, int alphaReference)
    {
        var a = projected[corners[0]];
        var b = projected[corners[1]];
        var c = projected[corners[2]];
        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (area == 0f || float.IsNaN(area))
            return;

        var clip = Rectangle.Intersect(screen.Clip, new Rectangle(Point.Empty, screen.Size));
        int left = Math.Max(clip.Left, (int)MathF.Ceiling(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int right = Math.Min(clip.Right - 1, (int)MathF.Floor(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int top = Math.Max(clip.Top, (int)MathF.Ceiling(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int bottom = Math.Min(clip.Bottom - 1, (int)MathF.Floor(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        var writer = new ScreenWriter(screen);
        var uvA = vertices[corners[0]].TextureCoordinate;
        var uvB = vertices[corners[1]].TextureCoordinate;
        var uvC = vertices[corners[2]].TextureCoordinate;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                float weightA = ((b.X - x) * (c.Y - y) - (b.Y - y) * (c.X - x)) / area;
                float weightB = ((c.X - x) * (a.Y - y) - (c.Y - y) * (a.X - x)) / area;
                float weightC = 1f - weightA - weightB;
                if (weightA < 0f || weightB < 0f || weightC < 0f)
                    continue;

                float depth = a.Z * weightA + b.Z * weightB + c.Z * weightC;
                if (depth < 0f || depth > 1f)
                    continue;

                var uv = uvA * weightA + uvB * weightB + uvC * weightC;
                uint color = screen.IsLinearFiltered
                    ? Sample(new PointF(uv.X * Size.Width - TexelCenter, uv.Y * Size.Height - TexelCenter), true)
                    : Fetch(new Point(WrapTexel(uv.X * Size.Width, Size.Width), WrapTexel(uv.Y * Size.Height, Size.Height)));
                int alpha = Modulate((int)(color >> AlphaShift), (int)(factor >> AlphaShift));
                if (alpha <= alphaReference)
                    continue;

                int red = Modulate((int)(color >> RedShift & ChannelMax), (int)(factor >> RedShift & ChannelMax));
                int green = Modulate((int)(color >> GreenShift & ChannelMax), (int)(factor >> GreenShift & ChannelMax));
                int blue = Modulate((int)(color & ChannelMax), (int)(factor & ChannelMax));
                writer.Blend(y * screen.Pitch + x, (uint)alpha << AlphaShift | (uint)red << RedShift | (uint)green << GreenShift | (uint)blue, blend.Source, blend.Destination);
            }
        }
    }

    private Point Wrap(Point texel) => new(WrapTexel(texel.X, Size.Width), WrapTexel(texel.Y, Size.Height));

    private static int WrapTexel(float coordinate, int size)
    {
        int texel = (int)MathF.Floor(coordinate) % size;
        return texel < 0 ? texel + size : texel;
    }

    private static int Modulate(int texel, int factor) => (texel * factor + ChannelMax / 2) / ChannelMax;

    internal void BlitQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isDoubled, bool isAlphaModulated, int minimumAlpha)
    {
        if (destination.Width <= 0 || destination.Height <= 0)
            return;

        var area = Rectangle.Intersect(Rectangle.Intersect(destination, screen.Clip), new Rectangle(Point.Empty, screen.Size));
        if (area.IsEmpty)
            return;

        bool hasGamma = gamma.Negative != 0 || gamma.Positive != 0;
        var table = hasGamma ? GammaTable.Get(gamma) : null;
        var reds = table is null ? default : table.Reds;
        var greens = table is null ? default : table.Greens;
        var blues = table is null ? default : table.Blues;

        int alphaScale = ChannelMax - gamma.GetNegative(AlphaShift);
        bool isFiltered = screen.IsLinearFiltered;
        var writer = new ScreenWriter(screen);
        int lastColumn = Size.Width - 1;
        int lastRow = Size.Height - 1;
        if (!isFiltered && blend == QuadBlend.None && !isAlphaModulated && minimumAlpha <= 0 && screen.IsTrueColor)
        {
            var pixels = screen.TrueColorPixels;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int row = y * screen.Pitch;
                int sourceRow = Math.Clamp(source.Top + y - destination.Top, 0, lastRow) * Size.Width;
                for (int x = area.Left; x < area.Right; x++)
                {
                    uint texel = Decode(_texels[sourceRow + Math.Clamp(source.Left + x - destination.Left, 0, lastColumn)]);
                    int red = (int)(texel >> RedShift & ChannelMax);
                    int green = (int)(texel >> GreenShift & ChannelMax);
                    int blue = (int)(texel & ChannelMax);
                    if (hasGamma)
                    {
                        red = reds[red];
                        green = greens[green];
                        blue = blues[blue];
                    }

                    if (isDoubled)
                    {
                        red = Math.Min(ChannelMax, red * 2);
                        green = Math.Min(ChannelMax, green * 2);
                        blue = Math.Min(ChannelMax, blue * 2);
                    }

                    pixels[row + x] = OpaqueBlack | (uint)red << RedShift | (uint)green << GreenShift | (uint)blue;
                }
            }

            return;
        }

        bool needsAlpha = isAlphaModulated || minimumAlpha > 0 || blend.Source is BlendFactor.SourceAlpha or BlendFactor.InverseSourceAlpha ||
            blend.Destination is BlendFactor.SourceAlpha or BlendFactor.InverseSourceAlpha;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            int row = y * screen.Pitch;
            int upperRow;
            int lowerRow = 0;
            float shareY = 0;
            int cachedCorner = int.MinValue;
            uint upperLeft = 0, upperRight = 0, lowerLeft = 0, lowerRight = 0;
            if (isFiltered)
            {
                float sourceY = source.Top + (y - destination.Top) * (float)source.Height / destination.Height;
                int cornerY = (int)MathF.Floor(sourceY);
                shareY = sourceY - cornerY;
                upperRow = Math.Clamp(cornerY, 0, lastRow) * Size.Width;
                lowerRow = Math.Clamp(cornerY + 1, 0, lastRow) * Size.Width;
            }
            else
            {
                upperRow = Math.Clamp(source.Top + y - destination.Top, 0, lastRow) * Size.Width;
            }

            for (int x = area.Left; x < area.Right; x++)
            {
                uint texel;
                if (!isFiltered)
                {
                    texel = Decode(_texels[upperRow + Math.Clamp(source.Left + x - destination.Left, 0, lastColumn)]);
                }
                else
                {
                    float sourceX = source.Left + (x - destination.Left) * (float)source.Width / destination.Width;
                    int cornerX = (int)MathF.Floor(sourceX);
                    if (cornerX != cachedCorner)
                    {
                        int left = Math.Clamp(cornerX, 0, lastColumn);
                        int right = Math.Clamp(cornerX + 1, 0, lastColumn);
                        upperLeft = Decode(_texels[upperRow + left]);
                        upperRight = Decode(_texels[upperRow + right]);
                        lowerLeft = Decode(_texels[lowerRow + left]);
                        lowerRight = Decode(_texels[lowerRow + right]);
                        cachedCorner = cornerX;
                    }

                    texel = needsAlpha
                        ? Interpolate(upperLeft, upperRight, lowerLeft, lowerRight, sourceX - cornerX, shareY)
                        : OpaqueBlack | InterpolateColor(upperLeft, upperRight, lowerLeft, lowerRight, sourceX - cornerX, shareY);
                }

                int alpha = (int)(texel >> AlphaShift);
                if (isAlphaModulated)
                {
                    alpha = (alpha * alphaScale + ChannelMax / 2) / ChannelMax;
                }

                if (alpha < minimumAlpha)
                    continue;

                int red = (int)(texel >> RedShift & ChannelMax);
                int green = (int)(texel >> GreenShift & ChannelMax);
                int blue = (int)(texel & ChannelMax);
                if (hasGamma)
                {
                    red = reds[red];
                    green = greens[green];
                    blue = blues[blue];
                }

                if (isDoubled)
                {
                    red = Math.Min(ChannelMax, red * 2);
                    green = Math.Min(ChannelMax, green * 2);
                    blue = Math.Min(ChannelMax, blue * 2);
                }

                writer.Blend(row + x, (uint)alpha << AlphaShift | (uint)red << RedShift | (uint)green << GreenShift | (uint)blue, blend.Source, blend.Destination);
            }
        }
    }

    private uint Fetch(Point texel) => Decode(_texels[Math.Clamp(texel.Y, 0, Size.Height - 1) * Size.Width + Math.Clamp(texel.X, 0, Size.Width - 1)]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint Decode(ushort value)
    {
        if (Format == VidPixelFormat.P8)
            return value < _palette.Length ? _palette[value] : OpaqueBlack;

        if (Format == VidPixelFormat.A4R4G4B4)
            return (uint)((value >> 12) * NibbleScale) << 24 | (uint)((value >> 8 & 0xf) * NibbleScale) << RedShift | (uint)((value >> 4 & 0xf) * NibbleScale) << GreenShift | (uint)((value & 0xf) * NibbleScale);

        uint red = (uint)(value >> 11);
        uint green = (uint)(value >> 5 & 0x3f);
        uint blue = (uint)(value & 0x1f);
        return 0xff000000u | (red << 3 | red >> 2) << RedShift | (green << 2 | green >> 4) << GreenShift | blue << 3 | blue >> 2;
    }

    private uint Sample(PointF point, bool isWrapped = false)
    {
        var corner = new Point((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
        float fx = point.X - corner.X;
        float fy = point.Y - corner.Y;
        uint a = Fetch(isWrapped ? Wrap(corner) : corner);
        uint b = Fetch(isWrapped ? Wrap(corner with { X = corner.X + 1 }) : corner with { X = corner.X + 1 });
        uint c = Fetch(isWrapped ? Wrap(corner with { Y = corner.Y + 1 }) : corner with { Y = corner.Y + 1 });
        uint d = Fetch(isWrapped ? Wrap(new Point(corner.X + 1, corner.Y + 1)) : new Point(corner.X + 1, corner.Y + 1));
        return Interpolate(a, b, c, d, fx, fy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint InterpolateColor(uint a, uint b, uint c, uint d, float fx, float fy)
    {
        uint result = 0;
        for (int shift = 0; shift < AlphaShift; shift += 8)
        {
            float upper = (a >> shift & ChannelMax) * (1 - fx) + (b >> shift & ChannelMax) * fx;
            float lower = (c >> shift & ChannelMax) * (1 - fx) + (d >> shift & ChannelMax) * fx;
            result |= (uint)(upper * (1 - fy) + lower * fy + 0.5f) << shift;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Interpolate(uint a, uint b, uint c, uint d, float fx, float fy)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            float upper = (a >> shift & ChannelMax) * (1 - fx) + (b >> shift & ChannelMax) * fx;
            float lower = (c >> shift & ChannelMax) * (1 - fx) + (d >> shift & ChannelMax) * fx;
            result |= (uint)(upper * (1 - fy) + lower * fy + 0.5f) << shift;
        }

        return result;
    }
}
