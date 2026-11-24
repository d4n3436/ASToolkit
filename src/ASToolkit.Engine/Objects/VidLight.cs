using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of an omni-light object ID whose sprites light the area around them in the color of their frame.
/// </summary>
public sealed class VidLight : Vid
{
    private const uint BlackColor = 0xff000000;

    private readonly VidResource _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidLight"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    [EngineRoutine(0x00414c60)]
    public VidLight(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames)
        : base(id, gameObject, weapon, file, totalFrames)
    {
        _file = file;
        FrameSize = new Size((short)(int)gameObject.Size.X, (short)(int)gameObject.Size.Y);
    }

    /// <summary>
    /// Draws the light of a sprite with the gamma of the sprite and the gamma of the level applied.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00414d10)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        uint color = _file.GetLightColor(sprite.Frame);
        if (IsHidden || color == 0 || color == BlackColor)
            return;

        var properties = Properties;

        var gamma = sprite.GetGamma().Add(ObjectGamma);
        if (!properties.HasFlag(ObjectProperties.Gamma))
        {
            gamma = map.Session.Graphics.Gamma.Add(gamma);
        }

        var position = sprite.Position;

        if (gamma.Negative != 0 || gamma.Positive != 0)
        {
            color = gamma.Apply(color);
        }

        var origin = new Vector2(position.X - camera.X, position.Y - position.Z - camera.Y);
        map.Session.Graphics.DrawLightSource(screen, new Vector3(origin, position.Z), Size.AsVector2(), color, properties.HasFlag(ObjectProperties.DoubleLight));
    }

    /// <inheritdoc/>
    [EngineRoutine(0x00414d00)]
    public override void SetLayer() => Layer = ObjectLayer.Light;
}
