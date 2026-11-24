using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a texture that is drawn to the back buffer in software.
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
    private const float HalfStep = 0.5f;

    private readonly Span<ushort> _texels;
    private readonly Span<uint> _texels32;
    private readonly ReadOnlySpan<uint> _palette;

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture"/> structure.
    /// </summary>
    /// <param name="texels">The texels of the texture.</param>
    /// <param name="size">The size of the texture, in texels.</param>
    /// <param name="format">The layout of the texels.</param>
    /// <param name="palette">The colors of a P8 texture, packed as 0xAARRGGBB.</param>
    public Texture(Span<ushort> texels, Size size, VidPixelFormat format, ReadOnlySpan<uint> palette = default)
    {
        _texels = texels;
        _texels32 = Is32Bit(format) ? MemoryMarshal.Cast<ushort, uint>(texels) : default;
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

    private static Vector4 MaxChannels => new(ChannelMax);

    private static Vector4 Doubled => new(2, 2, 2, 1);

    /// <summary>
    /// Returns the texels of the texture from a given texel to its end.
    /// </summary>
    /// <param name="origin">The first texel of the area, or <see langword="null"/> for the whole texture.</param>
    /// <param name="pitch">The number of bytes from the start of one row to the next.</param>
    /// <returns>The texels from the first texel of the area to the end of the texture.</returns>
    [EngineRoutine(0x00403a40)]
    public Span<ushort> Lock(Point? origin, out int pitch)
    {
        pitch = Size.Width * sizeof(ushort);
        return origin.HasValue ? _texels[(origin.Value.Y * Size.Width + origin.Value.X)..] : _texels;
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
    /// <param name="topDepth">The depth of the vertices on the top edge of the quad.</param>
    /// <param name="bottomDepth">The depth of the vertices on the bottom edge of the quad.</param>
    /// <exception cref="NotSupportedException">The texture is DXT-compressed.</exception>
    [EngineRoutine(0x00403dd0)]
    public void DrawQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isDoubled = false, float topDepth = 0, float bottomDepth = 0)
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
    /// <exception cref="NotSupportedException">The texture is DXT-compressed.</exception>
    [EngineRoutine(0x00403ae0)]
    public void DrawFlatQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isAlphaModulated = false, int minimumAlpha = 0)
    {
        screen.IsLinearFiltered = destination.Size != source.Size;
        BlitQuad(screen, destination, source, gamma, blend, false, isAlphaModulated, minimumAlpha);
    }

    /// <summary>
    /// Draws a list of textured triangles to the screen with a transform and without a depth test.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="vertices">The vertices.</param>
    /// <param name="indexes">The vertex indexes, three for each triangle.</param>
    /// <param name="transform">The world, view and projection transforms, multiplied in this order.</param>
    /// <param name="blend">How the triangles combine with the colors underneath.</param>
    /// <param name="factor">The texture factor, a color multiplied with the texels.</param>
    /// <param name="alphaReference">The alpha that a texel must exceed to be drawn.</param>
    /// <exception cref="NotSupportedException">The texture is DXT-compressed.</exception>
    public void DrawTriangles(Screen screen, ReadOnlySpan<HardwareLayerVertex> vertices, ReadOnlySpan<ushort> indexes, in Matrix4x4 transform, QuadBlend blend, uint factor, int alphaReference)
    {
        if (IsDxt(Format))
            throw new NotSupportedException($"Could not draw a {Format} texture.");

        var viewport = screen.Viewport;
        var projected = vertices.Length <= 256 ? stackalloc Vector3[vertices.Length] : new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var position = Vector4.Transform(new Vector4(vertices[i].Position, 1f), transform);
            projected[i] = new Vector3(
                viewport.Left + (position.X / position.W + 1f) * viewport.Width * 0.5f,
                viewport.Top + (1f - position.Y / position.W) * viewport.Height * 0.5f,
                position.Z / position.W);
        }

        for (int i = 0; i + 2 < indexes.Length; i += 3)
        {
            if (indexes[i] >= vertices.Length || indexes[i + 1] >= vertices.Length || indexes[i + 2] >= vertices.Length)
                continue;

            RasterizeTriangle(screen, vertices, projected, indexes.Slice(i, 3), blend, factor, alphaReference);
        }
    }

    [EngineRoutine(0x00403970)]
    internal int CopyFromSurface(Screen surface, Rectangle source, Point destination, GameLogger? logger)
    {
        int result = surface.IsTrueColor
            ? LoadTexels(new Rectangle(destination, source.Size), MemoryMarshal.AsBytes(surface.TrueColorPixels), VidPixelFormat.X8R8G8B8, surface.Pitch * sizeof(uint), surface.Size, source, DefaultFilter, 0)
            : LoadTexels(new Rectangle(destination, source.Size), MemoryMarshal.AsBytes(surface.HighColorPixels), surface.Format.IsRgb555 ? VidPixelFormat.X1R5G5B5 : VidPixelFormat.R5G6B5, surface.Pitch * sizeof(ushort), surface.Size, source, DefaultFilter, 0);
        if (result != 0)
        {
            logger?.Error(TextureLogTag, LogError.Copy, result);
        }

        return result;
    }

    internal void BlitQuad(Screen screen, Rectangle destination, Rectangle source, Gamma gamma, QuadBlend blend, bool isDoubled, bool isAlphaModulated, int minimumAlpha)
    {
        if (IsDxt(Format))
            throw new NotSupportedException($"Could not draw a {Format} texture.");

        if (destination.Width <= 0 || destination.Height <= 0)
            return;

        var area = Rectangle.Intersect(Rectangle.Intersect(destination, screen.Viewport), new Rectangle(Point.Empty, screen.Size));
        if (area.IsEmpty)
            return;

        bool hasGamma = gamma.Negative != 0 || gamma.Positive != 0;
        var table = hasGamma ? GammaTable.Get(gamma) : null;
        var reds = table is null ? default : table.Reds;
        var greens = table is null ? default : table.Greens;
        var blues = table is null ? default : table.Blues;

        bool isFiltered = screen.IsLinearFiltered;
        var writer = new ScreenWriter(screen);
        int lastColumn = Size.Width - 1;
        int lastRow = Size.Height - 1;
        if (!isFiltered && blend == QuadBlend.None && !isAlphaModulated && !isDoubled && minimumAlpha <= 0 && screen.IsTrueColor)
        {
            var pixels = screen.TrueColorPixels;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int row = y * screen.Pitch;
                int sourceRow = Math.Clamp(source.Top + y - destination.Top, 0, lastRow) * Size.Width;
                for (int x = area.Left; x < area.Right; x++)
                {
                    uint texel = Load(sourceRow + Math.Clamp(source.Left + x - destination.Left, 0, lastColumn));
                    int red = (int)(texel >> RedShift & ChannelMax);
                    int green = (int)(texel >> GreenShift & ChannelMax);
                    int blue = (int)(texel & ChannelMax);
                    if (hasGamma)
                    {
                        red = reds[red];
                        green = greens[green];
                        blue = blues[blue];
                    }

                    pixels[row + x] = OpaqueBlack | (uint)red << RedShift | (uint)green << GreenShift | (uint)blue;
                }
            }

            return;
        }

        var scale = new Vector4(ChannelMax - gamma.GetNegative(RedShift), ChannelMax - gamma.GetNegative(GreenShift), ChannelMax - gamma.GetNegative(0),
            isAlphaModulated ? ChannelMax - gamma.GetNegative(AlphaShift) : ChannelMax) / ChannelMax;
        var offset = new Vector4(gamma.GetPositive(RedShift), gamma.GetPositive(GreenShift), gamma.GetPositive(0), 0);
        var doubling = isDoubled ? Doubled : Vector4.One;
        bool isAlphaNeeded = isAlphaModulated || minimumAlpha > 0 || blend.Source is BlendFactor.SourceAlpha or BlendFactor.InverseSourceAlpha ||
            blend.Destination is BlendFactor.SourceAlpha or BlendFactor.InverseSourceAlpha;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            int row = y * screen.Pitch;
            int upperRow;
            int lowerRow = 0;
            float weightY = 0;
            int cachedCorner = int.MinValue;
            uint upperLeft = 0, upperRight = 0, lowerLeft = 0, lowerRight = 0;
            if (isFiltered)
            {
                float sourceY = source.Top + (y - destination.Top) * (float)source.Height / destination.Height;
                int cornerY = (int)MathF.Floor(sourceY);
                weightY = sourceY - cornerY;
                upperRow = Math.Clamp(cornerY, 0, lastRow) * Size.Width;
                lowerRow = Math.Clamp(cornerY + 1, 0, lastRow) * Size.Width;
            }
            else
            {
                upperRow = Math.Clamp(source.Top + y - destination.Top, 0, lastRow) * Size.Width;
            }

            for (int x = area.Left; x < area.Right; x++)
            {
                Vector4 texel;
                if (!isFiltered)
                {
                    texel = ToChannels(Load(upperRow + Math.Clamp(source.Left + x - destination.Left, 0, lastColumn)));
                }
                else
                {
                    float sourceX = source.Left + (x - destination.Left) * (float)source.Width / destination.Width;
                    int cornerX = (int)MathF.Floor(sourceX);
                    if (cornerX != cachedCorner)
                    {
                        int left = Math.Clamp(cornerX, 0, lastColumn);
                        int right = Math.Clamp(cornerX + 1, 0, lastColumn);
                        upperLeft = Load(upperRow + left);
                        upperRight = Load(upperRow + right);
                        lowerLeft = Load(lowerRow + left);
                        lowerRight = Load(lowerRow + right);
                        cachedCorner = cornerX;
                    }

                    texel = Filter(upperLeft, upperRight, lowerLeft, lowerRight, sourceX - cornerX, weightY);
                    if (!isAlphaNeeded)
                    {
                        texel.W = ChannelMax;
                    }
                }

                uint color = ToColor(Vector4.Min(MaxChannels, Vector4.Min(MaxChannels, texel * scale + offset) * doubling));
                if ((int)(color >> AlphaShift) < minimumAlpha)
                    continue;

                writer.Blend(row + x, color, blend.Source, blend.Destination);
            }
        }
    }

    private static int WrapTexel(float coordinate, int size)
    {
        int texel = (int)MathF.Floor(coordinate) % size;
        return texel < 0 ? texel + size : texel;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 Filter(uint upperLeft, uint upperRight, uint lowerLeft, uint lowerRight, float weightX, float weightY)
    {
        var upper = ToChannels(upperLeft) * (1 - weightX) + ToChannels(upperRight) * weightX;
        var lower = ToChannels(lowerLeft) * (1 - weightX) + ToChannels(lowerRight) * weightX;
        return upper * (1 - weightY) + lower * weightY;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 ToChannels(uint color) => new(color >> RedShift & ChannelMax, color >> GreenShift & ChannelMax, color & ChannelMax, color >> AlphaShift);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ToColor(Vector4 channels)
    {
        var rounded = channels + new Vector4(HalfStep);
        return (uint)rounded.W << AlphaShift | (uint)rounded.X << RedShift | (uint)rounded.Y << GreenShift | (uint)rounded.Z;
    }

    private void RasterizeTriangle(Screen screen, ReadOnlySpan<HardwareLayerVertex> vertices, ReadOnlySpan<Vector3> projected, ReadOnlySpan<ushort> corners, QuadBlend blend, uint factor, int alphaReference)
    {
        var a = projected[corners[0]];
        var b = projected[corners[1]];
        var c = projected[corners[2]];
        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (area == 0f || float.IsNaN(area))
            return;

        var viewport = Rectangle.Intersect(screen.Viewport, new Rectangle(Point.Empty, screen.Size));
        int left = Math.Max(viewport.Left, (int)MathF.Ceiling(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int right = Math.Min(viewport.Right - 1, (int)MathF.Floor(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int top = Math.Max(viewport.Top, (int)MathF.Ceiling(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int bottom = Math.Min(viewport.Bottom - 1, (int)MathF.Floor(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        var writer = new ScreenWriter(screen);
        var uvA = vertices[corners[0]].TextureCoordinate;
        var uvB = vertices[corners[1]].TextureCoordinate;
        var uvC = vertices[corners[2]].TextureCoordinate;
        var modulation = ToChannels(factor) / ChannelMax;
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
                if (depth is < 0f or > 1f)
                    continue;

                var uv = uvA * weightA + uvB * weightB + uvC * weightC;
                var texel = screen.IsLinearFiltered
                    ? Sample(new PointF(uv.X * Size.Width - TexelCenter, uv.Y * Size.Height - TexelCenter), true)
                    : ToChannels(Fetch(new Point(WrapTexel(uv.X * Size.Width, Size.Width), WrapTexel(uv.Y * Size.Height, Size.Height))));
                uint color = ToColor(texel * modulation);
                if ((int)(color >> AlphaShift) <= alphaReference)
                    continue;

                writer.Blend(y * screen.Pitch + x, color, blend.Source, blend.Destination);
            }
        }
    }

    private Point Wrap(Point texel) => new(WrapTexel(texel.X, Size.Width), WrapTexel(texel.Y, Size.Height));

    private uint Fetch(Point texel) => Load(Math.Clamp(texel.Y, 0, Size.Height - 1) * Size.Width + Math.Clamp(texel.X, 0, Size.Width - 1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint Load(int index)
    {
        if (_texels32.Length != 0)
            return Format == VidPixelFormat.X8R8G8B8 ? OpaqueBlack | _texels32[index] : _texels32[index];

        return Decode(_texels[index]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint Decode(ushort value)
    {
        if (Format is VidPixelFormat.A1R5G5B5 or VidPixelFormat.X1R5G5B5)
        {
            uint red5 = (uint)(value >> 10 & 0x1f);
            uint green5 = (uint)(value >> 5 & 0x1f);
            uint blue5 = (uint)(value & 0x1f);
            uint alpha = Format == VidPixelFormat.A1R5G5B5 && (value & 0x8000) == 0 ? 0u : 0xffu;
            return alpha << AlphaShift | (red5 << 3 | red5 >> 2) << RedShift | (green5 << 3 | green5 >> 2) << GreenShift | blue5 << 3 | blue5 >> 2;
        }

        if (Format == VidPixelFormat.P8)
            return value < _palette.Length ? _palette[value] : OpaqueBlack;

        if (Format == VidPixelFormat.A4R4G4B4)
            return (uint)((value >> 12) * NibbleScale) << 24 | (uint)((value >> 8 & 0xf) * NibbleScale) << RedShift | (uint)((value >> 4 & 0xf) * NibbleScale) << GreenShift | (uint)((value & 0xf) * NibbleScale);

        uint red = (uint)(value >> 11);
        uint green = (uint)(value >> 5 & 0x3f);
        uint blue = (uint)(value & 0x1f);
        return 0xff000000u | (red << 3 | red >> 2) << RedShift | (green << 2 | green >> 4) << GreenShift | blue << 3 | blue >> 2;
    }

    private Vector4 Sample(PointF point, bool isWrapped)
    {
        var corner = new Point((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
        uint upperLeft = Fetch(isWrapped ? Wrap(corner) : corner);
        uint upperRight = Fetch(isWrapped ? Wrap(corner with { X = corner.X + 1 }) : corner with { X = corner.X + 1 });
        uint lowerLeft = Fetch(isWrapped ? Wrap(corner with { Y = corner.Y + 1 }) : corner with { Y = corner.Y + 1 });
        uint lowerRight = Fetch(isWrapped ? Wrap(new Point(corner.X + 1, corner.Y + 1)) : new Point(corner.X + 1, corner.Y + 1));
        return Filter(upperLeft, upperRight, lowerLeft, lowerRight, point.X - corner.X, point.Y - corner.Y);
    }
}
