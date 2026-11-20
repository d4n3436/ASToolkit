using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;
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

    /// <summary>
    /// Represents the id of the definition of the prerendered ground.
    /// </summary>
    public const KnownObject HardwareGroundId = (KnownObject)0x400;

    private const int DefaultSpriteSpeed = 71;
    private const KnownObject EmptyId = (KnownObject)(-1);
    private const string SpriteFileExtension = ".vid";
    private const string PictureFileExtension = ".bmp";
    private const string HardwareGroundName = "Self Created Hardware Prerendered Ground ";

    private static readonly Vector3 DefaultSize = new(24, 16, 20);
    private static readonly Vector3 HardwareGroundSize = new(256, 256, 1);

    private readonly ObjectTable _objects;
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();
    private SpriteDefinition?[] _definitions;

    /// <summary>
    /// Creates the definitions of a game folder.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="objects">The object table.</param>
    public SpriteCatalog(string gameFolder, ObjectTable objects)
    {
        _objects = objects;
        Parts = new SpritePartReader(gameFolder, objects);
        Empty = CreateDefault();
        int length = objects.Ids.Max(id => (int)id) + 1;
        _definitions = new SpriteDefinition?[length];
        foreach (var id in objects.Ids)
        {
            if (id < 0 || !objects.TryFind(id, out var obj))
                continue;

            objects.TryGetWeapon(obj, out var weapon);
            var file = Parts.GetFile(objects.GetPath(id));
            var definition = new SpriteDefinition(id, obj, weapon, file?.Speed ?? DefaultSpriteSpeed, Parts.CountFrames(objects.GetPath(id)), (int)GetLayer(id, obj, file))
            {
                FrameHeight = file?.Height ?? 0
            };

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

        ResolveLinks(null);
    }

    private void ResolveLinks(GameLogger? logger)
    {
        foreach (var definition in _definitions)
        {
            if (definition is not null)
            {
                SetChildAndLink(definition, logger);
            }
        }
    }

    internal FrameMask? GetFrameMask(SpriteDefinition definition, int frame)
    {
        if (_masks.TryGetValue((definition.Id, frame), out var mask))
            return mask;

        VidResource? file;
        lock (Parts)
        {
            file = Parts.GetFile(GetPath(definition.Id));
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
    public string GetName(SpriteDefinition definition) => definition.RecordId == HardwareGroundId ? HardwareGroundName : _objects.GetName(definition.RecordId);

    private string GetPath(SpriteDefinition definition) => GetPath(definition.RecordId);

    private string GetPath(KnownObject id) => id == HardwareGroundId ? string.Empty : _objects.GetPath(id);

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
    public void ReloadRecords() => ReloadRecords(null);

    [EngineRoutine(0x00411920)]
    internal void ReloadRecords(GameLogger? logger)
    {
        foreach (var definition in _definitions)
        {
            if (definition is not null && definition.Identity != definition)
            {
                Exchange(definition.Identity, definition);
            }
        }

        if (logger is not null && LoadingScreen is not null)
        {
            LogRecordErrors(logger, LoadingScreen);
        }

        foreach (var definition in _definitions)
        {
            definition?.LoadParameters();

            if (definition is not null && logger is not null)
            {
                LogRecordErrors(logger, definition);
            }
        }

        ResolveLinks(logger);
    }

    private void LogRecordErrors(GameLogger logger, SpriteDefinition definition)
    {
        ref readonly var obj = ref definition.Object;
        int directions = obj.Directions;
        int frames = definition.Layout.TotalFrames;
        string path = GetPath(definition);

        if (!path.EndsWith(SpriteFileExtension, StringComparison.OrdinalIgnoreCase) && !path.EndsWith(PictureFileExtension, StringComparison.OrdinalIgnoreCase))
            return;

        if (directions == 0)
        {
            LogDefinitionError(logger, definition, LogError.Invalid, 0, "NoDir==0"u8);
            return;
        }

        if (frames == 0)
        {
            LogDefinitionError(logger, definition, LogError.Invalid, 0, "noCadr==0"u8);
        }
        else if (frames < directions)
        {
            LogDefinitionError(logger, definition, LogError.Invalid, 0, "noCadr < noDir"u8);
            directions = frames;
        }

        int needed = 0;
        for (int state = 0; state <= (int)SpriteAnimation.Death2; state++)
        {
            needed += obj.FrameCounts[state] * directions;
        }

        if (frames < needed)
        {
            LogDefinitionError(logger, definition, LogError.Missing, 0, "noCadr for noAnimCadr and noDir"u8);
        }
    }

    [EngineRoutine(0x00413a60)]
    private void SetChildAndLink(SpriteDefinition definition, GameLogger? logger)
    {
        int link = (int)definition.Object.Link;
        definition.LinkDefinition = Find(definition.Object.Link);

        if (logger is null)
            return;

        if (link != 0 && !IsDefined(link))
        {
            LogDefinitionError(logger, definition, LogError.Invalid, link, "LinkVid"u8);
        }

        foreach (int child in definition.Object.Children)
        {
            if (child != 0 && !IsDefined(Math.Abs(child)))
            {
                LogDefinitionError(logger, definition, LogError.Invalid, child, "child"u8);
            }
        }
    }

    [EngineRoutine(0x00413a30)]
    private void LogDefinitionError(GameLogger logger, SpriteDefinition definition, LogError code, int value, ReadOnlySpan<byte> text) =>
        logger.Error("VID [%i-%s]"u8, code, value, text, (int)definition.Id, (ReadOnlyMemory<byte>)GameTextEncoding.Cyrillic.GetBytes(GetName(definition)));

    private bool IsDefined(int id) => id >= 0 && id < _definitions.Length && _definitions[id] is not null;

    /// <summary>
    /// Clears every definition's counts and script handlers for a new level.
    /// </summary>
    public void ResetLevelState()
    {
        foreach (var definition in _definitions)
        {
            definition?.ResetLevelState();
            definition?.ClearHandlers();
        }
    }

    internal void LogCounts(GameLogger logger)
    {
        for (int i = 0; i < _definitions.Length; i++)
        {
            var definition = _definitions[i];

            if (definition is null)
                continue;

            var counts = definition.Counts;
            if (counts[0] + counts[1] + counts[2] + counts[3] == 0)
                continue;

            var name = (ReadOnlyMemory<byte>)GameTextEncoding.Cyrillic.GetBytes(GetName(definition));
            var path = (ReadOnlyMemory<byte>)GameTextEncoding.Cyrillic.GetBytes(GetPath(definition));
            logger.Write("NoVid[%3i]=%i %i %i %i %s Layer=%i %s"u8, i, counts[0], counts[1], counts[2], counts[3], name, definition.Layer, path);
        }
    }

    internal MovementMap CreateMovementMap(MapData mapData) => new(mapData, Parts);

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

    [EngineRoutine(0x00419750)]
    internal SpriteDefinition CreateHardwareGround()
    {
        var definition = new SpriteDefinition(HardwareGroundId, new GameObject(SpriteClass.Primitive, HardwareGroundSize), default, DefaultSpriteSpeed);

        if (_definitions.Length <= (int)HardwareGroundId)
        {
            Array.Resize(ref _definitions, (int)HardwareGroundId + 1);
        }

        _definitions[(int)HardwareGroundId] = definition;

        return definition;
    }

    [EngineRoutine(0x00413710)]
    private static SpriteDefinition CreateDefault() => new(EmptyId, new GameObject(SpriteClass.Primitive, DefaultSize), default, DefaultSpriteSpeed);

    /// <summary>
    /// Gets the sounds of the object table.
    /// </summary>
    public IReadOnlyList<SfxRecord> Sounds => _objects.Sounds;

    /// <summary>
    /// Gets the fastest scroll speed of the camera along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed => _objects.ScrollSpeed * SpriteDefinition.PerMillisecond;

    /// <summary>
    /// Gets the milliseconds the cursor rests before a mouse tip shows.
    /// </summary>
    public int HoverDelay => _objects.HoverDelay;

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
