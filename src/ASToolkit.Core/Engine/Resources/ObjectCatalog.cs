using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using System.Collections.Concurrent;
using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class ObjectCatalog
{
    public const int MaxLaunchDepth = 3;

    private const int DefaultSpriteSpeed = 71;

    private readonly ObjectTable _objects;
    private readonly ObjectDefinition?[] _kinds;
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();

    public ObjectCatalog(string gameFolder, ObjectTable objects)
    {
        _objects = objects;
        Parts = new SpritePartReader(gameFolder, objects);
        Loot = new MonsterLootCatalog(gameFolder);
        int length = objects.Ids.Max(id => (int)id) + 1;
        _kinds = new ObjectDefinition?[length];
        foreach (var id in objects.Ids)
        {
            if (id <= 0 || !objects.TryFind(id, out var obj))
                continue;

            objects.TryGetWeapon(obj, out var weapon);
            var file = Parts.GetFile(objects.GetPath(id));
            _kinds[(int)id] = new ObjectDefinition(id, obj, weapon, file?.Speed ?? DefaultSpriteSpeed, file?.FrameCount ?? 0,
                (int)GetLayer(id, obj, file));

            if (obj.Properties.HasFlag(ObjectProperties.Hash))
            {
                LargestHashedSize = Vector2.Max(LargestHashedSize, obj.Size.AsVector2());
            }
        }
    }

    public FrameMask? GetFrameMask(ObjectDefinition kind, int frame)
    {
        if (_masks.TryGetValue((kind.Id, frame), out var mask))
            return mask;

        VidResource? file;
        lock (Parts)
        {
            file = Parts.GetFile(_objects.GetPath(kind.Id));
        }

        mask = file is null || (uint)frame >= file.Value.FrameCount ? null : FrameMask.Read(file.Value, frame);

        return _masks.GetOrAdd((kind.Id, frame), mask);
    }

    [EngineRoutine(0x00411b50)]
    [EngineRoutine(0x00415e90)]
    [EngineRoutine(0x0041a730)]
    [EngineRoutine(0x0041b880)]
    private static ObjectLayer GetLayer(KnownObject id, in GameObject obj, VidResource? sprite) => obj.GetLayer(id, sprite);

    public MonsterLootCatalog Loot { get; }

    public MovementMap CreateMovementMap(LevelModel model) => new(model, Parts);

    public void AddTerrain(MovementMap map, MergedMapSprite sprite) => map.Add(sprite, Parts, true);

    public SpritePartReader Parts { get; }

    public float Gravitation => _objects.Gravitation * ObjectDefinition.PerMillisecondSquared;

    public float Gravitation2 => _objects.Gravitation2 * ObjectDefinition.PerMillisecondSquared;

    public Vector2 LargestHashedSize { get; }

    public ObjectDefinition? Find(KnownObject id) => (uint)id < (uint)_kinds.Length ? _kinds[(int)id] : null;

    [EngineRoutine(0x0043f200)]
    public int GetArmy(ObjectDefinition kind) =>
        kind.Object.WeaponIndex == 0 && kind.Object.Link != 0 ? Find(kind.Object.Link)?.Army ?? kind.Army : kind.Army;
}
