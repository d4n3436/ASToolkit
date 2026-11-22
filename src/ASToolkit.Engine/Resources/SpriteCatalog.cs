using System.Drawing;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;
using System.Collections.Concurrent;
using System.Numerics;
using ASToolkit.Core;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the sprite definitions of a game folder, with the changes the level script makes to them.
/// </summary>
public sealed class SpriteCatalog
{
    private static readonly ConcurrentDictionary<(string Path, bool IsText), Lazy<ReadOnlyMemory<byte>>> ConvertedPictures = new();

    /// <summary>
    /// Represents the deepest chain of child launches.
    /// </summary>
    public const int MaxLaunchDepth = 3;

    /// <summary>
    /// Represents the ID of the definition of the prerendered ground.
    /// </summary>
    public const KnownObject HardwareGroundId = (KnownObject)1024;

    /// <summary>
    /// Represents the largest texture width and height a catalog allows when it isn't given the device's limit.
    /// </summary>
    public const int DefaultMaxTextureSize = 32768;

    private const int WeaponRecordSize = 612;
    private const int DefinitionSize = 1156;
    private const int OneBits = 0x3f800000;
    internal const int DefaultSpriteSpeed = 71;
    private const KnownObject EmptyId = (KnownObject)(-1);
    private const string FontExtension = ".fon";
    private const string TrueTypeExtension = ".ttf";
    private const string AnimationExtension = ".flc";
    private const string HardwareGroundName = "Self Created Hardware Prerendered Ground ";

    private static readonly Vector3 DefaultSize = new(24, 16, 20);
    private readonly ObjectTable _objects;
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();
    private SpriteDefinition?[] _definitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteCatalog"/> class with the definitions of a game folder.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="session">The session whose sound system is loaded first and whose log takes the loading errors and summaries, or <see langword="null"/> to load silently.</param>
    /// <param name="isTrueColor"><see langword="true"/> for a 32-bit back buffer, where only built terrain takes the 16-bit class; <see langword="false"/> for a 16-bit back buffer, where every software sprite takes the 16-bit class.</param>
    /// <param name="format">The channel layout of the device's 16-bit textures, which 16-bit colors follow, or <see langword="null"/> for 5-6-5.</param>
    /// <param name="maxTextureSize">The largest texture width and height the device accepts, which limits the textures of fonts.</param>
    [EngineRoutine(0x00411510)]
    public SpriteCatalog(string gameFolder, ObjectTable objects, GameSession? session = null, bool isTrueColor = true, HighColorFormat? format = null, int maxTextureSize = DefaultMaxTextureSize)
    {
        _objects = objects;
        var logger = session?.Logger;
        session?.Sound ??= new SoundSystem(objects.Sounds, new GameFolder(gameFolder), logger, isHighQuality: session.IsSoundHighQuality);

        int start = Environment.TickCount;
        if (objects.WeaponCount != 0)
        {
            logger?.Write("LoadWeapon::No=%-5i             sizeof(WEAPON)=%-4i"u8, objects.WeaponCount, WeaponRecordSize);
        }

        Parts = new SpritePartReader(gameFolder, objects);
        Empty = CreateDefault();
        int length = objects.Ids.Max(id => (int)id) + 1;
        _definitions = new SpriteDefinition?[length];
        int count = 0;
        foreach (var id in objects.Ids)
        {
            if (id < 0 || !objects.TryFind(id, out var gameObject))
                continue;

            var definition = CreateVid(gameFolder, id, gameObject, isTrueColor, format ?? HighColorFormat.Rgb565, maxTextureSize, logger, session?.Sound);
            if (definition is null)
                continue;

            count = Math.Max(count, (int)id + 1);
            if (gameObject.WeaponIndex >= objects.WeaponCount)
            {
                definition.Report(logger, LogError.Text, gameObject.WeaponIndex, "nWeapon > noWeapon"u8);
            }

            if (id == 0)
            {
                LoadingScreen = definition;
                continue;
            }

            _definitions[(int)id] = definition;
            if (gameObject.Properties.HasFlag(ObjectProperties.Hash))
            {
                LargestHashedSize = Vector2.Max(LargestHashedSize, gameObject.Size.AsVector2());
            }
        }

        ResolveLinks(logger, false);
        var largest = Size.Empty;
        foreach (var definition in _definitions.Prepend(LoadingScreen))
        {
            if (definition is not null)
            {
                largest = new Size(Math.Max(largest.Width, definition.FrameSize.Width), Math.Max(largest.Height, definition.FrameSize.Height));
            }
        }

        logger?.Write("LoadVid::No   =%-15i   sizeof(VID)   =%-5i    load time     =%ims   MaxSizeX,Y=%i,%i"u8, count, DefinitionSize, Environment.TickCount - start, largest.Width, largest.Height);
    }

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
    /// Gets the sounds of the object table.
    /// </summary>
    public IReadOnlyList<SfxRecord> Sounds => _objects.Sounds;

    /// <summary>
    /// Gets the fastest scroll speed of the camera along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed => _objects.ScrollSpeed * SpriteDefinition.PerMillisecond;

    /// <summary>
    /// Gets the time in milliseconds the cursor must rest before a mouse tip shows.
    /// </summary>
    public int HoverDelay => _objects.HoverDelay;

    /// <summary>
    /// Gets the packed gamma applied to the player's sprite, or zero for none.
    /// </summary>
    public uint SelectedGamma => _objects.SelectedGamma;

    /// <summary>
    /// Gets the packed gamma applied to an enemy under the cursor, or zero for none.
    /// </summary>
    public uint HoveredEnemyGamma => _objects.HoveredEnemyGamma;

    /// <summary>
    /// Gets the packed gamma applied to a sprite under the cursor, or zero for none.
    /// </summary>
    public uint HoveredGamma => _objects.HoveredGamma;

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
    /// Returns the name of a definition's record.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The name.</returns>
    public string GetName(SpriteDefinition definition) => definition.RecordId == HardwareGroundId ? HardwareGroundName : _objects.GetName(definition.RecordId);

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

        for (int state = 0; state < first.StateHandlers.Length; state++)
        {
            (first.StateHandlers[state], second.StateHandlers[state]) = (second.StateHandlers[state], first.StateHandlers[state]);
        }

        (first.DestroyHandler, second.DestroyHandler) = (second.DestroyHandler, first.DestroyHandler);
        (first.DamageHandler, second.DamageHandler) = (second.DamageHandler, first.DamageHandler);
        (first.CollisionHandler, second.CollisionHandler) = (second.CollisionHandler, first.CollisionHandler);
    }

    /// <summary>
    /// Undoes the level script's exchanges and reloads every definition's record.
    /// </summary>
    public void ReloadRecords() => ReloadRecords(null, null);

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

    /// <summary>
    /// Returns the definition of an object ID.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <returns>The definition, or null if no definition has that ID.</returns>
    public SpriteDefinition? Find(KnownObject id) => id >= 0 && (int)id < _definitions.Length ? _definitions[(int)id] : null;

    /// <summary>
    /// Returns the army a new sprite of a definition joins.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The army.</returns>
    public Army GetArmy(SpriteDefinition definition) =>
        definition.Object.WeaponIndex == 0 && definition.Object.Link != 0 ? Find(definition.Object.Link)?.Army ?? definition.Army : definition.Army;

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

    [EngineRoutine(0x00411920)]
    internal void ReloadRecords(GameLogger? logger, SoundSystem? sound)
    {
        foreach (var definition in _definitions)
        {
            if (definition is not null && definition.Identity != definition)
            {
                Exchange(definition.Identity, definition);
            }
        }

        LoadingScreen?.LoadParameters(logger, sound);
        foreach (var definition in _definitions)
        {
            definition?.LoadParameters(logger, sound);
        }

        ResolveLinks(logger, true);
    }

    private static void LogUnopened(GameLogger? logger, string path) => logger?.Write("LOAD::Can't open file %s"u8, (ReadOnlyMemory<byte>)GameTextEncoding.Cyrillic.GetBytes(path));

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
            logger.Write("NoVid[%3i]=%i %i %i %i %s Layer=%i %s"u8, i, counts[0], counts[1], counts[2], counts[3], name, (int)definition.Layer, path);
        }
    }

    /// <summary>
    /// Puts a definition in the slot of an object ID, and grows the table when the ID is past its end.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="definition">The definition.</param>
    public void SetDefinition(KnownObject id, SpriteDefinition definition)
    {
        if (_definitions.Length <= (int)id)
        {
            Array.Resize(ref _definitions, (int)id + 1);
        }

        _definitions[(int)id] = definition;
    }

    /// <summary>
    /// Gets the ambient gamma the palettes of the sprite files were last rebuilt with.
    /// </summary>
    public Gamma Ambient { get; private set; }

    /// <summary>
    /// Rebuilds the palettes of every sprite file with an ambient gamma.
    /// </summary>
    /// <param name="ambient">The ambient gamma.</param>
    public void SetAmbient(Gamma ambient)
    {
        Ambient = ambient;
        foreach (var definition in _definitions)
        {
            definition?.SetGamma(ambient, SpriteDefinition.AllArmies, ambient);
        }
    }

    private ReadOnlyMemory<byte> GetBytes(string path)
    {
        lock (Parts)
        {
            return Parts.GetBytes(path);
        }
    }

    [EngineRoutine(0x00411b50)]
    private SpriteDefinition? CreateVid(string gameFolder, KnownObject id, in GameObject gameObject, bool isTrueColor, HighColorFormat format, int maxTextureSize, GameLogger? logger, SoundSystem? sound)
    {
        _objects.TryGetWeapon(gameObject, out var weapon);
        string path = _objects.GetPath(id);
        var file = Parts.GetFile(path);
        ReadOnlyMemory<byte> data = default;
        if (file is null)
        {
            if (path.Contains(FontExtension, StringComparison.Ordinal) || path.Contains(TrueTypeExtension, StringComparison.Ordinal))
            {
                var font = new FontSpriteDefinition(id, gameObject, weapon, maxTextureSize);
                Prepare(font, logger, sound);
                font.Load(default, default, logger);
                font.SetLayer();
                return font;
            }
            else if (path.Contains(TgaImage.Extension, StringComparison.Ordinal) || path.Contains(BmpImage.Extension, StringComparison.Ordinal) || path.Contains(AnimationExtension, StringComparison.Ordinal))
            {
                bool isText = gameObject.SpriteClass == SpriteClass.Text;
                data = logger is not null
                    ? ConvertPicture(gameFolder, path, isText, logger)
                    : ConvertedPictures.GetOrAdd((Path.GetFullPath(Path.Combine(gameFolder, path)), isText), key => new(() => ConvertPicture(gameFolder, path, key.IsText, null))).Value;
                if (data.IsEmpty)
                {
                    LogUnopened(logger, path);
                }
            }
        }

        if (!data.IsEmpty)
        {
            file = new VidResource(data);
        }

        if (file is not { } sprite)
        {
            if (logger is not null)
            {
                new Resource().Open(new GameFolder(gameFolder), logger, GameTextEncoding.Cyrillic.GetBytes(path), Resource.AnyTag);
            }

            LogUnopened(logger, path);
            return null;
        }

        int totalFrames = data.IsEmpty ? Parts.CountFrames(path) : sprite.FrameCount;
        var type = sprite.Type;
        SpriteDefinition definition;
        ReadOnlyMemory<byte> bytes = default;
        if (type.HasFlag(VidType.HardwareLayer))
        {
            definition = new HardwareLayerSpriteDefinition(id, gameObject, weapon, sprite, totalFrames);
        }
        else if (type.HasFlag(VidType.Light))
        {
            definition = new LightSpriteDefinition(id, gameObject, weapon, sprite, totalFrames);
        }
        else if (type.HasFlag(VidType.Hardware) && !(type.HasFlag(VidType.Blended) && type.HasFlag(VidType.ZBuffer)))
        {
            definition = new HardwareSpriteDefinition(id, gameObject, weapon, sprite, totalFrames);
        }
        else
        {
            bytes = data.IsEmpty ? GetBytes(path) : data;
            if (type.HasFlag(VidType.Hardware))
            {
                definition = new DepthFadedSpriteDefinition(id, gameObject, weapon, sprite, totalFrames, format);
            }
            else if (gameObject.SpriteClass == SpriteClass.BuiltTerrain || !isTrueColor)
            {
                definition = new HighColorSpriteDefinition(id, gameObject, weapon, sprite, totalFrames, format);
            }
            else
            {
                definition = new SoftwareSpriteDefinition(id, gameObject, weapon, sprite, totalFrames, format);
            }
        }

        Prepare(definition, logger, sound);
        definition.Load(sprite, bytes, logger);
        definition.SetLayer();
        return definition;
    }

    [EngineRoutine(0x00413710)]
    private static SpriteDefinition CreateDefault() => new(EmptyId, new GameObject(SpriteClass.Primitive, DefaultSize), default, DefaultSpriteSpeed, layer: ObjectLayer.Menu);

    private static ReadOnlyMemory<byte> ConvertPicture(string gameFolder, string path, bool isText, GameLogger? logger)
    {
        using var picture = isText ? new FontPicture(gameFolder, logger) : new Picture(gameFolder, logger);
        return picture.Load(path, string.Empty, string.Empty) ? picture.Convert() : default;
    }

    private void Prepare(SpriteDefinition definition, GameLogger? logger, SoundSystem? sound)
    {
        definition.Name = GetName(definition);
        definition.Path = GetPath(definition);
        if (logger is not null || sound is not null)
        {
            definition.LoadParameters(logger, sound);
        }
    }

    private void ResolveLinks(GameLogger? logger, bool isReload)
    {
        foreach (var definition in _definitions)
        {
            if (definition is not null && !(isReload && definition.FileType.HasFlag(VidType.FrameVector)))
            {
                SetChildAndLink(definition, logger);
            }
        }
    }

    private string GetPath(SpriteDefinition definition) => GetPath(definition.RecordId);

    private string GetPath(KnownObject id) => id == HardwareGroundId ? string.Empty : _objects.GetPath(id);

    [EngineRoutine(0x00413a60)]
    internal void SetChildAndLink(SpriteDefinition definition, GameLogger? logger)
    {
        int link = (int)definition.Object.Link;
        definition.LinkDefinition = Find(definition.Object.Link);
        ref readonly var weapon = ref definition.Weapon;
        definition.HasKeyframes = GetHasKeyframes(weapon);
        definition.HasScaleKeyframes = GetHasScaleKeyframes(weapon);
        definition.HasOffsetKeyframes = GetHasOffsetKeyframes(weapon);

        if (definition.Lifetime != SpriteDefinition.Unlimited || definition.HasKeyframes || definition.Object.Properties.HasFlag(ObjectProperties.Blur) || definition.Object.Properties.HasFlag(ObjectProperties.RandomSpeed))
        {
            definition.HasTimers = true;
        }

        foreach (int child in definition.Children)
        {
            if (child != 0 && Find((KnownObject)Math.Abs(child))?.IsBirthAsSmoke() == true)
            {
                definition.HasTimers = true;
            }
        }

        if (logger is null)
            return;

        if (link != 0 && !IsDefined(link))
        {
            definition.Report(logger, LogError.Invalid, link, "LinkVid"u8);
        }

        foreach (int child in definition.Object.Children)
        {
            if (child != 0 && !IsDefined(Math.Abs(child)))
            {
                definition.Report(logger, LogError.Invalid, child, "child"u8);
            }
        }
    }

    private static bool IsScaleKey(in WeaponRecord weapon, int key) =>
        BitConverter.SingleToInt32Bits(weapon.ScaleXKeys[key]) != OneBits || BitConverter.SingleToInt32Bits(weapon.ScaleYKeys[key]) != OneBits || BitConverter.SingleToInt32Bits(weapon.ScaleHeightKeys[key]) != OneBits;

    private static bool IsOffsetKey(in WeaponRecord weapon, int key) =>
        !weapon.OffsetXKeys[key].IsExactlyZero() || !weapon.OffsetYKeys[key].IsExactlyZero() || !weapon.OffsetZKeys[key].IsExactlyZero();

    private static bool GetHasScaleKeyframes(in WeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (IsScaleKey(weapon, key))
                return true;
        }

        return false;
    }

    private static bool GetHasOffsetKeyframes(in WeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (IsOffsetKey(weapon, key))
                return true;
        }

        return false;
    }

    private static bool GetHasKeyframes(in WeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (weapon.RedKeys[key] != 0 || weapon.GreenKeys[key] != 0 || weapon.BlueKeys[key] != 0 || weapon.AlphaKeys[key] != 0 || IsScaleKey(weapon, key) || IsOffsetKey(weapon, key) ||
                weapon.RotationXKeys[key] != 0 || weapon.RotationYKeys[key] != 0 || weapon.RotationZKeys[key] != 0)
                return true;
        }

        return false;
    }

    [EngineRoutine(0x00413a30)]
    private bool IsDefined(int id) => id >= 0 && id < _definitions.Length && _definitions[id] is not null;
}
