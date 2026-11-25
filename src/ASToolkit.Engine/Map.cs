using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the game world, holding one loaded level at a time.
/// </summary>
public partial class Map : IDisposable
{
    /// <summary>
    /// Represents the number of sprite layers of a map.
    /// </summary>
    public const int LayerCount = 17;

    /// <summary>
    /// Represents the lowest height a blast reaches above its center.
    /// </summary>
    public const float MinBlastHeight = 20;
 
    internal const int UpdatedLayerCount = 16;
    private const int CompactionPeriodMask = 3;
    private const int SpriteRecordSize = 112;
    private const float OldGridRounding = 7;
    private const int StartRandomState = 1;
    private const string ScriptExtension = ".lgc";
    private const string CompiledScriptExtension = ".lgd";
    private const int ScriptHookCount = 64;
    private const int NoHookBase = 1000000;
    private const int ReleaseWindSpeed = 0x19;
    private const int ReleaseWindDirection = 200;

    private static readonly SizeF StartMapSize = new(640, 480);
    private protected GameClock _clock;
    private protected MapFlags _flags;
    private ReadOnlyMemory<byte> _pendingMapName;
    private readonly string _gameFolder;
    private protected int _stepCount;
    private int _levelStartTime;
    private ScrollType _scrollType = ScrollType.FromBorder;
    private protected readonly MyList<Sprite?>[] _layers = new MyList<Sprite?>[LayerCount];
    private DemoResource? _playedDemo;
    private int _playedOffset;
    private MemoryStream? _recordedDemo;
    private short[] _sceneryHeights = [];
    private short[] _objectHeights = [];
    private int _gridColumns;
    private int _gridRows;
    private bool _hasWindow;
    private protected readonly Player[] _players = new Player[ASToolkit.Engine.Vid.ArmyCount];
    private readonly Group _groupHead = new(null, null);
    private int _weaponCount;
    private AlienShooterWeaponRecord[]? _weapons;
    private int _vidCount;
    private readonly Vid?[] _vids = new Vid?[VidTableSize];

    private protected readonly GameRandom _rng;
    private readonly Dictionary<int, ScriptFile> _scriptFiles = [];
    private readonly ControlSettings _controls;
    private readonly LivingSet _livingFences = new();
    private readonly LivingSet _livingSites = new();
    private readonly LivingSet _livingCharges = new();
    private readonly int[] _scriptHooks = new int[ScriptHookCount];
    private int _pausedAt;
    private int _demoPacedTime;
    private uint _demoPacedAt;
    private MemoryStream? _endedDemo;
    private ObjectTable _objects;
    private int _lastHandle;
    private int _unitIndex;
    private OldSpriteType _unitMask;
    private int _spriteLayer;
    private int _spriteIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="Map"/> class with no level loaded.
    /// </summary>
    /// <param name="gameFolder">The game folder that holds the sprite files of the object IDs.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="fileSystem">The file system that holds the game's files.</param>
    /// <param name="registry">The registry of the level scripts, or null for an empty one.</param>
    /// <param name="session">The session the map belongs to, or null to create one.</param>
    [EngineRoutine(0x00408ff0)]
    protected Map(string gameFolder, ObjectTable objects, IGameFileSystem fileSystem, Registry? registry = null, GameSession? session = null)
    {
        Registry = registry ?? new Registry("");
        Menu = new Menu(this);
        Session = session ?? new GameSession(Registry);
        SetFlag(MapFlags.AppMenu, Session.HasAppMenu);
        _flags |= MapFlags.PlayerControlled | MapFlags.DrawFlagman | MapFlags.DrawSoundCount | MapFlags.MouseTips;
        Session.ReadConfig(fileSystem);
        SetFlag(MapFlags.DrawFps, Session.ShouldDrawFps);
        SetFlag(MapFlags.DrawPresentation, Session.ShouldDrawPresentation);
        _hasWindow = true;
        FileSystem = fileSystem;
        _gameFolder = gameFolder;
        _objects = objects;
        Parts = new SpritePartReader(gameFolder, objects);
        _rng = new GameRandom(StartRandomState);
        Script.Error = Session.Error;
        for (int layer = 0; layer < LayerCount; layer++)
        {
            _layers[layer] = new MyList<Sprite?>();
        }
        _clock = new GameClock();
        Size = StartMapSize;
        ResetGroundZ();
        var objectsFile = new Resource();
        if (OpenObjects(fileSystem, Session.Error, ref objectsFile) != 0)
        {
            Session.Error.Error(MapLogTag, LogError.Open, 0, "resource file"u8);
        }

        Sound = Session.Sound ??= new Sound(ref objectsFile, fileSystem, Session.Error, isHighQuality: Session.IsSoundHighQuality);
        Constant = Session.Constant ??= new Constant(ref objectsFile, Session.Error);
        Constant.IsDebugMode = Profile.GetInteger(fileSystem, GameSession.ConfigFile, GameSession.GameSection, "DebugMode"u8, 0) != 0;
        Collector = new SpriteCollector(this);
        LoadVid(ref objectsFile);

        EmptyVid.SetWeapons(_weapons!);
        Collector = new SpriteCollector(Size, Vids, this);
        CursorSprite = new Mouse(EmptyVid, new Vector3(Session.ScreenSize.ToVector2() * HalfShare, 0), default, null, this);
        _controls = ControlSettings.Read(fileSystem);
        Input = new Input(_controls);
        for (int slot = 0; slot < ASToolkit.Engine.Vid.ArmyCount; slot++)
        {
            _players[slot] = new Player(this, _controls);
        }

        _flags |= MapFlags.Initialized;
    }

    /// <summary>
    /// Gets the player the first player slot controls.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no player in the first player slot.</exception>
    public Sprite Player => Flagman(0) ?? throw new InvalidOperationException("There is no player in the first player slot.");

    /// <summary>
    /// Gets a value indicating whether the first player slot has a player.
    /// </summary>
    public bool HasPlayer => Flagman(0) is not null;

    /// <summary>
    /// Gets the sprite groups of the map.
    /// </summary>
    public IEnumerable<Group> Groups
    {
        get
        {
            for (var group = _groupHead.Next; group != _groupHead; group = group.Next)
            {
                yield return group;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the controllers act on the input.
    /// </summary>
    public bool IsPlayerControlled => _flags.HasFlag(MapFlags.PlayerControlled);

    /// <summary>
    /// Gets the sprite of the mouse cursor.
    /// </summary>
    public Mouse CursorSprite { get; }

    /// <summary>
    /// Gets the game time in milliseconds.
    /// </summary>
    public int Time => _clock.Time;

    /// <summary>
    /// Gets the state of the random number generator.
    /// </summary>
    public int RandomState => _rng.State;

    /// <summary>
    /// Gets the length of the current frame in milliseconds.
    /// </summary>
    public int FrameTime => _clock.FrameTime;

    internal int PreviousTime => _clock.PreviousTime;

    /// <summary>
    /// Gets a value indicating whether the level script has locked the doors.
    /// </summary>
    public bool AreDoorsLocked => ReadScriptNumber("DoorsIsOpen"u8) == 0;

    /// <summary>
    /// Gets a value indicating whether any electric fence still stands.
    /// </summary>
    public bool AreFencesUp => GetLiving(KnownObject.Fence, _livingFences).Count != 0;

    /// <summary>
    /// Gets the wall time of the current frame in milliseconds.
    /// </summary>
    public uint WallTime => _clock.WallTime;

    /// <summary>
    /// Gets a value indicating whether the map is loading a level.
    /// </summary>
    public bool IsLoading => _flags.HasFlag(MapFlags.Loading);

    /// <summary>
    /// Gets a value indicating whether the level is paused, with only its menu sprites updating.
    /// </summary>
    public bool IsPaused => _flags.HasFlag(MapFlags.Paused);

    /// <summary>
    /// Gets the sound effects and music of the session.
    /// </summary>
    public Sound Sound { get; }

    /// <summary>
    /// Gets the tuning values of the session.
    /// </summary>
    public Constant Constant { get; }

    /// <summary>
    /// Gets the menu sprites that keep their place on the screen as the camera moves.
    /// </summary>
    public ReadOnlySpan<Sprite> MenuSprites => Menu.Items;

    internal Relation Relation { get; } = new();

    internal Menu Menu { get; }

    internal IGameFileSystem FileSystem { get; }

    /// <summary>
    /// Gets the size of the map, in map pixels.
    /// </summary>
    public SizeF Size { get; private set; }

    /// <summary>
    /// Gets the top-left corner of the screen on the map.
    /// </summary>
    public Vector2 Camera { get; private set; }

    /// <summary>
    /// Gets the level script.
    /// </summary>
    public Script Script { get; } = new();

    /// <summary>
    /// Gets the input of the current frame.
    /// </summary>
    public Input Input { get; }

    /// <summary>
    /// Gets the spatial hash of the sprites of the map.
    /// </summary>
    public SpriteCollector Collector { get; private set; }

    internal GameRandom Random => _rng;

    internal bool CanMonstersAttackNeutralTrains => _flags.HasFlag(MapFlags.MonstersAttackNeutralTrains);

    internal Dictionary<int, Sprite> Handles { get; } = [];

    internal int FramesPerSecond => _clock.FramesPerSecond;

    private static ReadOnlySpan<byte> SpriteDataTag => "SPRD"u8;

    /// <summary>
    /// Sets a map file, or a demo file, to load at the start of the next step, before that step's frame runs.
    /// </summary>
    /// <param name="name">The path of the map or demo file, relative to the game folder.</param>
    /// <remarks>The random number generator is seeded with the step's wall time unless a demo supplies the seed.</remarks>
    public void RequestLoad(ReadOnlySpan<byte> name)
    {
        _flags |= MapFlags.LoadPending;
        _pendingMapName = name.ToArray();
    }

    /// <summary>
    /// Loads a map file, or the map a demo file plays, and releases the level the map held before.
    /// </summary>
    /// <param name="name">The path of the map or demo file, relative to the game folder; an empty path loads nothing.</param>
    /// <param name="wallTime">The wall time in milliseconds, used as the random seed unless a demo supplies one.</param>
    /// <remarks>If the file is missing or invalid, or a chunk is missing, the problem is logged and the load ends early with the map still loading.</remarks>
    [EngineRoutine(0x0040c960)]
    public void Load(ReadOnlySpan<byte> name, uint wallTime)
    {
        _flags |= MapFlags.Loading;

        if (name.IsEmpty)
            return;

        if (Size.Width != 0 || Size.Height != 0)
        {
            ShowLoadingStep("Release previous map"u8);
            Release();
        }

        _lastHandle = 0;
        Menu.Clicks = default;
        _livingFences.Clear();
        _livingSites.Clear();
        _livingCharges.Clear();
        if (!Session.IsRecordingNextMap)
        {
            var demo = new Resource();
            if (demo.Open(FileSystem, Session.Error, name, "DEMO"u8) == 0)
            {
                FileSystem.TryRead(Encoding.ASCII.GetString(name), out var data);
                name = demo.ReadText();
                _playedDemo = new DemoResource(data);
                _flags |= MapFlags.PlayingDemo;
            }
        }

        var reader = new Resource();
        if (reader.Open(FileSystem, Session.Error, name, "MAP "u8) != 0)
        {
            Session.Error.Alert($"!!!ERROR!!!LOAD: Invalid map file {MyError.Decode(name)}");
            return;
        }

        CursorSprite.Disable();
        Session.EnterMap(name.ToArray());
        int seed = IsPlayingDemo ? _playedDemo!.Value.Seed : (int)wallTime;
        _rng.Seed(seed);
        ShowLoadingStep("Load extra vid"u8);
        LoadVid(ref reader);
        if (reader.GoBegin("GRPH"u8) != Resource.Found)
        {
            if (!LoadOldTypeMap(ref reader))
                return;
        }
        else if (!LoadNewTypeMap(ref reader, name, seed))
            return;

        _flags &= ~MapFlags.Loading;
        Relation.Release();
        Session.Error.Write($"Vid    release {Texture.MemoryInUse} {ASToolkit.Engine.Vid.MemoryInUse}");
        LoadScript(name);
        if (IsPlayingDemo)
        {
            _playedOffset = Script.ReadState(_playedDemo!.Value.Body);
        }
        else if (_flags.HasFlag(MapFlags.RecordingDemo))
        {
            Script.WriteState(_recordedDemo!);
        }

        _clock.SetWallTime(wallTime);
        ShowLoadingStep("Run scripts for create sprites"u8);
        if (!_flags.HasFlag(MapFlags.AppMenu))
        {
            RunCreationHandlers();
        }

        if (!IsPlayingDemo)
        {
            CursorSprite.Enable(FileSystem);
        }

        ShowLoadingStep([]);
    }

    /// <summary>
    /// Sets whether the controllers act on the input.
    /// </summary>
    /// <param name="controlled"><see langword="true"/> if the controllers act on the input; otherwise, <see langword="false"/>.</param>
    public void SetPlayerControl(bool controlled) => SetFlag(MapFlags.PlayerControlled, controlled);

    /// <summary>
    /// Removes a reference to a sprite and deletes the sprite when no reference is left.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    public void Release(Sprite sprite)
    {
        int count = sprite.Release();

        if (count >= 1)
            return;

        if (count < 0)
        {
            Session.Error.Error($"SPRITE {(int)sprite.Vid.Id}", LogError.Invalid, count, "noRef at Release"u8);
            return;
        }

        DeleteSprite(sprite);
    }

    /// <summary>
    /// Returns the state a new sprite of an object ID starts in.
    /// </summary>
    /// <param name="vid">The shared data of the object ID.</param>
    /// <returns>The starting state.</returns>
    public SpriteAnimation GetInitialAnimation(Vid vid)
    {
        return IsLoading && vid.InitialAnimation == SpriteAnimation.Birth ? SpriteAnimation.Stand : vid.InitialAnimation;
    }

    /// <summary>
    /// Returns the player a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <returns>The player, or null if the slot is empty.</returns>
    [EngineRoutine(0x0040e620)]
    public Sprite? Flagman(int army) => _players[army & ASToolkit.Engine.Vid.ArmyMask].Sprite;

    /// <summary>
    /// Sets the player a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <param name="sprite">The player, or null to clear the slot.</param>
    [EngineRoutine(0x0040e600)]
    public void SetFlagman(int army, Sprite? sprite) => _players[army & ASToolkit.Engine.Vid.ArmyMask].SetFlagman(sprite);

    /// <summary>
    /// Advances the game clock to a wall time, compacts the update lists, loads a map if one was requested and processes the window messages.
    /// </summary>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0040f210)]
    public bool AdvanceClock(uint wallTime)
    {
        _clock.StartTact(wallTime, wallTime);
        Input.ResetFrame();
        if ((Time & CompactionPeriodMask) == 0)
        {
            foreach (var list in _layers)
            {
                int kept = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is not null)
                    {
                        list[kept++] = list[i];
                    }
                }

                if (kept < 1)
                {
                    list.Release();
                }
                else
                {
                    list.Count = kept;
                }
            }
        }

        if (_flags.HasFlag(MapFlags.LoadPending))
        {
            _flags &= ~MapFlags.LoadPending;
            Load(_pendingMapName.Span, wallTime);
        }

        while (Input.TryPeekMessage(out var message))
        {
            if (message.Id == WindowMessageId.Quit)
                return true;

            if (!_hasWindow || !TranslateAccelerator(message))
            {
                DispatchMessage(message);
            }
        }

        return false;
    }

    /// <summary>
    /// Runs one frame of the map.
    /// </summary>
    /// <param name="wallTime">The wall time of the frame in milliseconds.</param>
    /// <param name="now">The real time in milliseconds that paces a played demo.</param>
    /// <param name="waitUntil">The real time to wait for before the frame, or <paramref name="now"/> when the frame is due.</param>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0041f0e0)]
    public virtual bool Tact(uint wallTime, uint now, out uint waitUntil)
    {
        waitUntil = now;
        return false;
    }

    /// <summary>
    /// Releases the loaded level.
    /// </summary>
    [EngineRoutine(0x0040b180)]
    public void Dispose()
    {
        Release();
        for (int i = _vidCount - 1; i >= 0; i--)
        {
            if (_vids[i] is not null)
            {
                _vids[i]!.Destruct(Session.Error);
                _vids[i] = null;
            }
        }

        _vidCount = 0;
        Session.Error.Write($"Vid    release {Texture.MemoryInUse} {ASToolkit.Engine.Vid.MemoryInUse}");
    }

    /// <summary>
    /// Gets the sprites of the update list of a layer.
    /// </summary>
    /// <param name="layer">The layer, from 0 to 16.</param>
    /// <returns>The sprites in the list's order, with <see langword="null"/> where a sprite was removed.</returns>
    public MyList<Sprite?> GetLayer(int layer) => _layers[layer];

    internal void SetMonstersAttackNeutralTrains(bool attack) => SetFlag(MapFlags.MonstersAttackNeutralTrains, attack);

    [EngineRoutine(0x004145b0)]
    internal void RaiseHeights(Vid vid, Sprite sprite)
    {
        if (sprite == CursorSprite || vid.HeightPoints.Count == 0)
            return;

        bool permanent = !_flags.HasFlag(MapFlags.AppMenu) && vid.Class == SpriteClass.BuiltTerrain;

        foreach (var point in vid.GetHeightPoints(sprite.Frame))
        {
            var cell = new Vector3(sprite.Position.X + point.X, sprite.Position.Y + point.Y, sprite.Position.Z + point.Z);

            if (permanent)
            {
                SetGroundZ(cell);
            }
            else
            {
                SetTempGroundZ(cell);
            }
        }
    }

    [EngineRoutine(0x004146b0)]
    internal void LowerHeights(Vid vid, Sprite sprite)
    {
        if (sprite == CursorSprite || vid.HeightPoints.Count == 0 || !_flags.HasFlag(MapFlags.AppMenu) && vid.Class == SpriteClass.BuiltTerrain)
            return;

        foreach (var point in vid.GetHeightPoints(sprite.Frame))
        {
            ClearTempGroundZ(new Vector3(sprite.Position.X + point.X, sprite.Position.Y + point.Y, sprite.Position.Z + point.Z));
        }
    }

    internal bool IsFree(Vid vid, Vector3 position) => Collector.CanPlace(vid, position) is null;

    [EngineRoutine(0x0040c570)]
    internal virtual void Release()
    {
        var error = Session.Error;
        LogCounts(error);
        Relation.Release();
        _clock.ResetRate();
        Session.Graphics.Movie.Release();
        Session.Graphics.SetWind(ReleaseWindSpeed, new Angle(ReleaseWindDirection));
        Session.Graphics.SetEnvironment((MapEnvironment)(-1));
        if (IsPlayingDemo)
        {
            CursorSprite.Enable(FileSystem);
        }

        _playedDemo = null;
        _playedOffset = 0;
        _demoPacedTime = 0;
        _demoPacedAt = 0;
        EndDemoRecording();
        _flags &= ~(MapFlags.RecordingDemo | MapFlags.PlayingDemo);
        _scrollType = ScrollType.FromBorder;
        error.Write("Player release"u8);
        foreach (var player in _players)
        {
            player.Reset();
        }

        error.Write("Sprite release"u8);
        // TODO: Port the trains; the release sets the flag (DAT_005da58c) that makes the Engine destructors (0x44e9f0, 0x450e20) skip their scripts while it destroys the sprites
        foreach (var player in _players)
        {
            player.Messages.Clear();
        }

        DeleteAll();
        error.Write("Menu   release"u8);
        if (Menu.Count != 0)
        {
            error.Error($"SPRITE {(int)Menu[0].Vid.Id}", LogError.Text, 0, "Menu sprite exist after delete"u8);
            Menu.DeleteAll();
        }

        int elapsed = Time - _levelStartTime;
        error.Write($"Average fps={(elapsed == 0 ? 0 : (int)((uint)(_stepCount * 1000) / (uint)elapsed))}");
        error.Write("Script release"u8);
        foreach (var file in _scriptFiles.Values)
        {
            file.Dispose();
        }

        _scriptFiles.Clear();
        Script.Reset();
        _stepCount = 0;
        DeleteExtraVid();
        ResetVidLevelState();
        for (int i = 0; i < _scriptHooks.Length; i++)
        {
            _scriptHooks[i] = i + NoHookBase;
        }
    }

    [EngineRoutine(0x00406f20)]
    private static ReadOnlySpan<byte> GetBeforeLast(ReadOnlySpan<byte> text, ReadOnlySpan<byte> delimiter)
    {
        int index = text.LastIndexOf(delimiter);

        return index < 0 ? text : text[..index];
    }

    private static int GetScriptHook(ReadOnlySpan<byte> name) => Encoding.ASCII.GetString(name) switch
    {
        "main" => 0,
        "TrainNotAmmo" => 1,
        "TrainNotPower" => 2,
        "TrainDamage" => 3,
        "TrainCreated" => 4,
        "TrainSplit" => 5,
        "TrainDestroy" => 6,
        "TrainDestroyPower" => 7,
        "TrainArrive" => 8,
        "???TrainNotArrive" => 9,
        "TrainAttacked" => 10,
        "DepoDestroy" => 11,
        "DepoBirth" => 12,
        "DepoAttacked" => 13,
        "DepoFree" => 14,
        "BuildingCapture" => 15,
        "MasterDestroy" => 16,
        "???" => 17,
        "???SuperWeaponWounded" => 18,
        "MineBlast" => 19,
        "MineRemove" => 20,
        "EnemyLinked" => 21,
        "TrainClash" => 22,
        "UnitCreated" => 23,
        "UnitDestroy" => 24,
        _ => -1
    };

    private protected static void OverrideGamma(Sprite sprite, Gamma gamma)
    {
        sprite.SetGammaOverride(gamma);
        var link = sprite.Link;

        if (link is not null && link.Vid == sprite.Vid.LinkVid)
        {
            link.SetGammaOverride(gamma);
        }
    }

    private bool LoadOldTypeMap(ref Resource reader)
    {
        if (reader.GoBegin(HeadTag) != Resource.Found)
        {
            LogMissingSection(HeadTag);
            return false;
        }

        int width = 0;
        reader.ReadInto(ref width);
        int height = 0;
        reader.ReadInto(ref height);
        Size = new SizeF(width, height);
        short scrollX = 0;
        reader.ReadInto(ref scrollX);
        short scrollY = 0;
        reader.ReadInto(ref scrollY);
        int time = Time;
        reader.ReadInto(ref time);
        _levelStartTime = time;
        _clock.SetTimes(time, time);
        int version = 0;
        reader.ReadInto(ref version);
        Session.Graphics.OldLoadParameters(ref reader, this);
        Collector = new SpriteCollector(Size, Vids, this);
        ResetGroundZ();
        LoadGridZ(ref reader, (int)(Size.Height + OldGridRounding) / GridZCellSize * ((int)(Size.Width + OldGridRounding) / GridZCellSize) * sizeof(short));
        if (!FindSection(ref reader, SpriteTag))
            return false;

        while (TryLoadOldSprite(ref reader, out _))
        {
        }

        if (!FindSection(ref reader, SpriteDataTag))
            return false;

        while (TryReadPointer(ref reader, out var sprite))
        {
            sprite?.Action(ActionCode.RestoreOld, ref reader, version);

            reader.GoNextSub(SpriteDataTag);
        }

        return true;
    }

    private bool LoadNewTypeMap(ref Resource reader, ReadOnlySpan<byte> name, int seed)
    {
        ShowLoadingStep("Load graph parameters"u8);
        Session.Graphics.Load(ref reader, this);
        if (!FindSection(ref reader, HeadTag))
            return false;

        int version = LoadHead(ref reader, name, seed, out var scroll);
        Session.Error.Write($"CurrentTime   ={(uint)Time,-15}   sizeof(SPRITE)={SpriteRecordSize,-8} Map version   ={version}");
        ShowLoadingStep("Create new hash table"u8);
        Collector = new SpriteCollector(Size, Vids, this);
        SetPosition(Session.ScreenSize.ToVector2() * HalfShare + scroll, CameraMove.None);
        ShowLoadingStep("Load gridZ"u8);
        ResetGroundZ();
        LoadGridZ(ref reader, _gridColumns * _gridRows * sizeof(short));
        if (!FindSection(ref reader, SpriteTag))
            return false;

        ShowLoadingStep("Load hardware terrain"u8);
        LoadSprites(ref reader, version);
        // TODO: Port the trains; the loader builds the rail map (0x43c510) after the terrain and the sprite records, and the Engine links (0x454530) after the sprites
        ShowLoadingStep("Load data for sprite"u8);
        if (!FindSection(ref reader, SpriteDataTag))
            return false;

        LoadSpriteData(ref reader, version);
        ShowLoadingStep("Load players info"u8);
        if (!FindSection(ref reader, "PLAY"u8))
            return false;

        foreach (var player in _players)
        {
            player.Load(ref reader);
        }

        ShowLoadingStep("Load groups info"u8);
        if (!FindSection(ref reader, "GROU"u8))
            return false;

        LoadGroups(ref reader);
        return true;
    }

    private int LoadHead(ref Resource reader, ReadOnlySpan<byte> name, int seed, out Vector2 scroll)
    {
        float width = Size.Width;
        reader.ReadInto(ref width);
        float height = Size.Height;
        reader.ReadInto(ref height);
        Size = new SizeF(width, height);
        float scrollX = 0;
        reader.ReadInto(ref scrollX);
        float scrollY = 0;
        reader.ReadInto(ref scrollY);
        scroll = new Vector2(scrollX, scrollY);
        int savedTime = Time;
        reader.ReadInto(ref savedTime);
        _clock.SetTimes(GameClock.LevelStartPreviousTime, GameClock.LevelStartTime);
        if (!IsPlayingDemo && Session.IsRecordingNextMap)
        {
            Session.IsRecordingNextMap = false;
            StartDemoRecording(name, seed);
        }

        if (IsPlayingDemo)
        {
            SetTime(_playedDemo!.Value.StartTime);
        }

        _levelStartTime = Time;
        int version = 0;
        reader.ReadInto(ref version);
        if (version < MapHeader.FloatVersion)
        {
            Size = new SizeF(BitConverter.SingleToInt32Bits(Size.Width), BitConverter.SingleToInt32Bits(Size.Height));
            scroll = new Vector2(BitConverter.SingleToInt32Bits(scroll.X), BitConverter.SingleToInt32Bits(scroll.Y));
        }

        return version;
    }

    private void LoadGridZ(ref Resource reader, int expectedSize)
    {
        if (reader.GoNext("GRID"u8) == Resource.Found)
        {
            int count = reader.Load(Session.Error, out byte[] grid);
            _sceneryHeights = MemoryMarshal.Cast<byte, short>(grid).ToArray();
            if (count != expectedSize)
            {
                Session.Error.Error(MapLogTag, LogError.Invalid, count, "grid"u8);
                ResetGroundZ();
            }
        }
        else
        {
            reader.GoBegin(Resource.AnyTag);
        }
    }

    private void LoadSprites(ref Resource reader, int version)
    {
        var shownLayer = ObjectLayer.Ground;
        while (TryLoadSprite(ref reader, version, out var sprite))
        {
            if (Constant.IsDebugMode && sprite!.Vid.Layer != shownLayer)
            {
                shownLayer = sprite.Vid.Layer;
                Session.Graphics.SetLoadingText(shownLayer switch
                {
                    ObjectLayer.Ground => "Load hardware terrain"u8,
                    ObjectLayer.Built => "Build sprites in terrain"u8,
                    ObjectLayer.BuiltAlpha => "Build sprites with alpha in terrain"u8,
                    _ => "Load sprites"u8
                });
            }

            Session.Graphics.DrawLoadBar(this, _vids[0]);
        }
    }

    private void LoadSpriteData(ref Resource reader, int version)
    {
        while (TryReadPointer(ref reader, out var sprite))
        {
            Session.Graphics.DrawLoadBar(this, _vids[0]);
            sprite?.Action(ActionCode.Restore, ref reader, version);

            reader.GoNextSub(SpriteDataTag);
        }
    }

    private bool FindSection(ref Resource reader, ReadOnlySpan<byte> tag)
    {
        if (reader.GoNext(tag) == Resource.Found)
            return true;

        LogMissingSection(tag);
        return false;
    }

    private void LoadScript(ReadOnlySpan<byte> name)
    {
        ShowLoadingStep("Load script file"u8);
        string stem = GameTextEncoding.Cyrillic.GetString(GetBeforeLast(name, "."u8));
        string scriptPath = stem + ScriptExtension;
        if (!FileSystem.TryRead(scriptPath, out _))
        {
            string compiledPath = stem + CompiledScriptExtension;
            if (FileSystem.TryRead(compiledPath, out _))
            {
                scriptPath = compiledPath;
            }
        }

        Script.Load(FileSystem, scriptPath);
        ShowLoadingStep("Connect script function with VID"u8);
        var objects = Script.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            ref readonly var entry = ref objects[i];

            if (entry.Kind == ScriptObjectKind.Function)
            {
                int hook = GetScriptHook(objects.Name(i).Span);

                if (hook >= 0)
                {
                    _scriptHooks[hook] = i;
                }

                ProcessScriptFunctions(objects.Name(i).Span, i, entry.Count);
            }
        }
    }

    private void RunCreationHandlers()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _layers[layer];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null)
                {
                    RunCreationHandler(sprite);
                }
            }
        }
    }

    internal static int OpenObjects(IGameFileSystem fileSystem, MyError? error, ref Resource resource)
    {
        var name = Profile.TryGetString(fileSystem, GameSession.ConfigFile, GameSession.GameSection, "Resource"u8, out var configured) ? configured.Span : "objects.res"u8;

        return resource.Open(fileSystem, error, name, "DATA"u8);
    }

    private void LogMissingSection(ReadOnlySpan<byte> tag) => Session.Error.Error(MapLogTag, LogError.Section, 0, tag);

    [EngineRoutine(0x00411440)]
    private void DeleteExtraVid()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _layers[layer];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null && sprite.Vid.FileType.HasFlag(VidType.FrameVector))
                {
                    DeleteSprite(sprite);
                }
            }
        }

        for (int i = _vidCount - 1; i >= 0; i--)
        {
            if (_vids[i] is not null && _vids[i]!.FileType.HasFlag(VidType.FrameVector))
            {
                _vids[i]!.Destruct(Session.Error);
                _vids[i] = null;
            }
        }

        while (_vidCount > 0 && _vids[_vidCount - 1] is null)
        {
            _vidCount--;
        }
    }

    private void ShowLoadingStep(ReadOnlySpan<byte> text)
    {
        if (Constant.IsDebugMode)
        {
            Session.Graphics.SetLoadingText(text);
        }
    }

    internal bool HasFlag(MapFlags flag) => _flags.HasFlag(flag);

    private void SetFlag(MapFlags flag, bool isSet) => _flags = (isSet ? flag : MapFlags.None) | _flags & ~flag;

    /// <summary>
    /// Creates a sprite of an object ID, or of the object ID that a region at the position replaces it with, and runs the creation handler of
    /// the sprite's object ID unless the map is loading or is the application menu.
    /// </summary>
    /// <param name="vid">The shared data of the object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, and links to if the new sprite is a linker.</param>
    /// <returns>The sprite, or <see langword="null"/> if <paramref name="vid"/> is null, or its class isn't valid.</returns>
    [EngineRoutine(0x0040e870)]
    public virtual Sprite? CreateSprite(Vid? vid, Vector3 position, Angle direction, Sprite? parent = null)
    {
        if (vid is null)
            return null;

        if (vid.IsRegionReplaced)
        {
            vid = Region.ReplaceVid(this, vid, position);
        }

        Sprite sprite;
        switch (vid.Class)
        {
            case SpriteClass.Unit:
                sprite = new Unit(vid, position, direction, parent, this);
                break;
            case SpriteClass.Avia:
                sprite = new Plane(vid, position, direction, parent, this);
                break;
            case SpriteClass.Cannon:
                sprite = new Cannon(vid, position, direction, parent, this);
                break;
            case SpriteClass.Primitive:
                sprite = new Primitive(vid, position, direction, parent, this);
                break;
            case SpriteClass.Man:
                sprite = new Man(vid, position, direction, parent, this);
                break;
            case SpriteClass.BuiltTerrain:
                sprite = new BuiltTerrain(vid, position, direction, parent, this);
                break;
            case SpriteClass.Sprite:
                sprite = new Sprite(vid, position, direction, parent, this);
                break;
            case SpriteClass.Frame:
                sprite = new Frame(vid, position, direction, parent, this);
                break;
            case SpriteClass.Linker:
                sprite = new Linker(vid, position, direction, parent, this);
                break;
            case SpriteClass.Text:
                sprite = new Text(vid, position, direction, parent, this);
                break;
            case SpriteClass.Region:
                sprite = new Region(vid, position, direction, parent, this);
                break;
            default:
                Session.Error.Error(MapLogTag, LogError.Create, (int)vid.Class, "sprite - Behave is invalidate"u8);

                return null;
        }

        if ((_flags & (MapFlags.Loading | MapFlags.AppMenu)) == 0)
        {
            RunCreationHandler(sprite);
        }

        return sprite;
    }

    private List<Sprite> GetLiving(KnownObject id, LivingSet set)
    {
        if (set.CheckedAt == Time)
            return set.Sprites;

        set.CheckedAt = Time;
        set.Sprites.Clear();
        var vid = FindVid(id);

        if (vid is null)
            return set.Sprites;

        foreach (var sprite in _layers[(int)vid.Layer].Items)
        {
            if (sprite is not null && sprite.Vid == vid && sprite.Animation < SpriteAnimation.Death)
            {
                set.Sprites.Add(sprite);
            }
        }

        return set.Sprites;
    }

    private void SetTime(int time) => _clock.SetTime(time);

    [EngineRoutine(0x0043ad70)]
    private void LoadGroups(ref Resource reader)
    {
        while (TryReadPointer(ref reader, out var first))
        {
            var group = new Group(_groupHead, first);
            while (TryReadPointer(ref reader, out var sprite))
            {
                group.Add(sprite!);
            }
        }
    }

    [EngineRoutine(0x0043adf0)]
    private void RemoveFromGroups(Sprite sprite)
    {
        var group = _groupHead.Next;

        while (group != _groupHead)
        {
            var next = group.Next;

            if (group.Remove(sprite))
            {
                Release(sprite);

                if (group.Members.Count == 0)
                {
                    group.Unlink();
                }
            }

            group = next;
        }
    }

    [EngineRoutine(0x0040fad0)]
    internal void AddToLayer(Sprite sprite)
    {
        _layers[(int)sprite.Vid.Layer].Insert(sprite);
        sprite.AddRef();
        Release(sprite);
    }

    [EngineRoutine(0x0040fa90)]
    internal bool RemoveFromLayer(Sprite sprite)
    {
        var list = _layers[(int)sprite.Vid.Layer];
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] != sprite)
                continue;

            list[i] = null;

            return true;
        }

        return false;
    }

    [EngineRoutine(0x0040f9e0)]
    internal void Pause()
    {
        if (!IsPaused)
        {
            _pausedAt = Time;
        }

        _flags |= MapFlags.Paused;
    }

    [EngineRoutine(0x0040fa00)]
    private void Resume()
    {
        if (!IsPaused)
            return;

        for (int layer = 0; layer < LayerCount; layer++)
        {
            foreach (var entry in _layers[layer].Items)
            {
                entry?.LastTickTime = _pausedAt;
            }
        }

        _clock.Resume(_pausedAt);
        _flags &= ~MapFlags.Paused;
    }

    private void DeleteAll()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _layers[layer];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null)
                {
                    DeleteSprite(sprite);
                }
            }
        }

        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _layers[layer];
            int top = list.Count - 1;
            while (top >= 0 && list[top] is null)
            {
                top--;
            }

            if (top >= 0)
            {
                Session.Error.Error($"SPRITE {(int)list[top]!.Vid.Id}", LogError.Text, top, "Sprite exist after delete"u8);
            }
        }

        if (_groupHead.Next != _groupHead)
        {
            Session.Error.Error(MapLogTag, LogError.Text, 0, "Incorrect delete groups in DeleteAll()"u8);
        }
    }

    [EngineRoutine(0x0040b4b0)]
    internal virtual void DeletePointerToSprite(Sprite removed)
    {
        foreach (var controller in _players)
        {
            controller.Forget(removed);
        }

        if (removed.Handle != 0)
        {
            Script.DeletePointerToObject(removed.Handle);
        }

        RemoveFromGroups(removed);

        int index = removed.ReferenceCount > 1 ? Collector.Listed.Length - 1 : -1;
        while (index >= 0)
        {
            Collector.Listed[index].DeletePointerToSprite(removed);

            if (Collector.Listed.Length < index)
            {
                index = Collector.Listed.Length;
            }

            index--;
        }

        for (int layer = 0; layer < LayerCount; layer++)
        {
            if (removed.ReferenceCount <= 1)
                continue;

            var list = _layers[layer];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                sprite?.DeletePointerToSprite(removed);
            }
        }
    }
}
