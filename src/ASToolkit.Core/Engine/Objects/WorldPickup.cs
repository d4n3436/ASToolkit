using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class WorldPickup
{
    private readonly Unit? _body;

    public WorldPickup(ObjectDefinition kind, Vector3 position, MergedMapSprite? placement, int createdAt = 0,
        int expiresAt = int.MaxValue, Facing facing = default, Unit? body = null)
    {
        _body = body;
        Kind = kind;
        Position = position;
        Placement = placement;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Facing = facing;
    }

    public ObjectDefinition Kind { get; }

    public KnownObject Id => Kind.Id;

    public Vector3 Position { get; }

    public MergedMapSprite? Placement { get; }

    public int CreatedAt { get; }

    public int ExpiresAt { get; }

    public Facing Facing { get; }

    public bool IsDropped => ExpiresAt != int.MaxValue;

    [EngineRoutine(0x00440110)]
    public EffectState GetState(int time)
    {
        var body = _body;

        if (body is null)
            return new EffectState(Id, Position, Facing.Value, SpriteAnimation.Stand, CreatedAt, 0);

        int count = body.LastFrame - body.FirstFrame + 1;
        int ticks = (time - body.LastTickTime) / Math.Max(1, Kind.SpriteSpeed);
        int frame = body.FirstFrame + (body.Frame - body.FirstFrame + ticks) % Math.Max(1, count);

        return new EffectState(Id, Position, Facing.Value, body.Animation, CreatedAt, frame);
    }
}
