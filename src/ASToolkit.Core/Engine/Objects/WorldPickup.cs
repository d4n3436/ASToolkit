using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class WorldPickup
{
    public WorldPickup(ObjectDefinition kind, Vector3 position, MergedMapSprite? placement, int createdAt = 0,
        int expiresAt = int.MaxValue, Facing facing = default)
    {
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

    public EffectState GetState() => new(Id, Position, Facing.Value, SpriteAnimation.Stand, CreatedAt, 0);
}
