using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a font whose 256 glyphs are rendered once into a texture, from which text is drawn as one quad for each glyph.
/// </summary>
public sealed class TextureFont
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
    /// Initializes a new instance of the <see cref="TextureFont"/> class without a texture.
    /// </summary>
    /// <param name="name">The name of the font face.</param>
    /// <param name="width">The average width of the glyphs, in points.</param>
    /// <param name="height">The height of the glyphs, in points.</param>
    /// <param name="style">How the glyphs are rendered and drawn.</param>
    [EngineRoutine(0x00434940)]
    public TextureFont(string name, int width, int height, TextureFontStyle style)
    {
        Name = name;
        Height = height;
        Width = width;
        Style = style;
    }

    /// <summary>
    /// Gets the name of the font face.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the height of the glyphs, in points.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the average width of the glyphs, in points.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets how the glyphs are rendered and drawn.
    /// </summary>
    public TextureFontStyle Style { get; }

    /// <summary>
    /// Gets the size of the texture of glyphs, in texels, which is zero before the texture is created.
    /// </summary>
    public Size TextureSize { get; private set; }

    /// <summary>
    /// Gets the ratio of the texture size that was created to the size the font asked for.
    /// </summary>
    public float Scale { get; private set; }

    /// <summary>
    /// Renders the 256 glyphs into a texture, row by row from the top left, and records where each one lies.
    /// </summary>
    /// <param name="maxTextureSize">The largest texture width and height the device accepts.</param>
    /// <returns>Zero if the texture was created; otherwise, a nonzero error code.</returns>
    [EngineRoutine(0x004349c0)]
    public int Create(int maxTextureSize)
    {
        Scale = 1f;
        int size = (uint)Height > TallLimit ? LargeTextureSize : (uint)Height > ShortLimit ? MediumTextureSize : SmallTextureSize;
        if (maxTextureSize < size)
        {
            Scale = (float)maxTextureSize / size;
            size = maxTextureSize;
        }

        TextureSize = new Size(size, size);
        ushort[] texels = new ushort[size * size];
        uint[] bitmapBits = new uint[size * size];
        // TODO: Port the GDI glyph rendering: CreateFontA with the height -MulDiv(Height, (int)(LOGPIXELSY * Scale), 72), the width -MulDiv(Width, (int)(LOGPIXELSX * Scale), 72),
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
    /// Sets the states that drawing text uses, which are alpha blending and an alpha test.
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
    /// <param name="x">The horizontal position of the first glyph, in screen pixels.</param>
    /// <param name="y">The vertical position of the first line, in screen pixels.</param>
    /// <param name="color">The color that the glyphs are multiplied with, packed as 0xAARRGGBB.</param>
    /// <param name="text">The bytes of the text, which ends at the first zero byte.</param>
    /// <param name="flags">How the text is drawn.</param>
    /// <returns>Zero if the text was drawn; otherwise, a nonzero error code.</returns>
    [EngineRoutine(0x004350b0)]
    public int Draw(Screen screen, float x, float y, uint color, ReadOnlySpan<byte> text, TextDrawFlags flags)
    {
        if (_texels.Length == 0)
            return Failure;

        bool wasLinearFiltered = screen.IsLinearFiltered;
        screen.IsLinearFiltered = flags.HasFlag(TextDrawFlags.Filtered);
        var texture = new Texture(_texels, TextureSize, VidPixelFormat.A4R4G4B4);
        var gamma = new Gamma(~color, 0);
        float startX = x;
        for (int i = 0; i < text.Length && text[i] != 0; i++)
        {
            int glyph = text[i];
            if (glyph == NewLine)
            {
                x = startX;
                y = (_glyphs[0].Max.Y - _glyphs[0].Min.Y) * TextureSize.Height + y;
            }

            var bounds = _glyphs[glyph];
            float glyphWidth = (bounds.Max.X - bounds.Min.X) * TextureSize.Width / Scale;
            float glyphHeight = (bounds.Max.Y - bounds.Min.Y) * TextureSize.Height / Scale;
            if (glyph != Space)
            {
                float left = x - PixelCenter;
                float bottom = glyphHeight + y - PixelCenter;
                float top = y - PixelCenter;
                float right = glyphWidth + x - PixelCenter;
                var destination = Rectangle.FromLTRB((int)MathF.Ceiling(left), (int)MathF.Ceiling(top), (int)MathF.Ceiling(right), (int)MathF.Ceiling(bottom));
                var source = Rectangle.FromLTRB(
                    (int)MathF.Round(bounds.Min.X * TextureSize.Width),
                    (int)MathF.Round(bounds.Min.Y * TextureSize.Height),
                    (int)MathF.Round(bounds.Max.X * TextureSize.Width),
                    (int)MathF.Round(bounds.Max.Y * TextureSize.Height));
                texture.BlitQuad(screen, destination, source, gamma, _blend, false, true, _minimumAlpha);
            }

            x = glyphWidth + x;
        }

        screen.IsLinearFiltered = wasLinearFiltered;
        return 0;
    }
}
