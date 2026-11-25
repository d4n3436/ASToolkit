using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
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
    private const int UnlimitedBits = 0x497423f0;
    private const float KeyScale = 0.001f;
    private const int VidSize = 1156;
    private const int OneBits = 0x3f800000;
    private const int MaxId = 0x7ff;
    private const int VidTableSize = MaxId + 1;
    private const string FontExtension = ".fon";
    private const string TrueTypeExtension = ".ttf";
    private const string AnimationExtension = ".flc";
    private const string HardwareGroundName = "Self Created Hardware Prerendered Ground ";

    private static readonly ConcurrentDictionary<(string Path, bool IsText), Lazy<ReadOnlyMemory<byte>>> ConvertedPictures = new();
    
    private readonly ConcurrentDictionary<(KnownObject Id, int Frame), FrameMask?> _masks = new();

    /// <summary>
    /// Gets a value indicating whether object IDs of the built terrain class are loaded as terrain.
    /// </summary>
    public bool ShouldResetBuiltTerrain => Session.HasAppMenu;

    /// <summary>
    /// Gets the sprite parts of the object IDs.
    /// </summary>
    public SpritePartReader Parts { get; }

    /// <summary>
    /// Gets the shared data, without frames, of the cursor and goal sprites.
    /// </summary>
    public Vid EmptyVid { get; } = ASToolkit.Engine.Vid.CreateDefault();

    internal ReadOnlySpan<Vid?> Vids => _vids.AsSpan(0, _vidCount);

    private static ReadOnlySpan<byte> ObjectChunkTag => "OBJ "u8;

    /// <summary>
    /// Returns the name of an object ID's record.
    /// </summary>
    /// <param name="vid">The shared data of the object ID.</param>
    /// <returns>The name.</returns>
    public string GetVidName(Vid vid) => vid.RecordId == HardwareGroundId ? HardwareGroundName : _objects.GetName(vid.RecordId);

    [EngineRoutine(0x00411510)]
    private void LoadVid(ref Resource resource)
    {
        int start = Environment.TickCount;
        if (_weapons is null)
        {
            LoadWeaponRecords(ref resource);
            if (_weaponCount != 0)
            {
                Session.Error.Write($"LoadWeapon::No={_weaponCount,-5}             sizeof(WEAPON)={WeaponRecordSize,-4}");
            }
        }

        if (resource.GoBegin(ObjectChunkTag) != Resource.Found)
        {
            Session.Error.Error(MapLogTag, LogError.Section, 0, "load 'VID'"u8);
            return;
        }

        int existing = _vidCount;
        var sound = Sound.IsLoaded ? Sound : null;
        var screen = Session.Graphics.Screen;
        bool isTrueColor = screen?.IsTrueColor ?? true;
        var format = screen?.Format ?? HighColorFormat.Rgb565;
        do
        {
            int id = resource.Read<int>();
            if (id > MaxId)
            {
                Session.Error.Error(MapLogTag, LogError.Invalid, id, "nvid > MAX_VID"u8);
                throw new InvalidDataException($"The object ID {id} is past the end of the VID table.");
            }

            if (_vids[id] is not null)
            {
                _vids[id]!.Destruct(Session);
                _vids[id] = null;
                Session.Error.Error(MapLogTag, LogError.Load, id, "this VID already loaded"u8);
            }

            var vid = CreateVid(ref resource, (KnownObject)id, isTrueColor, format, sound);
            _vids[id] = vid;
            if (vid is null)
                continue;

            if (_vidCount <= id)
            {
                _vidCount = id + 1;
            }

            if (existing != 0)
            {
                vid.FileType |= VidType.FrameVector;
            }

            if (vid.WeaponIndex >= _weaponCount)
            {
                vid.Report(Session.Error, LogError.Text, vid.WeaponIndex, "nWeapon > noWeapon"u8);
            }

            Session.Graphics.DrawLoadBar(this, _vids[0]);
        }
        while (resource.GoNextSub(ObjectChunkTag) == Resource.Found);

        var largest = System.Drawing.Size.Empty;
        foreach (var vid in Vids)
        {
            if (vid is not null)
            {
                SetChildAndLink(vid);
                largest = new System.Drawing.Size(Math.Max(largest.Width, vid.FrameSize.Width), Math.Max(largest.Height, vid.FrameSize.Height));
            }
        }

        Session.Error.Write($"LoadVid::No   ={_vidCount,-15}   sizeof(VID)   ={VidSize,-5}    load time     ={Environment.TickCount - start}ms   MaxSizeX,Y={largest.Width},{largest.Height}");
    }

    /// <summary>
    /// Exchanges the identities of two object IDs.
    /// </summary>
    /// <param name="first">The shared data of the first object ID.</param>
    /// <param name="second">The shared data of the second object ID.</param>
    [EngineRoutine(0x00411790)]
    public void ExchangeVid(Vid first, Vid second)
    {
        if (first == second)
            return;

        foreach (var vid in Vids)
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

            var children = vid.ChildVids;
            for (int state = 0; state < children.Length; state++)
            {
                if (children[state] == first)
                {
                    children[state] = second;
                }
                else if (children[state] == second)
                {
                    children[state] = first;
                }
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
    /// Clears the counts and script handlers of every object ID for a new level.
    /// </summary>
    public void ResetVidLevelState()
    {
        foreach (var vid in Vids)
        {
            vid?.ResetLevelState();
        }
    }

    /// <summary>
    /// Returns the shared data of an object ID.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <returns>The shared data of the object ID, or null if the ID isn't loaded.</returns>
    public Vid? FindVid(KnownObject id) => id >= 0 && (int)id < _vidCount ? _vids[(int)id] : null;

    /// <summary>
    /// Applies an ambient gamma to the palettes of every sprite file.
    /// </summary>
    /// <param name="ambient">The ambient gamma.</param>
    public void SetAmbient(Gamma ambient)
    {
        foreach (var vid in Vids)
        {
            vid?.SetGamma(ambient, ASToolkit.Engine.Vid.AllArmies, ambient, Session);
        }
    }

    internal FrameMask? GetFrameMask(Vid vid, int frame)
    {
        if (_masks.TryGetValue((vid.Id, frame), out var mask))
            return mask;

        VidResource? file;
        lock (Parts)
        {
            file = Parts.GetFile(vid.Path);
        }

        mask = file is null || frame < 0 || frame >= file.Value.FrameCount ? null : FrameMask.Read(file.Value, frame);

        return _masks.GetOrAdd((vid.Id, frame), mask);
    }

    [EngineRoutine(0x00411880)]
    private void LoadWeaponRecords(ref Resource resource)
    {
        _weapons = resource.Load<AlienShooterWeaponRecord>("WEAP"u8, Session.Error);
        _weaponCount = _weapons.Length;
        for (int i = 0; i < _weaponCount; i++)
        {
            ref var weapon = ref _weapons[i];
            for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
            {
                if (BitConverter.SingleToInt32Bits(weapon.SpeedKeys[key]) != UnlimitedBits)
                {
                    weapon.SpeedKeys[key] *= KeyScale;
                }

                if (!weapon.UnknownKeys[key].IsExactly(ASToolkit.Engine.Vid.Unlimited))
                {
                    weapon.UnknownKeys[key] *= KeyScale;
                }
            }
        }
    }

    /// <summary>
    /// Undoes the level script's exchanges and reloads the record of every object ID.
    /// </summary>
    [EngineRoutine(0x00411920)]
    public void ReloadVid()
    {
        foreach (var vid in Vids)
        {
            if (vid is not null && vid.Identity != vid)
            {
                ExchangeVid(vid.Identity, vid);
            }
        }

        ResContainer container;
        try
        {
            container = new ResContainer(File.ReadAllBytes(_objects.Path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Session.Error.Error(MapLogTag, LogError.Open, 0);
            return;
        }

        if (!container.TryGetChunk(ObjectChunkTag, out _))
        {
            Session.Error.Error(MapLogTag, LogError.Section, 0);
            return;
        }

        var objectsFile = new Resource();
        if (OpenObjects(FileSystem, Session.Error, ref objectsFile) == 0)
        {
            LoadWeaponRecords(ref objectsFile);
        }

        _objects = ObjectTable.Load(_objects.Path);

        foreach (var id in _objects.Ids)
        {
            if ((int)id > MaxId)
            {
                Session.Error.Error(MapLogTag, LogError.Invalid, 0);
                continue;
            }

            var slot = (int)id < _vidCount ? _vids[(int)id] : null;

            if (slot is null)
                continue;

            var target = slot.Identity;
            _objects.TryFind(id, out var gameObject);
            target.SetWeapons(_weapons!);
            target.Name = GetVidName(target);
            target.LoadParameters(gameObject, Session.Error, Sound, ShouldResetBuiltTerrain);
        }

        ResolveLinks(true);
    }

    internal void LogCounts()
    {
        for (int i = 0; i < _vidCount; i++)
        {
            var vid = _vids[i];

            if (vid is null)
                continue;

            var counts = vid.Counts;
            if (counts[0] + counts[1] + counts[2] + counts[3] == 0)
                continue;

            Session.Error.Write($"NoVid[{i,3}]={counts[0]} {counts[1]} {counts[2]} {counts[3]} {vid.Name} Layer={(int)vid.Layer} {vid.Path}");
        }
    }

    [EngineRoutine(0x00413a60)]
    internal void SetChildAndLink(Vid vid)
    {
        int link = (int)vid.Link;
        if (link != 0)
        {
            var linked = FindVid(vid.Link);
            if (linked is not null)
            {
                vid.LinkVid = linked.Identity;
            }
        }

        ref readonly var weapon = ref vid.Weapon;
        vid.HasColorKeyframes = GetHasColorKeyframes(weapon);
        vid.HasScaleKeyframes = GetHasScaleKeyframes(weapon);
        vid.HasOffsetKeyframes = GetHasOffsetKeyframes(weapon);
        vid.HasRotationKeyframes = GetHasRotationKeyframes(weapon);

        if (vid.Lifetime != ASToolkit.Engine.Vid.Unlimited || vid.HasKeyframes || vid.Properties.HasFlag(ObjectProperties.Blur) || vid.Properties.HasFlag(ObjectProperties.RandomSpeed))
        {
            vid.HasTimers = true;
        }

        var children = vid.Children;
        for (int state = 0; state < children.Length; state++)
        {
            int child = children[state];
            if (child == 0)
                continue;

            int sign = child >> 31;
            var target = FindVid((KnownObject)unchecked((child ^ sign) - sign));
            if (target is null)
                continue;

            vid.ChildVids[state] = target.Identity;
            if (target.Identity.IsBirthAsSmoke())
            {
                vid.HasTimers = true;
            }
        }

        if (link != 0 && !IsVidDefined(link))
        {
            vid.Report(Session.Error, LogError.Invalid, link, "LinkVid"u8);
        }

        foreach (int child in vid.Children)
        {
            if (child != 0 && !IsVidDefined(Math.Abs(child)))
            {
                vid.Report(Session.Error, LogError.Invalid, child, "child"u8);
            }
        }
    }

    private static void LogUnopened(MyError? error, string path) => error?.Write($"LOAD::Can't open file {path}");

    private static ReadOnlyMemory<byte> ConvertPicture(string gameFolder, string path, bool isText, MyError? error)
    {
        using var picture = isText ? new FontPicture(gameFolder, error) : new Picture(gameFolder, error);
        return picture.Load(path, string.Empty, string.Empty) ? picture.Convert() : default;
    }

    private static bool IsScaleKey(in AlienShooterWeaponRecord weapon, int key) =>
        BitConverter.SingleToInt32Bits(weapon.ScaleXKeys[key]) != OneBits || BitConverter.SingleToInt32Bits(weapon.ScaleYKeys[key]) != OneBits || BitConverter.SingleToInt32Bits(weapon.ScaleHeightKeys[key]) != OneBits;

    private static bool IsOffsetKey(in AlienShooterWeaponRecord weapon, int key) =>
        !weapon.OffsetXKeys[key].IsExactlyZero() || !weapon.OffsetYKeys[key].IsExactlyZero() || !weapon.OffsetZKeys[key].IsExactlyZero();

    private static bool GetHasScaleKeyframes(in AlienShooterWeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (IsScaleKey(weapon, key))
                return true;
        }

        return false;
    }

    private static bool GetHasOffsetKeyframes(in AlienShooterWeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (IsOffsetKey(weapon, key))
                return true;
        }

        return false;
    }

    private static bool GetHasColorKeyframes(in AlienShooterWeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (weapon.RedKeys[key] != 0 || weapon.GreenKeys[key] != 0 || weapon.BlueKeys[key] != 0 || weapon.AlphaKeys[key] != 0)
                return true;
        }

        return false;
    }

    private static bool GetHasRotationKeyframes(in AlienShooterWeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (weapon.RotationXKeys[key] != 0 || weapon.RotationYKeys[key] != 0 || weapon.RotationZKeys[key] != 0)
                return true;
        }

        return false;
    }

    private ReadOnlyMemory<byte> GetVidBytes(string path)
    {
        lock (Parts)
        {
            return Parts.GetBytes(path);
        }
    }

    [EngineRoutine(0x00411b50)]
    private Vid? CreateVid(ref Resource resource, KnownObject id, bool isTrueColor, HighColorFormat format, Sound? sound)
    {
        string name = GameTextEncoding.Cyrillic.GetString(resource.ReadText());
        var gameObject = _objects.ReadObject(resource.Read<byte>(_objects.ObjectSize));
        string path = Encoding.ASCII.GetString(resource.ReadText());
        string gameFolder = _gameFolder;
        var temporary = ASToolkit.Engine.Vid.CreateEmpty(KnownObject.None, gameObject, DefaultSpriteSpeed, ObjectLayer.Menu);
        temporary.LoadParameters(gameObject, Session.Error, sound, ShouldResetBuiltTerrain);
        var source = FindCopySource(path, temporary);
        if (source is not null)
        {
            var mirror = source.CreateMirror();
            mirror.Id = id;
            mirror.RecordId = id;
            mirror.Name = name;
            mirror.Path = path;
            mirror.LoadParameters(gameObject, Session.Error, sound, ShouldResetBuiltTerrain);
            mirror.SetLayer();

            return mirror;
        }

        var file = Parts.GetFile(path);
        ReadOnlyMemory<byte> data = default;
        if (file is null)
        {
            if (path.Contains(FontExtension, StringComparison.Ordinal) || path.Contains(TrueTypeExtension, StringComparison.Ordinal))
            {
                var font = new VidFont(id, gameObject, _weapons!, DefaultMaxTextureSize);
                PrepareVid(font, gameObject, name, path, sound);
                font.Load(default, default, Session);
                font.SetLayer();
                return font;
            }
            else if (path.Contains(TgaImage.Extension, StringComparison.Ordinal) || path.Contains(BmpImage.Extension, StringComparison.Ordinal) || path.Contains(AnimationExtension, StringComparison.Ordinal))
            {
                bool isText = gameObject.SpriteClass == SpriteClass.Text;
                data = ConvertedPictures.GetOrAdd((Path.GetFullPath(Path.Combine(gameFolder, path)), isText), key => new(() => ConvertPicture(gameFolder, path, key.IsText, null))).Value;
                if (data.IsEmpty)
                {
                    ConvertPicture(gameFolder, path, isText, Session.Error);
                    LogUnopened(Session.Error, path);
                }
            }
        }

        if (!data.IsEmpty)
        {
            file = new VidResource(data);
        }

        if (file is null)
        {
            new Resource().Open(new GameFolder(gameFolder), Session.Error, Encoding.ASCII.GetBytes(path), Resource.AnyTag);

            LogUnopened(Session.Error, path);
            return null;
        }

        var sprite = file.Value;

        int totalFrames = data.IsEmpty ? Parts.CountFrames(path) : sprite.FrameCount;
        var type = sprite.Type;
        Vid vid;
        ReadOnlyMemory<byte> bytes = default;
        if (type.HasFlag(VidType.HardwareLayer))
        {
            vid = new VidHardwareLayer(id, gameObject, _weapons!, sprite, totalFrames);
        }
        else if (type.HasFlag(VidType.Light))
        {
            bytes = data.IsEmpty ? GetVidBytes(path) : data;
            vid = new VidLight(id, gameObject, _weapons!, sprite, totalFrames);
        }
        else if (type.HasFlag(VidType.Hardware) && !(type.HasFlag(VidType.Blended) && type.HasFlag(VidType.ZBuffer)))
        {
            vid = new VidHardware(id, gameObject, _weapons!, sprite, totalFrames, format);
        }
        else
        {
            bytes = data.IsEmpty ? GetVidBytes(path) : data;
            if (type.HasFlag(VidType.Hardware))
            {
                vid = new VidDepthFaded(id, gameObject, _weapons!, sprite, totalFrames, format);
            }
            else if (gameObject.SpriteClass == SpriteClass.BuiltTerrain || !isTrueColor)
            {
                vid = new VidHighColor(id, gameObject, _weapons!, sprite, totalFrames, format);
            }
            else
            {
                vid = new VidSoftware(id, gameObject, _weapons!, sprite, totalFrames, format);
            }
        }

        PrepareVid(vid, gameObject, name, path, sound);
        vid.Load(sprite, bytes, Session);
        if (vid is VidHardware hardware)
        {
            for (int surface = 0; surface < hardware.SurfaceCount; surface++)
            {
                Session.Graphics.DrawLoadBar(this, Vid(0));
            }
        }

        vid.SetLayer();
        return vid;
    }

    private Vid? FindCopySource(string path, Vid temporary)
    {
        bool isBuiltTerrain = temporary.Class == SpriteClass.BuiltTerrain;
        foreach (var existing in Vids)
        {
            if (existing is null || !string.Equals(existing.Path, path, StringComparison.Ordinal))
                continue;

            if (isBuiltTerrain != (existing.Class == SpriteClass.BuiltTerrain))
                continue;

            bool isHardware = existing.FileType.HasFlag(VidType.Hardware);
            if ((isHardware || existing.ObjectGamma == temporary.ObjectGamma) && (isHardware || existing.Properties.HasFlag(ObjectProperties.Gamma) == temporary.Properties.HasFlag(ObjectProperties.Gamma)))
                return existing;
        }

        return null;
    }

    private void PrepareVid(Vid vid, in GameObject record, string name, string path, Sound? sound)
    {
        vid.Name = name;
        vid.Path = path;
        vid.LoadParameters(record, Session.Error, sound, ShouldResetBuiltTerrain);
    }

    private void ResolveLinks(bool isReload)
    {
        foreach (var vid in Vids)
        {
            if (vid is not null && !(isReload && vid.FileType.HasFlag(VidType.FrameVector)))
            {
                SetChildAndLink(vid);
            }
        }
    }

    private bool IsVidDefined(int id) => id >= 0 && id < _vidCount && _vids[id] is not null;
}
