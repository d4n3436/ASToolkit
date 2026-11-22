using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a definition whose object file names a font, which the engine uses to draw text from a texture of glyphs rendered at run time.
/// </summary>
public sealed class FontSpriteDefinition : SpriteDefinition
{
    private const int GlyphCount = 256;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontSpriteDefinition"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="maxTextureSize">The largest texture width and height the device accepts, which limits the texture of glyphs.</param>
    public FontSpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, int maxTextureSize)
        : base(id, gameObject, weapon, SpriteCatalog.DefaultSpriteSpeed)
    {
        MaxTextureSize = maxTextureSize;
    }

    /// <summary>
    /// Gets the largest texture width and height the device accepts.
    /// </summary>
    public int MaxTextureSize { get; }

    /// <summary>
    /// Gets the height and width of the glyphs of the font, in pixels.
    /// </summary>
    public Size GlyphSize { get; private set; }

    /// <summary>
    /// Gets how the glyphs are rendered and drawn.
    /// </summary>
    public TextureFontStyle Style { get; private set; }

    /// <summary>
    /// Gets the font that draws the text of the definition, which is created when the definition loads.
    /// </summary>
    public TextureFont? Font { get; private set; }

    [EngineRoutine(0x00415140)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, GameLogger? logger)
    {
        FileFrameCount = GlyphCount;
        FrameSize = new Size((short)(int)Object.Size.X, (short)(int)Object.Size.Y);
        FileType = VidType.Font;
        GlyphSize = new Size((int)Object.Size.X, (int)Object.Size.Y);
        Style = TextureFontStyle.FixedPitch;
        Font = new TextureFont(Path, GlyphSize.Width, GlyphSize.Height, Style);
        Font.Create(MaxTextureSize);
        Font.CreateStateBlocks();
    }

    /// <summary>
    /// Draws the text of a sprite with the glyphs of the font.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00415200)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        if (Font is null)
            return;

        var position = sprite.Position;

        var gamma = sprite.GetGamma();
        Font.Draw(screen, position.X - camera.X, position.Y - position.Z - camera.Y, ~gamma.Negative, ""u8, TextDrawFlags.None);
    }

    /// <inheritdoc/>
    [EngineRoutine(0x004151f0)]
    public override void SetLayer() => Layer = ObjectLayer.Cursor;
}
