using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of an omni-light object ID whose sprites light the area around them in the color of their frame.
/// </summary>
public sealed class VidLight : Vid
{
    private const uint BlackColor = 0xff000000;
    private const int HeadFieldsSize = 5 * sizeof(short);

    private int _dataSize;
    private byte[] _colors = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="VidLight"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    public VidLight(KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, in VidResource file, int totalFrames)
        : base(id, gameObject, weapons, file, totalFrames)
    {
    }

    [EngineRoutine(0x00414b70)]
    private VidLight(VidLight source)
        : base(source)
    {
        _dataSize = source._dataSize;
        _colors = source._colors;
    }

    /// <inheritdoc/>
    [EngineRoutine(0x00414d00)]
    public override void SetLayer() => Layer = ObjectLayer.Light;

    /// <summary>
    /// Draws the light of a sprite with the gamma of the sprite and the gamma of the level applied.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="shift">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00414d10)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 shift)
    {
        int frame = sprite.Frame;
        uint color = frame >= 0 && frame < _colors.Length / sizeof(uint) ? MemoryMarshal.Read<uint>(_colors.AsSpan(frame * sizeof(uint))) : 0;
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

        var origin = new Vector2(position.X - shift.X, position.Y - position.Z - shift.Y);
        map.Session.Graphics.DrawLightSource(screen, new Vector3(origin, position.Z), Size.AsVector2(), color, properties.HasFlag(ObjectProperties.DoubleLight));
    }

    [EngineRoutine(0x00412800)]
    internal override Vid CreateMirror() => new VidLight(this);

    [EngineRoutine(0x00414c10)]
    internal override void Destruct(GameSession session)
    {
        if (NextMirror == this)
        {
            _colors = [];
            session.VidMemoryInUse -= _dataSize;
            _dataSize = 0;
        }

        base.Destruct(session);
    }

    [EngineRoutine(0x00414c60)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, GameSession session)
    {
        var resource = default(Resource);
        resource.Open(null, data.Span, Resource.AnyTag);
        resource.GoBegin("HEAD"u8);
        resource.Read(stackalloc byte[HeadFieldsSize]);
        if (resource.GoNext("DATA"u8) != Resource.Found)
        {
            Report(session.Error, LogError.Load, 0, "DATA"u8);
        }

        FrameSize = new Size((short)(int)Size.X, (short)(int)Size.Y);
        _dataSize = resource.SubLoad(null, out _colors);
        if (_dataSize == 0)
        {
            Report(session.Error, LogError.Load, 0, "cadr"u8);
        }

        session.VidMemoryInUse += _dataSize;
    }
}
