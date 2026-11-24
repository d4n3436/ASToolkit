using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using System.Text;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

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
    /// Represents the ID of the definition of the prerendered ground.
    /// </summary>
    public const KnownObject HardwareGroundId = (KnownObject)1024;

    /// <summary>
    /// Represents the default largest texture width and height.
    /// </summary>
    public const int DefaultMaxTextureSize = 32768;
    internal const int DefaultSpriteSpeed = 71;
    private const int WeaponRecordSize = 612;
    private const int DefinitionSize = 1156;
    private const int OneBits = 0x3f800000;
    private const int MaxId = 0x7ff;
    private const string FontExtension = ".fon";
    private const string TrueTypeExtension = ".ttf";
    private const string AnimationExtension = ".flc";
    private const string HardwareGroundName = "Self Created Hardware Prerendered Ground ";

    private static readonly ConcurrentDictionary<(string Path, bool IsText), Lazy<ReadOnlyMemory<byte>>> ConvertedPictures = new();

    private static readonly Vector3 DefaultSize = new(24, 16, 20);
    private readonly GameLogger? _logger;
    private readonly List<SpriteDefinition> _loadOrder = [];
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();

    private ObjectTable _objects;
    private SpriteDefinition?[] _definitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteCatalog"/> class with the definitions of a game folder.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="session">The session that logs the loading errors and summaries and gets a sound system if it doesn't have one, or <see langword="null"/> to load silently.</param>
    /// <param name="isTrueColor"><see langword="true"/> for a 32-bit back buffer, where only built terrain uses <see cref="HighColorSpriteDefinition"/>; <see langword="false"/> for a 16-bit back buffer, where every software sprite uses it.</param>
    /// <param name="format">The channel layout of 16-bit colors, or <see langword="null"/> for 5-6-5.</param>
    /// <param name="maxTextureSize">The largest texture width and height the device accepts.</param>
    /// <param name="resetBuiltTerrain"><see langword="true"/> to load definitions of the built terrain class as terrain; otherwise, <see langword="false"/>.</param>
    [EngineRoutine(0x00411510)]
    public SpriteCatalog(string gameFolder, ObjectTable objects, GameSession? session = null, bool isTrueColor = true, HighColorFormat? format = null, int maxTextureSize = DefaultMaxTextureSize, bool resetBuiltTerrain = false)
    {
        _objects = objects;
        ShouldResetBuiltTerrain = resetBuiltTerrain || session?.HasAppMenu == true;
        var logger = session?.Logger;
        _logger = logger;
        session?.Sound ??= new SoundSystem(objects.Sounds, new GameFolder(gameFolder), logger, isHighQuality: session.IsSoundHighQuality);

        int start = Environment.TickCount;
        if (objects.WeaponCount != 0)
        {
            logger?.Write($"LoadWeapon::No={objects.WeaponCount,-5}             sizeof(WEAPON)={WeaponRecordSize,-4}");
        }

        Parts = new SpritePartReader(gameFolder, objects);
        Empty = CreateDefault();
        int length = objects.Ids.Max(id => (int)id) + 1;
        _definitions = new SpriteDefinition?[length];
        int count = 0;
        bool isAddition = count != 0;
        foreach (var id in objects.Ids)
        {
            if (id < 0 || !objects.TryFind(id, out var gameObject))
                continue;

            if ((int)id > MaxId)
            {
                logger?.Error(Map.MapLogTag, LogError.Invalid, 0);
                throw new InvalidDataException($"The object ID {(int)id} is past the end of the definition table.");
            }

            var previous = id == 0 ? LoadingScreen : _definitions[(int)id];
            if (previous is not null)
            {
                _loadOrder.Remove(previous);
                logger?.Error(Map.MapLogTag, LogError.Load, 0);
            }

            var definition = CreateVid(gameFolder, id, gameObject, isTrueColor, format ?? HighColorFormat.Rgb565, maxTextureSize, logger, session?.Sound);
            if (definition is null)
                continue;

            count = Math.Max(count, (int)id + 1);
            if (isAddition)
            {
                definition.FileType |= VidType.FrameVector;
            }

            _loadOrder.Add(definition);
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

        logger?.Write($"LoadVid::No   ={count,-15}   sizeof(VID)   ={DefinitionSize,-5}    load time     ={Environment.TickCount - start}ms   MaxSizeX,Y={largest.Width},{largest.Height}");
    }

    /// <summary>
    /// Gets a value indicating whether definitions of the built terrain class are loaded as terrain.
    /// </summary>
    public bool ShouldResetBuiltTerrain { get; }

    /// <summary>
    /// Gets the sprite parts of the definitions.
    /// </summary>
    public SpritePartReader Parts { get; }

    /// <summary>
    /// Gets the first weapon record of the object table.
    /// </summary>
    public WeaponRecord FirstWeapon
    {
        get
        {
            _objects.TryGetWeapon(default, out var weapon);
            return weapon;
        }
    }

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
    /// Gets the packed gamma applied to the player's sprite, or zero if there isn't one.
    /// </summary>
    public uint SelectedGamma => _objects.SelectedGamma;

    /// <summary>
    /// Gets the packed gamma applied to an enemy under the cursor, or zero if there isn't one.
    /// </summary>
    public uint HoveredEnemyGamma => _objects.HoveredEnemyGamma;

    /// <summary>
    /// Gets the packed gamma applied to a sprite under the cursor, or zero if there isn't one.
    /// </summary>
    public uint HoveredGamma => _objects.HoveredGamma;

    /// <summary>
    /// Gets the empty definition of the cursor and goal sprites.
    /// </summary>
    public SpriteDefinition Empty { get; }

    /// <summary>
    /// Gets the definition of the loading screen, in the first slot of the table.
    /// </summary>
    public SpriteDefinition? LoadingScreen { get; private set; }

    internal ReadOnlySpan<SpriteDefinition?> Table => _definitions;

    internal IReadOnlyList<SpriteDefinition> LoadOrder => _loadOrder;

    private static ReadOnlySpan<byte> ObjectChunkTag => "OBJ "u8;

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
        }
    }

    /// <summary>
    /// Returns the definition of an object ID.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <returns>The definition, or null if no definition has that ID.</returns>
    public SpriteDefinition? Find(KnownObject id) => id >= 0 && (int)id < _definitions.Length ? _definitions[(int)id] : null;

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
    /// Applies an ambient gamma to the palettes of every sprite file.
    /// </summary>
    /// <param name="ambient">The ambient gamma.</param>
    public void SetAmbient(Gamma ambient)
    {
        foreach (var definition in _definitions)
        {
            definition?.SetGamma(ambient, SpriteDefinition.AllArmies, ambient, _logger);
        }
    }

    internal void ReleaseAdditionalLoads()
    {
        for (int i = _definitions.Length - 1; i >= 0; i--)
        {
            var definition = _definitions[i];

            if (definition is not null && definition.FileType.HasFlag(VidType.FrameVector))
            {
                _loadOrder.Remove(definition);
                _definitions[i] = null;
            }
        }

        int count = _definitions.Length;
        while (count > 0 && _definitions[count - 1] is null)
        {
            count--;
        }

        Array.Resize(ref _definitions, count);
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

        ResContainer container;
        try
        {
            container = new ResContainer(File.ReadAllBytes(_objects.Path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.Error(Map.MapLogTag, LogError.Open, 0);
            return;
        }

        if (!container.TryGetChunk(ObjectChunkTag, out _))
        {
            logger?.Error(Map.MapLogTag, LogError.Section, 0);
            return;
        }

        _objects = ObjectTable.Load(_objects.Path);

        foreach (var id in _objects.Ids)
        {
            if ((int)id > MaxId)
            {
                logger?.Error(Map.MapLogTag, LogError.Invalid, 0);
                continue;
            }

            var slot = id == 0 ? LoadingScreen : _definitions[(int)id];

            if (slot is null)
                continue;

            var target = slot.Identity;
            _objects.TryFind(id, out var gameObject);
            _objects.TryGetWeapon(gameObject, out var weapon);
            target.ReplaceRecord(gameObject, weapon);
            target.Name = GetName(target);
            target.LoadParameters(logger, sound, ShouldResetBuiltTerrain);
        }

        ResolveLinks(logger, true);
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

            logger.Write($"NoVid[{i,3}]={counts[0]} {counts[1]} {counts[2]} {counts[3]} {GetName(definition)} Layer={(int)definition.Layer} {GetPath(definition)}");
        }
    }

    [EngineRoutine(0x00413a60)]
    internal void SetChildAndLink(SpriteDefinition definition, GameLogger? logger)
    {
        int link = (int)definition.Object.Link;
        if (link != 0)
        {
            var linked = Find(definition.Object.Link);
            if (linked is not null)
            {
                definition.LinkDefinition = linked;
            }
        }

        ref readonly var weapon = ref definition.Weapon;
        definition.HasColorKeyframes = GetHasColorKeyframes(weapon);
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

    private static void LogUnopened(GameLogger? logger, string path) => logger?.Write($"LOAD::Can't open file {path}");

    private static SpriteDefinition CreateDefault() => SpriteDefinition.CreateEmpty(KnownObject.None, new GameObject(SpriteClass.Primitive, DefaultSize), DefaultSpriteSpeed, ObjectLayer.Menu);

    private static ReadOnlyMemory<byte> ConvertPicture(string gameFolder, string path, bool isText, GameLogger? logger)
    {
        using var picture = isText ? new FontPicture(gameFolder, logger) : new Picture(gameFolder, logger);
        return picture.Load(path, string.Empty, string.Empty) ? picture.Convert() : default;
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

    private static bool GetHasColorKeyframes(in WeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (weapon.RedKeys[key] != 0 || weapon.GreenKeys[key] != 0 || weapon.BlueKeys[key] != 0 || weapon.AlphaKeys[key] != 0)
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
        var temporary = SpriteDefinition.CreateEmpty(KnownObject.None, gameObject, DefaultSpriteSpeed, ObjectLayer.Menu);
        temporary.LoadParameters(logger, sound, ShouldResetBuiltTerrain);
        bool isCopy = FindCopySource(path, temporary) is not null;
        var parametersLogger = logger;
        if (isCopy)
        {
            logger = null;
        }

        var file = Parts.GetFile(path);
        ReadOnlyMemory<byte> data = default;
        if (file is null)
        {
            if (path.Contains(FontExtension, StringComparison.Ordinal) || path.Contains(TrueTypeExtension, StringComparison.Ordinal))
            {
                var font = new FontSpriteDefinition(id, gameObject, weapon, maxTextureSize);
                Prepare(font, parametersLogger, sound);
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

        if (file is null)
        {
            if (logger is not null)
            {
                new Resource().Open(new GameFolder(gameFolder), logger, Encoding.ASCII.GetBytes(path), Resource.AnyTag);
            }

            LogUnopened(logger, path);
            return null;
        }

        var sprite = file.Value;

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
            definition = new HardwareSpriteDefinition(id, gameObject, weapon, sprite, totalFrames, format);
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

        Prepare(definition, parametersLogger, sound);
        definition.Load(sprite, bytes, logger);
        definition.SetLayer();
        definition.IsCopy = isCopy;
        return definition;
    }

    private SpriteDefinition? FindCopySource(string path, SpriteDefinition temporary)
    {
        bool isBuiltTerrain = temporary.Class == SpriteClass.BuiltTerrain;
        var table = _definitions;
        for (int slot = 0; slot < table.Length; slot++)
        {
            var existing = slot == 0 ? LoadingScreen : table[slot];
            if (existing is null || !string.Equals(existing.Path, path, StringComparison.Ordinal))
                continue;

            if (isBuiltTerrain != (existing.Class == SpriteClass.BuiltTerrain))
                continue;

            bool isHardware = existing.FileType.HasFlag(VidType.Hardware);
            if ((isHardware || existing.ObjectGamma == temporary.ObjectGamma) && (isHardware || existing.Object.Properties.HasFlag(ObjectProperties.Gamma) == temporary.Object.Properties.HasFlag(ObjectProperties.Gamma)))
                return existing;
        }

        return null;
    }

    private void Prepare(SpriteDefinition definition, GameLogger? logger, SoundSystem? sound)
    {
        definition.Name = GetName(definition);
        definition.Path = GetPath(definition);
        definition.LoadParameters(logger, sound, ShouldResetBuiltTerrain);
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

    private bool IsDefined(int id) => id >= 0 && id < _definitions.Length && _definitions[id] is not null;
}
