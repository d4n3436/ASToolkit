namespace ASToolkit.Engine;

internal sealed class SpawnedObjectSystem
{
    private readonly SpriteCatalog _definitions;
    private readonly MovementMap _movement;
    private readonly Map _map;
    private readonly SpriteKinematics _motion;

    public SpawnedObjectSystem(SpriteCatalog definitions, MovementMap movement, Map map)
    {
        _definitions = definitions;
        _movement = movement;
        _map = map;
        _motion = map.Motion;
    }

    [EngineRoutine(0x004449c0)]
    public static void AddLinkToLast(Sprite parent, Sprite link)
    {
        if (link.Owner is not null)
            return;

        var last = parent;
        while (last.Link is not null)
        {
            last = last.Link;
        }

        last.Link = link;
        link.Owner = last;
    }

    public static EffectState GetState(Sprite sprite) =>
        new(sprite.Definition.Id, sprite.Position, sprite.Direction, sprite.Animation, sprite.CreatedAt, sprite.Frame, sprite.KeyframePosition);

}
