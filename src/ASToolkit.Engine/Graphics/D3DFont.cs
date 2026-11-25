using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a font with its 256 glyphs rendered into a texture.
/// </summary>
public sealed class D3DFont
{
    private const int GlyphCount = 256;
    private const uint TallLimit = 0x28;
    private const uint ShortLimit = 0x14;
    private const int SmallTextureSize = 0x100;
    private const int MediumTextureSize = 0x200;
    private const int LargeTextureSize = 0x400;
    private const int NewLine = 10;
    private const int Space = 0x20;
    private const int AlphaShift = 4;
    private const int NibbleMask = 0xf;
    private const int AlphaBits = 12;
    private const int ColorBits = 0xfff;
    private const int MinimumAlpha = 8;
    private const float PixelCenter = 0.5f;
    private const int Failure = unchecked((int)0x80004005);

    private readonly GlyphBounds[] _glyphs = new GlyphBounds[GlyphCount];
    private ushort[] _texels = [];
    private QuadBlend _blend;
    private int _minimumAlpha;

    /// <summary>
    /// Initializes a new instance of the <see cref="D3DFont"/> class without a texture.
    /// </summary>
    /// <param name="name">The name of the font face.</param>
    /// <param name="glyphSize">The average width and the height of the glyphs, in points.</param>
    /// <param name="style">How the glyphs are rendered and drawn.</param>
    [EngineRoutine(0x00434940)]
    public D3DFont(string name, Size glyphSize, D3DFontStyle style)
    {
        Name = name;
        GlyphSize = glyphSize;
        Style = style;
    }

    /// <summary>
    /// Gets the name of the font face.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the average width and the height of the glyphs, in points.
    /// </summary>
    public Size GlyphSize { get; }

    /// <summary>
    /// Gets how the glyphs are rendered and drawn.
    /// </summary>
    public D3DFontStyle Style { get; }

    /// <summary>
    /// Gets the size of the texture of glyphs, in texels, or zero before the texture is created.
    /// </summary>
    public Size TextureSize { get; private set; }

    /// <summary>
    /// Gets the ratio of the created texture size to the requested size.
    /// </summary>
    public float Scale { get; private set; }

    /// <summary>
    /// Renders the 256 glyphs into a texture.
    /// </summary>
    /// <param name="maxTextureSize">The largest texture width and height the device accepts.</param>
    /// <returns>Zero if the texture was created; otherwise, a nonzero error code.</returns>
    [EngineRoutine(0x004349c0)]
    public int InitDeviceObjects(int maxTextureSize)
    {
        Scale = 1f;
        int size = (uint)GlyphSize.Height > TallLimit ? LargeTextureSize : (uint)GlyphSize.Height > ShortLimit ? MediumTextureSize : SmallTextureSize;
        if (maxTextureSize < size)
        {
            Scale = (float)maxTextureSize / size;
            size = maxTextureSize;
        }

        TextureSize = new Size(size, size);
        ushort[] texels = new ushort[size * size];
        uint[] bitmapBits = new uint[size * size];
        // TODO: Port the GDI glyph rendering: CreateFontA with the height -MulDiv(GlyphSize.Height, (int)(LOGPIXELSY * Scale), 72), the width -MulDiv(GlyphSize.Width, (int)(LOGPIXELSX * Scale), 72),
        // weight 700 for Bold (else 400), italic for Italic, DEFAULT_CHARSET, ANTIALIASED_QUALITY and FIXED_PITCH for FixedPitch (else VARIABLE_PITCH); GetTextExtentPoint32A into extent
        // for each glyph; white ExtTextOutA ETO_OPAQUE text on black into bitmapBits
        int x = 0;
        int y = 0;
        for (int glyph = 0; glyph < GlyphCount; glyph++)
        {
            var extent = Size.Empty;
            if (extent.Width + x + 1 > size)
            {
                y += extent.Height + 1;
                x = 0;
            }

            _glyphs[glyph] = new GlyphBounds(
                new Vector2((float)x / size, (float)y / size),
                new Vector2((float)(x + extent.Width) / size, (float)(y + extent.Height) / size));
            x += extent.Width + 1;
        }

        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                int alpha = (int)(bitmapBits[row * size + column] >> AlphaShift) & NibbleMask;
                texels[row * size + column] = alpha == 0 ? (ushort)0 : (ushort)(alpha << AlphaBits | ColorBits);
            }
        }

        _texels = texels;
        return 0;
    }

    /// <summary>
    /// Sets the alpha blending and alpha test states that drawing text uses.
    /// </summary>
    [EngineRoutine(0x00434d90)]
    public void CreateStateBlocks()
    {
        _blend = QuadBlend.Alpha;
        _minimumAlpha = MinimumAlpha;
    }

    /// <summary>
    /// Draws text to the screen, one quad for each glyph.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="position">The position of the first glyph of the first line, in screen pixels.</param>
    /// <param name="color">The color that the glyphs are multiplied with, packed as 0xAARRGGBB.</param>
    /// <param name="text">The bytes of the text, up to the first zero byte.</param>
    /// <param name="flags">How the text is drawn.</param>
    /// <returns>Zero if the text was drawn; otherwise, a nonzero error code.</returns>
    [EngineRoutine(0x004350b0)]
    public int Draw(Screen screen, Vector2 position, uint color, ReadOnlySpan<byte> text, TextDrawFlags flags = TextDrawFlags.None)
    {
        if (_texels.Length == 0)
            return Failure;

        bool wasLinearFiltered = screen.IsLinearFiltered;
        screen.IsLinearFiltered = flags.HasFlag(TextDrawFlags.Filtered);
        var texture = new Texture(_texels, TextureSize, VidPixelFormat.A4R4G4B4);
        var gamma = new Gamma(~color, 0);
        float startX = position.X;
        for (int i = 0; i < text.Length && text[i] != 0; i++)
        {
            int glyph = text[i];
            if (glyph == NewLine)
            {
                position.X = startX;
                position.Y = (_glyphs[0].Max.Y - _glyphs[0].Min.Y) * TextureSize.Height + position.Y;
            }

            var bounds = _glyphs[glyph];
            float glyphWidth = (bounds.Max.X - bounds.Min.X) * TextureSize.Width / Scale;
            float glyphHeight = (bounds.Max.Y - bounds.Min.Y) * TextureSize.Height / Scale;
            if (glyph != Space)
            {
                float left = position.X - PixelCenter;
                float bottom = glyphHeight + position.Y - PixelCenter;
                float top = position.Y - PixelCenter;
                float right = glyphWidth + position.X - PixelCenter;
                var destination = Rectangle.FromLTRB((int)MathF.Ceiling(left), (int)MathF.Ceiling(top), (int)MathF.Ceiling(right), (int)MathF.Ceiling(bottom));
                var source = Rectangle.FromLTRB(
                    (int)MathF.Round(bounds.Min.X * TextureSize.Width),
                    (int)MathF.Round(bounds.Min.Y * TextureSize.Height),
                    (int)MathF.Round(bounds.Max.X * TextureSize.Width),
                    (int)MathF.Round(bounds.Max.Y * TextureSize.Height));
                texture.BlitQuad(screen, destination, source, gamma, _blend, false, true, _minimumAlpha);
            }

            position.X = glyphWidth + position.X;
        }

        screen.IsLinearFiltered = wasLinearFiltered;
        return 0;
    }
}
