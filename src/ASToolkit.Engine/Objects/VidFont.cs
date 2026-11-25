using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data of an object ID whose sprites draw text with a font.
/// </summary>
public sealed class VidFont : Vid
{
    private const int GlyphCount = 256;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidFont"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="maxTextureSize">The largest texture width and height the device accepts.</param>
    public VidFont(KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, int maxTextureSize)
        : base(id, gameObject, weapons, Map.DefaultSpriteSpeed, GlyphCount, ObjectLayer.Ground, null)
    {
        MaxTextureSize = maxTextureSize;
    }

    [EngineRoutine(0x00415070)]
    private VidFont(VidFont source)
        : base(source)
    {
        MaxTextureSize = source.MaxTextureSize;
        Font = source.Font;
        GlyphSize = source.GlyphSize;
        Style = source.Style;
    }

    [EngineRoutine(0x00412850)]
    internal override Vid CreateMirror() => new VidFont(this);

    /// <summary>
    /// Gets the font of this object ID, or <see langword="null"/> before the sprite file loads.
    /// </summary>
    public D3DFont? Font { get; private set; }

    /// <summary>
    /// Gets the height and width of the glyphs of the font, in pixels.
    /// </summary>
    public Size GlyphSize { get; private set; }

    /// <summary>
    /// Gets how the glyphs are rendered and drawn.
    /// </summary>
    public D3DFontStyle Style { get; private set; }

    /// <summary>
    /// Gets the largest texture width and height the device accepts.
    /// </summary>
    public int MaxTextureSize { get; }

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
        Font.Draw(screen, new Vector2(position.X - camera.X, position.Y - position.Z - camera.Y), ~gamma.Negative, ""u8);
    }

    /// <inheritdoc/>
    [EngineRoutine(0x004151f0)]
    public override void SetLayer() => Layer = ObjectLayer.Cursor;

    [EngineRoutine(0x00415140)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, MyError? error)
    {
        FileFrameCount = GlyphCount;
        FrameSize = new Size((short)(int)Size.X, (short)(int)Size.Y);
        FileType = VidType.Font;
        GlyphSize = new Size((int)Size.X, (int)Size.Y);
        Style = D3DFontStyle.FixedPitch;
        Font = new D3DFont(Path, GlyphSize, Style);
        Font.Create(MaxTextureSize);
        Font.CreateStateBlocks();
    }
}
