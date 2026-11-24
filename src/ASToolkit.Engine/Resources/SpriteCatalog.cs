using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using System.Text;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of the object IDs of a game folder, with the changes the level script makes to them.
/// </summary>
public sealed class SpriteCatalog
{
    /// <summary>
    /// Represents the deepest chain of child launches.
    /// </summary>
    public const int MaxLaunchDepth = 3;

    /// <summary>
    /// Represents the object ID of the prerendered ground.
    /// </summary>
    public const KnownObject HardwareGroundId = (KnownObject)1024;

    /// <summary>
    /// Represents the default largest texture width and height.
    /// </summary>
    public const int DefaultMaxTextureSize = 32768;
    internal const int DefaultSpriteSpeed = 71;
    private const int WeaponRecordSize = 612;
    private const int VidSize = 1156;
    private const int OneBits = 0x3f800000;
    private const int MaxId = 0x7ff;
    private const string FontExtension = ".fon";
    private const string TrueTypeExtension = ".ttf";
    private const string AnimationExtension = ".flc";
    private const string HardwareGroundName = "Self Created Hardware Prerendered Ground ";

    private static readonly ConcurrentDictionary<(string Path, bool IsText), Lazy<ReadOnlyMemory<byte>>> ConvertedPictures = new();

    private static readonly Vector3 DefaultSize = new(24, 16, 20);
    private readonly MyError? _error;
    private readonly List<Vid> _loadOrder = [];
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();

    private ObjectTable _objects;
    private Vid?[] _vids;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteCatalog"/> class with the object IDs of a game folder.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="session">The session that logs the loading errors and summaries and gets a sound system if it doesn't have one, or <see langword="null"/> to load silently.</param>
    /// <param name="isTrueColor"><see langword="true"/> for a 32-bit back buffer, where only built terrain uses <see cref="VidHighColor"/>; <see langword="false"/> for a 16-bit back buffer, where every software sprite uses it.</param>
    /// <param name="format">The channel layout of 16-bit colors, or <see langword="null"/> for 5-6-5.</param>
    /// <param name="maxTextureSize">The largest texture width and height the device accepts.</param>
    /// <param name="resetBuiltTerrain"><see langword="true"/> to load object IDs of the built terrain class as terrain; otherwise, <see langword="false"/>.</param>
    [EngineRoutine(0x00411510)]
    public SpriteCatalog(string gameFolder, ObjectTable objects, GameSession? session = null, bool isTrueColor = true, HighColorFormat? format = null, int maxTextureSize = DefaultMaxTextureSize, bool resetBuiltTerrain = false)
    {
        _objects = objects;
        ShouldResetBuiltTerrain = resetBuiltTerrain || session?.HasAppMenu == true;
        var error = session?.Error;
        _error = error;
        session?.Sound ??= new Sound(objects.Sounds, new GameFolder(gameFolder), error, isHighQuality: session.IsSoundHighQuality);

        int start = Environment.TickCount;
        if (objects.WeaponCount != 0)
        {
            error?.Write($"LoadWeapon::No={objects.WeaponCount,-5}             sizeof(WEAPON)={WeaponRecordSize,-4}");
        }

        Parts = new SpritePartReader(gameFolder, objects);
        Empty = CreateDefault();
        int length = objects.Ids.Max(id => (int)id) + 1;
        _vids = new Vid?[length];
        int count = 0;
        bool isAddition = count != 0;
        foreach (var id in objects.Ids)
        {
            if (id < 0 || !objects.TryFind(id, out var gameObject))
                continue;

            if ((int)id > MaxId)
            {
                error?.Error(Map.MapLogTag, LogError.Invalid, 0);
                throw new InvalidDataException($"The object ID {(int)id} is past the end of the VID table.");
            }

            var previous = id == 0 ? LoadingScreen : _vids[(int)id];
            if (previous is not null)
            {
                _loadOrder.Remove(previous);
                error?.Error(Map.MapLogTag, LogError.Load, 0);
            }

            var vid = CreateVid(gameFolder, id, gameObject, isTrueColor, format ?? HighColorFormat.Rgb565, maxTextureSize, error, session?.Sound);
            if (vid is null)
                continue;

            count = Math.Max(count, (int)id + 1);
            if (isAddition)
            {
                vid.FileType |= VidType.FrameVector;
            }

            _loadOrder.Add(vid);
            if (gameObject.WeaponIndex >= objects.WeaponCount)
            {
                vid.Report(error, LogError.Text, gameObject.WeaponIndex, "nWeapon > noWeapon"u8);
            }

            if (id == 0)
            {
                LoadingScreen = vid;
                continue;
            }

            _vids[(int)id] = vid;
        }

        ResolveLinks(error, false);
        var largest = Size.Empty;
        foreach (var vid in _vids.Prepend(LoadingScreen))
        {
            if (vid is not null)
            {
                largest = new Size(Math.Max(largest.Width, vid.FrameSize.Width), Math.Max(largest.Height, vid.FrameSize.Height));
            }
        }

        error?.Write($"LoadVid::No   ={count,-15}   sizeof(VID)   ={VidSize,-5}    load time     ={Environment.TickCount - start}ms   MaxSizeX,Y={largest.Width},{largest.Height}");
    }

    /// <summary>
    /// Gets a value indicating whether object IDs of the built terrain class are loaded as terrain.
    /// </summary>
    public bool ShouldResetBuiltTerrain { get; }

    /// <summary>
    /// Gets the sprite parts of the object IDs.
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
    public float Gravitation => _objects.Gravitation * Vid.PerMillisecondSquared;

    /// <summary>
    /// Gets the second gravity in pixels per millisecond squared.
    /// </summary>
    public float Gravitation2 => _objects.Gravitation2 * Vid.PerMillisecondSquared;

    /// <summary>
    /// Gets the sounds of the object table.
    /// </summary>
    public IReadOnlyList<SfxRecord> Sounds => _objects.Sounds;

    /// <summary>
    /// Gets the fastest scroll speed of the camera along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed => _objects.ScrollSpeed * Vid.PerMillisecond;

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
    /// Gets the shared data, without frames, of the cursor and goal sprites.
    /// </summary>
    public Vid Empty { get; }

    /// <summary>
    /// Gets the shared data and frames of the loading screen, in the first slot of the table.
    /// </summary>
    public Vid? LoadingScreen { get; private set; }

    internal ReadOnlySpan<Vid?> Table => _vids;

    internal IReadOnlyList<Vid> LoadOrder => _loadOrder;

    private static ReadOnlySpan<byte> ObjectChunkTag => "OBJ "u8;

    /// <summary>
    /// Returns the name of an object ID's record.
    /// </summary>
    /// <param name="vid">The shared data of the object ID.</param>
    /// <returns>The name.</returns>
    public string GetName(Vid vid) => vid.RecordId == HardwareGroundId ? HardwareGroundName : _objects.GetName(vid.RecordId);

    /// <summary>
    /// Exchanges the identities of two object IDs.
    /// </summary>
    /// <param name="first">The shared data of the first object ID.</param>
    /// <param name="second">The shared data of the second object ID.</param>
    [EngineRoutine(0x00411790)]
    public void Exchange(Vid first, Vid second)
    {
        if (first == second)
            return;

        foreach (var vid in _vids)
        {
            if (vid is null)
                continue;

            if (vid.LinkVid == first)
            {
                vid.LinkVid = second;
            }
            else if (vid.LinkVid == second)
            {
                vid.LinkVid = first;
            }
        }

        _vids[(int)first.Id] = second;
        _vids[(int)second.Id] = first;
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
    /// Undoes the level script's exchanges and reloads the record of every object ID.
    /// </summary>
    public void ReloadRecords() => ReloadRecords(null, null);

    /// <summary>
    /// Clears the counts and script handlers of every object ID for a new level.
    /// </summary>
    public void ResetLevelState()
    {
        foreach (var vid in _vids)
        {
            vid?.ResetLevelState();
        }
    }

    /// <summary>
    /// Returns the shared data of an object ID.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <returns>The shared data of the object ID, or null if the ID isn't loaded.</returns>
    public Vid? Find(KnownObject id) => id >= 0 && (int)id < _vids.Length ? _vids[(int)id] : null;

    /// <summary>
    /// Puts shared data in the slot of an object ID, and grows the table when the ID is past its end.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="vid">The shared data of the object ID.</param>
    public void SetVid(KnownObject id, Vid vid)
    {
        if (_vids.Length <= (int)id)
        {
            Array.Resize(ref _vids, (int)id + 1);
        }

        _vids[(int)id] = vid;
    }

    /// <summary>
    /// Applies an ambient gamma to the palettes of every sprite file.
    /// </summary>
    /// <param name="ambient">The ambient gamma.</param>
    public void SetAmbient(Gamma ambient)
    {
        foreach (var vid in _vids)
        {
            vid?.SetGamma(ambient, Vid.AllArmies, ambient, _error);
        }
    }

    internal void ReleaseAdditionalLoads()
    {
        for (int i = _vids.Length - 1; i >= 0; i--)
        {
            var vid = _vids[i];

            if (vid is not null && vid.FileType.HasFlag(VidType.FrameVector))
            {
                _loadOrder.Remove(vid);
                _vids[i] = null;
            }
        }

        int count = _vids.Length;
        while (count > 0 && _vids[count - 1] is null)
        {
            count--;
        }

        Array.Resize(ref _vids, count);
    }

    internal FrameMask? GetFrameMask(Vid vid, int frame)
    {
        if (_masks.TryGetValue((vid.Id, frame), out var mask))
            return mask;

        VidResource? file;
        lock (Parts)
        {
            file = Parts.GetFile(GetPath(vid.Id));
        }

        mask = file is null || frame < 0 || frame >= file.Value.FrameCount ? null : FrameMask.Read(file.Value, frame);

        return _masks.GetOrAdd((vid.Id, frame), mask);
    }

    [EngineRoutine(0x00411920)]
    internal void ReloadRecords(MyError? error, Sound? sound)
    {
        foreach (var vid in _vids)
        {
            if (vid is not null && vid.Identity != vid)
            {
                Exchange(vid.Identity, vid);
            }
        }

        ResContainer container;
        try
        {
            container = new ResContainer(File.ReadAllBytes(_objects.Path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error?.Error(Map.MapLogTag, LogError.Open, 0);
            return;
        }

        if (!container.TryGetChunk(ObjectChunkTag, out _))
        {
            error?.Error(Map.MapLogTag, LogError.Section, 0);
            return;
        }

        _objects = ObjectTable.Load(_objects.Path);

        foreach (var id in _objects.Ids)
        {
            if ((int)id > MaxId)
            {
                error?.Error(Map.MapLogTag, LogError.Invalid, 0);
                continue;
            }

            var slot = id == 0 ? LoadingScreen : _vids[(int)id];

            if (slot is null)
                continue;

            var target = slot.Identity;
            _objects.TryFind(id, out var gameObject);
            _objects.TryGetWeapon(gameObject, out var weapon);
            target.ReplaceRecord(gameObject, weapon);
            target.Name = GetName(target);
            target.LoadParameters(error, sound, ShouldResetBuiltTerrain);
        }

        ResolveLinks(error, true);
    }

    internal void LogCounts(MyError error)
    {
        for (int i = 0; i < _vids.Length; i++)
        {
            var vid = _vids[i];

            if (vid is null)
                continue;

            var counts = vid.Counts;
            if (counts[0] + counts[1] + counts[2] + counts[3] == 0)
                continue;

            error.Write($"NoVid[{i,3}]={counts[0]} {counts[1]} {counts[2]} {counts[3]} {GetName(vid)} Layer={(int)vid.Layer} {GetPath(vid)}");
        }
    }

    [EngineRoutine(0x00413a60)]
    internal void SetChildAndLink(Vid vid, MyError? error)
    {
        int link = (int)vid.Object.Link;
        if (link != 0)
        {
            var linked = Find(vid.Object.Link);
            if (linked is not null)
            {
                vid.LinkVid = linked;
            }
        }

        ref readonly var weapon = ref vid.Weapon;
        vid.HasColorKeyframes = GetHasColorKeyframes(weapon);
        vid.HasKeyframes = GetHasKeyframes(weapon);
        vid.HasScaleKeyframes = GetHasScaleKeyframes(weapon);
        vid.HasOffsetKeyframes = GetHasOffsetKeyframes(weapon);

        if (vid.Lifetime != Vid.Unlimited || vid.HasKeyframes || vid.Object.Properties.HasFlag(ObjectProperties.Blur) || vid.Object.Properties.HasFlag(ObjectProperties.RandomSpeed))
        {
            vid.HasTimers = true;
        }

        foreach (int child in vid.Children)
        {
            if (child != 0 && Find((KnownObject)Math.Abs(child))?.IsBirthAsSmoke() == true)
            {
                vid.HasTimers = true;
            }
        }

        if (error is null)
            return;

        if (link != 0 && !IsDefined(link))
        {
            vid.Report(error, LogError.Invalid, link, "LinkVid"u8);
        }

        foreach (int child in vid.Object.Children)
        {
            if (child != 0 && !IsDefined(Math.Abs(child)))
            {
                vid.Report(error, LogError.Invalid, child, "child"u8);
            }
        }
    }

    private static void LogUnopened(MyError? error, string path) => error?.Write($"LOAD::Can't open file {path}");

    private static Vid CreateDefault() => Vid.CreateEmpty(KnownObject.None, new GameObject(SpriteClass.Primitive, DefaultSize), DefaultSpriteSpeed, ObjectLayer.Menu);

    private static ReadOnlyMemory<byte> ConvertPicture(string gameFolder, string path, bool isText, MyError? error)
    {
        using var picture = isText ? new FontPicture(gameFolder, error) : new Picture(gameFolder, error);
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
    private Vid? CreateVid(string gameFolder, KnownObject id, in GameObject gameObject, bool isTrueColor, HighColorFormat format, int maxTextureSize, MyError? error, Sound? sound)
    {
        _objects.TryGetWeapon(gameObject, out var weapon);
        string path = _objects.GetPath(id);
        var temporary = Vid.CreateEmpty(KnownObject.None, gameObject, DefaultSpriteSpeed, ObjectLayer.Menu);
        temporary.LoadParameters(error, sound, ShouldResetBuiltTerrain);
        bool isCopy = FindCopySource(path, temporary) is not null;
        var parametersError = error;
        if (isCopy)
        {
            error = null;
        }

        var file = Parts.GetFile(path);
        ReadOnlyMemory<byte> data = default;
        if (file is null)
        {
            if (path.Contains(FontExtension, StringComparison.Ordinal) || path.Contains(TrueTypeExtension, StringComparison.Ordinal))
            {
                var font = new VidFont(id, gameObject, weapon, maxTextureSize);
                Prepare(font, parametersError, sound);
                font.Load(default, default, error);
                font.SetLayer();
                return font;
            }
            else if (path.Contains(TgaImage.Extension, StringComparison.Ordinal) || path.Contains(BmpImage.Extension, StringComparison.Ordinal) || path.Contains(AnimationExtension, StringComparison.Ordinal))
            {
                bool isText = gameObject.SpriteClass == SpriteClass.Text;
                data = error is not null
                    ? ConvertPicture(gameFolder, path, isText, error)
                    : ConvertedPictures.GetOrAdd((Path.GetFullPath(Path.Combine(gameFolder, path)), isText), key => new(() => ConvertPicture(gameFolder, path, key.IsText, null))).Value;
                if (data.IsEmpty)
                {
                    LogUnopened(error, path);
                }
            }
        }

        if (!data.IsEmpty)
        {
            file = new VidResource(data);
        }

        if (file is null)
        {
            if (error is not null)
            {
                new Resource().Open(new GameFolder(gameFolder), error, Encoding.ASCII.GetBytes(path), Resource.AnyTag);
            }

            LogUnopened(error, path);
            return null;
        }

        var sprite = file.Value;

        int totalFrames = data.IsEmpty ? Parts.CountFrames(path) : sprite.FrameCount;
        var type = sprite.Type;
        Vid vid;
        ReadOnlyMemory<byte> bytes = default;
        if (type.HasFlag(VidType.HardwareLayer))
        {
            vid = new VidHardwareLayer(id, gameObject, weapon, sprite, totalFrames);
        }
        else if (type.HasFlag(VidType.Light))
        {
            vid = new VidLight(id, gameObject, weapon, sprite, totalFrames);
        }
        else if (type.HasFlag(VidType.Hardware) && !(type.HasFlag(VidType.Blended) && type.HasFlag(VidType.ZBuffer)))
        {
            vid = new VidHardware(id, gameObject, weapon, sprite, totalFrames, format);
        }
        else
        {
            bytes = data.IsEmpty ? GetBytes(path) : data;
            if (type.HasFlag(VidType.Hardware))
            {
                vid = new VidDepthFaded(id, gameObject, weapon, sprite, totalFrames, format);
            }
            else if (gameObject.SpriteClass == SpriteClass.BuiltTerrain || !isTrueColor)
            {
                vid = new VidHighColor(id, gameObject, weapon, sprite, totalFrames, format);
            }
            else
            {
                vid = new VidSoftware(id, gameObject, weapon, sprite, totalFrames, format);
            }
        }

        Prepare(vid, parametersError, sound);
        vid.Load(sprite, bytes, error);
        vid.SetLayer();
        vid.IsCopy = isCopy;
        return vid;
    }

    private Vid? FindCopySource(string path, Vid temporary)
    {
        bool isBuiltTerrain = temporary.Class == SpriteClass.BuiltTerrain;
        var table = _vids;
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

    private void Prepare(Vid vid, MyError? error, Sound? sound)
    {
        vid.Name = GetName(vid);
        vid.Path = GetPath(vid);
        vid.LoadParameters(error, sound, ShouldResetBuiltTerrain);
    }

    private void ResolveLinks(MyError? error, bool isReload)
    {
        foreach (var vid in _vids)
        {
            if (vid is not null && !(isReload && vid.FileType.HasFlag(VidType.FrameVector)))
            {
                SetChildAndLink(vid, error);
            }
        }
    }

    private string GetPath(Vid vid) => GetPath(vid.RecordId);

    private string GetPath(KnownObject id) => id == HardwareGroundId ? string.Empty : _objects.GetPath(id);

    private bool IsDefined(int id) => id >= 0 && id < _vids.Length && _vids[id] is not null;
}
