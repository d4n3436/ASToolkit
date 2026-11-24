using System.Drawing;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a picture loaded from up to three image files, for the colors and the optional alpha and depth, and read frame by frame.
/// </summary>
public partial class Picture : IDisposable
{
    private static readonly Size LightSize = new(2, 2);

    /// <summary>
    /// Initializes a new instance of the <see cref="Picture"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder the picture files are in.</param>
    public Picture(string gameFolder)
        : this(gameFolder, null)
    {
    }

    [EngineRoutine(0x004125b0)]
    internal Picture(string gameFolder, GameLogger? logger)
    {
        Colors = new PictureChannel(gameFolder, logger);
        Alpha = new PictureChannel(gameFolder, logger);
        Depth = new PictureChannel(gameFolder, logger);
    }

    /// <summary>
    /// Gets the channel with the colors.
    /// </summary>
    public PictureChannel Colors { get; }

    /// <summary>
    /// Gets the channel with the alpha values.
    /// </summary>
    public PictureChannel Alpha { get; }

    /// <summary>
    /// Gets the channel with the depth values.
    /// </summary>
    public PictureChannel Depth { get; }

    /// <summary>
    /// Gets or sets which images the picture has and how its sprite file is made.
    /// </summary>
    public PictureFlags Flags { get; protected set; }

    /// <summary>
    /// Releases the three channels.
    /// </summary>
    [EngineRoutine(0x004125f0)]
    public virtual void Dispose()
    {
        Depth.Dispose();
        Alpha.Dispose();
        Colors.Dispose();
    }

    /// <summary>
    /// Loads the image files of the picture.
    /// </summary>
    /// <param name="colors">The path of the image with the colors, or an empty string.</param>
    /// <param name="alpha">The path of the image with the alpha values, or an empty string.</param>
    /// <param name="depth">The path of the image with the depth values, or an empty string.</param>
    /// <returns><see langword="true"/> if the colors or the alpha values were loaded; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00428ce0)]
    public virtual bool Load(string colors, string alpha, string depth)
    {
        _paletteMap = [];
        if (alpha.Length != 0)
        {
            Flags |= PictureFlags.Alpha;
        }

        if (depth.Length != 0)
        {
            Flags |= PictureFlags.Depth;
        }

        if (colors.Length == 0)
        {
            colors = alpha;
            alpha = string.Empty;
        }
        else
        {
            Flags |= PictureFlags.Color;
        }

        bool isColorsLoaded = Colors.Load(colors);
        bool isAlphaLoaded = Alpha.Load(alpha);
        Depth.Load(depth);
        if (Colors.Image.Size == LightSize)
        {
            Flags = PictureFlags.Light;
        }

        return isColorsLoaded || isAlphaLoaded;
    }

    /// <summary>
    /// Goes to the next frame of every image.
    /// </summary>
    [EngineRoutine(0x00428fa0)]
    public virtual void Advance()
    {
        Colors.Advance();
        Alpha.Advance();
        Depth.Advance();
    }

    /// <summary>
    /// Goes back to the first frame of every image.
    /// </summary>
    [EngineRoutine(0x00428f80)]
    public virtual void Rewind()
    {
        Colors.Rewind();
        Alpha.Rewind();
        Depth.Rewind();
    }

    /// <summary>
    /// Releases the pixels of every image.
    /// </summary>
    [EngineRoutine(0x00428e90)]
    public virtual void Reset()
    {
        Colors.Image.Reset();
        Alpha.Image.Reset();
        Depth.Image.Reset();
    }
}
