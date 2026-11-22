using ASToolkit.Engine.IO;
using System.Drawing;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a picture of a font, where an image of 16 by 16 characters is read one character at a time.
/// </summary>
public sealed class FontPicture : Picture
{
    private const int CharactersPerRow = 16;
    private const int CharacterCount = 256;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontPicture"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder the picture files are in.</param>
    public FontPicture(string gameFolder)
        : this(gameFolder, null)
    {
    }

    internal FontPicture(string gameFolder, GameLogger? logger)
        : base(gameFolder, logger)
    {
        Source = new Picture(gameFolder, logger);
    }

    /// <summary>
    /// Gets the picture of the image that holds all the characters.
    /// </summary>
    public Picture Source { get; }

    /// <summary>
    /// Releases the source picture and the three channels.
    /// </summary>
    [EngineRoutine(0x004128f0)]
    public override void Dispose()
    {
        Source.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Loads the image of the characters and sets the first character as the current frame.
    /// </summary>
    /// <param name="colors">The path of the image with the colors.</param>
    /// <param name="alpha">The path of the image with the alpha values, or an empty string.</param>
    /// <param name="depth">The path of the image with the depth values, or an empty string.</param>
    /// <returns><see langword="true"/> if the image was loaded; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0042cd10)]
    public override bool Load(string colors, string alpha, string depth)
    {
        Reset();
        if (!Source.Load(colors, alpha, depth))
            return false;
        var glyphs = Colors.Image;

        var characters = Source.Colors.Image;

        glyphs.Resize(new Size(characters.Size.Width / CharactersPerRow - 1, characters.Size.Height / CharactersPerRow - 1), characters.BytesPerPixel);
        glyphs.FrameCount = CharacterCount;
        Flags = PictureFlags.Color;
        if (characters.BytesPerPixel == 1)
        {
            characters.Palette.CopyTo(glyphs.Palette);
            Flags |= PictureFlags.Palette;
        }

        Rewind();
        return true;
    }

    /// <summary>
    /// Goes to the next character.
    /// </summary>
    [EngineRoutine(0x0042ce80)]
    public override void Advance()
    {
        base.Advance();
        var glyphs = Colors.Image;
        int column = glyphs.FrameIndex % CharactersPerRow;
        int row = glyphs.FrameIndex / CharactersPerRow;
        for (int y = 0; y < glyphs.Size.Height; y++)
        {
            for (int x = 0; x < glyphs.Size.Width; x++)
            {
                glyphs.SetPixel(new Point(x, y), Source.Colors.Image.GetPixel(new Point(x + (glyphs.Size.Width + 1) * column, (glyphs.Size.Height + 1) * row + y)));
            }
        }
    }

    /// <summary>
    /// Goes back to the first character.
    /// </summary>
    [EngineRoutine(0x0042cf10)]
    public override void Rewind()
    {
        base.Rewind();
        var glyphs = Colors.Image;
        for (int y = 0; y < glyphs.Size.Height; y++)
        {
            for (int x = 0; x < glyphs.Size.Width; x++)
            {
                glyphs.SetPixel(new Point(x, y), Source.Colors.Image.GetPixel(new Point(x, y)));
            }
        }
    }
}
