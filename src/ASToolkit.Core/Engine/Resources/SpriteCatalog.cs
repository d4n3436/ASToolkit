using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using System.Collections.Concurrent;
using System.Numerics;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the sprite definitions of a game folder, with the changes the level script makes to them.
/// </summary>
public sealed class SpriteCatalog
{
    /// <summary>
    /// Represents the deepest chain of child launches.
    /// </summary>
    public const int MaxLaunchDepth = 3;

    private const int DefaultSpriteSpeed = 71;
    private const KnownObject EmptyId = (KnownObject)(-1);

    private readonly ObjectTable _objects;
    private readonly SpriteDefinition?[] _definitions;
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();

    /// <summary>
    /// Creates the definitions of a game folder.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="objects">The object table.</param>
    public SpriteCatalog(string gameFolder, ObjectTable objects)
    {
        _objects = objects;
        Parts = new SpritePartReader(gameFolder, objects);
        Empty = new SpriteDefinition(EmptyId, default, default, DefaultSpriteSpeed);
        int length = objects.Ids.Max(id => (int)id) + 1;
        _definitions = new SpriteDefinition?[length];
        foreach (var id in objects.Ids)
        {
            if (id < 0 || !objects.TryFind(id, out var obj))
                continue;

            objects.TryGetWeapon(obj, out var weapon);
            var file = Parts.GetFile(objects.GetPath(id));
            var definition = new SpriteDefinition(id, obj, weapon, file?.Speed ?? DefaultSpriteSpeed, Parts.CountFrames(objects.GetPath(id)), (int)GetLayer(id, obj, file));

            if (id == 0)
            {
                LoadingScreen = definition;
                continue;
            }

            _definitions[(int)id] = definition;

            if (obj.Properties.HasFlag(ObjectProperties.Hash))
            {
                LargestHashedSize = Vector2.Max(LargestHashedSize, obj.Size.AsVector2());
            }
        }

        ResolveLinks();
    }

    private void ResolveLinks()
    {
        foreach (var definition in _definitions)
        {
            definition?.LinkDefinition = Find(definition.Object.Link);
        }
    }

    internal FrameMask? GetFrameMask(SpriteDefinition definition, int frame)
    {
        if (_masks.TryGetValue((definition.Id, frame), out var mask))
            return mask;

        VidResource? file;
        lock (Parts)
        {
            file = Parts.GetFile(_objects.GetPath(definition.Id));
        }

        mask = file is null || frame < 0 || frame >= file.Value.FrameCount ? null : FrameMask.Read(file.Value, frame);

        return _masks.GetOrAdd((definition.Id, frame), mask);
    }

    private static ObjectLayer GetLayer(KnownObject id, in GameObject obj, VidResource? sprite) => obj.GetLayer(id, sprite);

    /// <summary>
    /// Returns the name of a definition's record.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The name.</returns>
    public string GetName(SpriteDefinition definition) => _objects.GetName(definition.RecordId);

    /// <summary>
    /// Exchanges the identities of two definitions.
    /// </summary>
    /// <param name="first">The first definition.</param>
    /// <param name="second">The second definition.</param>
    [EngineRoutine(0x00411790)]
    public void Exchange(SpriteDefinition first, SpriteDefinition second)
    {
        if (first == second)
            return;

        foreach (var definition in _definitions)
        {
            if (definition is null)
                continue;

            if (definition.LinkDefinition == first)
            {
                definition.LinkDefinition = second;
            }
            else if (definition.LinkDefinition == second)
            {
                definition.LinkDefinition = first;
            }
        }

        _definitions[(int)first.Id] = second;
        _definitions[(int)second.Id] = first;
        (first.Identity, second.Identity) = (second.Identity, first.Identity);
        (first.Id, second.Id) = (second.Id, first.Id);
    }

    /// <summary>
    /// Undoes the level script's exchanges and reloads every definition's record.
    /// </summary>
    [EngineRoutine(0x00411920)]
    public void ReloadRecords()
    {
        foreach (var definition in _definitions)
        {
            if (definition is not null && definition.Identity != definition)
            {
                Exchange(definition.Identity, definition);
            }
        }

        foreach (var definition in _definitions)
        {
            definition?.LoadParameters();
        }

        ResolveLinks();
    }

    /// <summary>
    /// Clears every definition's counts and script handlers for a new level.
    /// </summary>
    [EngineRoutine(0x0040c570)]
    public void ResetLevelState()
    {
        foreach (var definition in _definitions)
        {
            definition?.ResetLevelState();
            definition?.ClearHandlers();
        }
    }

    internal MovementMap CreateMovementMap(LevelModel model) => new(model, Parts);

    internal void AddTerrain(MovementMap movement, MergedMapSprite sprite) => movement.Add(sprite, Parts, true);

    /// <summary>
    /// Gets the sprite parts of the definitions.
    /// </summary>
    public SpritePartReader Parts { get; }

    /// <summary>
    /// Gets the gravity of falling objects in pixels per millisecond squared.
    /// </summary>
    public float Gravitation => _objects.Gravitation * SpriteDefinition.PerMillisecondSquared;

    /// <summary>
    /// Gets the second gravity in pixels per millisecond squared.
    /// </summary>
    public float Gravitation2 => _objects.Gravitation2 * SpriteDefinition.PerMillisecondSquared;

    /// <summary>
    /// Gets the fastest scroll speed of the camera along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed => _objects.ScrollSpeed * SpriteDefinition.PerMillisecond;

    /// <summary>
    /// Gets the largest footprint of a hashed definition.
    /// </summary>
    public Vector2 LargestHashedSize { get; }

    /// <summary>
    /// Gets the empty definition of the cursor and goal sprites.
    /// </summary>
    public SpriteDefinition Empty { get; }

    /// <summary>
    /// Gets the definition in the first slot of the table, drawn by the loading screen.
    /// </summary>
    public SpriteDefinition? LoadingScreen { get; }

    /// <summary>
    /// Returns the definition of an id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The definition, or null when the table has none.</returns>
    public SpriteDefinition? Find(KnownObject id) => id >= 0 && (int)id < _definitions.Length ? _definitions[(int)id] : null;

    /// <summary>
    /// Returns the army a new sprite of a definition joins.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The army.</returns>
    public Army GetArmy(SpriteDefinition definition) =>
        definition.Object.WeaponIndex == 0 && definition.Object.Link != 0 ? Find(definition.Object.Link)?.Army ?? definition.Army : definition.Army;
}
