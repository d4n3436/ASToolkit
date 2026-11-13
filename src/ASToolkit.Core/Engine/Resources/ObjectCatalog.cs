using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using System.Collections.Concurrent;

namespace ASToolkit.Core.Engine;

internal sealed class ObjectCatalog
{
    public const int MaxLaunchDepth = 3;

    private readonly ObjectTable _objects;
    private readonly SpritePartReader _parts;
    private readonly ObjectDefinition?[] _kinds;
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();

    public ObjectCatalog(string gameFolder, ObjectTable objects)
    {
        _objects = objects;
        _parts = new SpritePartReader(gameFolder, objects);
        Loot = new MonsterLootCatalog(gameFolder);
        int length = objects.Ids.Max(id => (int)id) + 1;
        _kinds = new ObjectDefinition?[length];
        foreach (var id in objects.Ids)
        {
            if (id <= 0 || !objects.TryFind(id, out var obj))
                continue;

            objects.TryGetWeapon(obj, out var weapon, out var behavior);
            var file = _parts.GetFile(objects.GetPath(id));
            _kinds[(int)id] = new ObjectDefinition(id, obj, weapon, file?.Speed ?? 0, behavior, file?.FrameCount ?? 0,
                (int)GetLayer(id, obj, file));
        }
    }

    public FrameMask? GetFrameMask(ObjectDefinition kind, int frame)
    {
        if (_masks.TryGetValue((kind.Id, frame), out var mask))
            return mask;

        VidResource? file;
        lock (_parts)
        {
            file = _parts.GetFile(_objects.GetPath(kind.Id));
        }

        mask = file is null || (uint)frame >= file.Value.FrameCount ? null : FrameMask.Read(file.Value, frame);

        return _masks.GetOrAdd((kind.Id, frame), mask);
    }

    [EngineRoutine(0x00411b50)]
    [EngineRoutine(0x00415e90)]
    private static ObjectLayer GetLayer(KnownObject id, in GameObject obj, VidResource? sprite)
    {
        var file = sprite?.Flags ?? VidFlags.None;

        if (file.HasFlag(VidFlags.SurfaceLayer))
            return ObjectLayer.Surface;

        if (file.HasFlag(VidFlags.OmniLight))
            return ObjectLayer.OmniLight;

        if (file.HasFlag(VidFlags.Surface))
            return GetSurfaceLayer(id, obj, file, sprite!.Value.SurfaceCount);

        if (obj.SpriteClass == SpriteClass.Scenery)
            return file.HasFlag(VidFlags.Blended) ? ObjectLayer.BlendedScenery : ObjectLayer.Scenery;

        if (obj.Flags.HasFlag(ObjectFlags.GroundDecal))
            return ObjectLayer.GroundDecal;

        if (obj.Flags.HasFlag(ObjectFlags.Raised))
            return ObjectLayer.Raised;

        if (file.HasFlag(VidFlags.Blended))
            return ObjectLayer.BlendedObject;

        return (obj.Flags & (ObjectFlags.Outline | ObjectFlags.Solid)) == 0 ? ObjectLayer.Object : ObjectLayer.SolidObject;
    }

    [EngineRoutine(0x0041a730)]
    [EngineRoutine(0x0041b880)]
    private static ObjectLayer GetSurfaceLayer(KnownObject id, in GameObject obj, VidFlags file, int surfaces)
    {
        bool blended = file.HasFlag(VidFlags.Blended);
        bool depth = file.HasFlag(VidFlags.Depth);

        if (obj.Flags.HasFlag(ObjectFlags.GroundDecal))
            return ObjectLayer.SurfaceDecal;

        if (obj.SpriteType == SpriteType.Railway)
            return ObjectLayer.RailwaySurface;

        if (blended && depth)
            return ObjectLayer.BlendedSurface;

        if (surfaces == 0)
            return ObjectLayer.EmptySurface;

        if (depth)
            return ObjectLayer.DepthSurface;

        if (id == KnownObject.Cursor)
            return ObjectLayer.Cursor;

        if (blended)
            return obj.Flags.HasFlag(ObjectFlags.Bobbing) ? ObjectLayer.BobbingSurface : ObjectLayer.BlendedSurface;

        return ObjectLayer.Surface;
    }

    public MonsterLootCatalog Loot { get; }

    public MovementMap CreateMovementMap(LevelModel model) => new(model, _parts);

    public bool AddTerrain(MovementMap map, MergedMapSprite sprite) => map.Add(sprite, _parts, true);

    public float Gravity => _objects.Gravity * ObjectDefinition.PerMillisecondSquared;

    public float HeavyGravity => _objects.HeavyGravity * ObjectDefinition.PerMillisecondSquared;

    public ObjectDefinition? Find(KnownObject id) => (uint)id < (uint)_kinds.Length ? _kinds[(int)id] : null;
}
