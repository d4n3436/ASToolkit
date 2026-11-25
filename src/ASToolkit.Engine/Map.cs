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

    /// <summary>
    /// Represents the game time a level starts at.
    /// </summary>
    public const int LevelStartTime = 10;

    /// <summary>
    /// Represents the game time of the frame before the one a level starts at.
    /// </summary>
    public const int LevelStartPreviousTime = 1;
    
    internal const int UpdatedLayerCount = 16;
    internal const string ConfigFile = "AlienShooter.cfg";
    private const string DefaultFont = "Arial";
    private const int DefaultFontWidth = 0;
    private const int DefaultFontHeight = 8;
    private const int CompactionPeriodMask = 3;
    private const int ResumeFrameTime = 10;
    private const uint MaxRealStep = 71;
    private const uint FrameRateWindow = 1000;
    private const float NormalSpeed = 1;
    private const uint StartPreviousRealTimeOffset = 10;
    private const int SpriteRecordSize = 112;
    private const float OldGridRounding = 7;
    private const int StartRandomState = 1;
    private const string ScriptExtension = ".lgc";
    private const string CompiledScriptExtension = ".lgd";
    private const int EventFunctionCount = 64;
    private const int NoEventFunctionBase = 1000000;
    private const int ReleaseWindSpeed = 0x19;
    private const int ReleaseWindDirection = 200;

    private static readonly SizeF StartMapSize = new(640, 480);
    private int _framesPerSecond;
    private int _framesThisSecond;
    private protected MapFlags _flags;
    private float _speed;
    private ReadOnlyMemory<byte> _mapName;
    private ReadOnlyMemory<byte> _pendingMapName;
    private ReadOnlyMemory<byte> _previousMapName;
    private readonly string _gameFolder;
    private protected int _stepCount;
    private int _levelStartTime;
    private ScrollType _scrollType = ScrollType.FromBorder;
    private protected readonly MyList<Sprite?>[] _layers = new MyList<Sprite?>[LayerCount];
    private DemoResource? _playedDemo;
    private int _playedOffset;
    private MemoryStream? _recordedDemo;
    private Stream? _demoFile;
    private ReadOnlyMemory<byte> _demoName;
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
    private protected readonly MouseTips _mouseTips;

    private protected readonly GameRandom _rng;
    private readonly Dictionary<int, ScriptFile> _scriptFiles = [];
    private readonly ControlSettings _controls;
    private readonly LivingSet _livingFences = new();
    private readonly LivingSet _livingSites = new();
    private readonly LivingSet _livingCharges = new();
    private readonly int[] _eventFunctionNumbers = new int[EventFunctionCount];
    private int _pausedAt;
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
    /// <param name="session">The session the map belongs to, or null to start one.</param>
    [EngineRoutine(0x00408ff0)]
    protected Map(string gameFolder, ObjectTable objects, IGameFileSystem fileSystem, GameSession? session = null)
    {
        _mouseTips = new MouseTips(this);
        Menu = new Menu(this);
        Session = session ?? new GameSession();
        Session.RealCurrentTime = Session.TimeGetTime();
        Session.PreviousRealCurrentTime = Session.RealCurrentTime - StartPreviousRealTimeOffset;
        _levelStartTime = (int)Session.RealCurrentTime;
        SetFlag(MapFlags.AppMenu, Session.HasAppMenu);
        _flags |= MapFlags.PlayerControlled | MapFlags.DrawFlagman | MapFlags.DrawSoundCount | MapFlags.MouseTips;
        var config = new Profile(fileSystem);
        config.Load(ConfigFile);
        int fontHeight = config.GetInt(GraphSection, "FontSizeY"u8, DefaultFontHeight);
        int fontWidth = config.GetInt(GraphSection, "FontSizeX"u8, DefaultFontWidth);
        string font = config.TryGetString(GraphSection, "Font"u8, out var fontName) ? Encoding.ASCII.GetString(fontName.Span) : DefaultFont;
        Session.Graphics.CreateDeviceFont(font, new Size(fontWidth, fontHeight), DefaultMaxTextureSize);
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
        _speed = NormalSpeed;
        Size = StartMapSize;
        ResetGroundZ();
        var objectsFile = new Resource();
        if (OpenObjects(fileSystem, Session.Error, ref objectsFile) != 0)
        {
            Session.Error.Error(MapLogTag, LogError.Open, 0, "resource file"u8);
        }

        Session.Sound ??= new Sound(ref objectsFile, fileSystem, Session.Error, isHighQuality: Session.Registry.GetInt("SoundHighQuality", 0) != 0, time: Session.Time);
        Session.Constant ??= new Constant(ref objectsFile, Session.Error);
        Constant.IsDebugMode = config.GetInt(GameSection, "DebugMode"u8, 0) != 0;
        SetFlag(MapFlags.DrawFps, config.GetInt(GameSection, "DrawFPS"u8, 0) != 0);
        SetFlag(MapFlags.DrawPresentation, config.GetInt(GameSection, "DrawPresentation"u8, 1) != 0);
        Session.Strings = new Profile(fileSystem);
        Session.Strings.Load(StringsFile);
        Sprites = new SpriteCollector(this);
        LoadVid(ref objectsFile);

        EmptyVid.SetWeapons(_weapons!);
        Sprites = new SpriteCollector(Size, Vids, this);
        Mouse = new Mouse(EmptyVid, new Vector3(Session.Graphics.ScreenSize.ToVector2() * HalfShare, 0), default, null, this);
        _controls = ControlSettings.Read(fileSystem);
        Input = new Input(_controls);
        for (int slot = 0; slot < ASToolkit.Engine.Vid.ArmyCount; slot++)
        {
            _players[slot] = new Player(this, _controls);
        }

        _flags |= MapFlags.Initialized;
    }

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
    public Mouse Mouse { get; }

    /// <summary>
    /// Gets the game time in milliseconds.
    /// </summary>
    public int CurrentTime => Session.CurrentTime;

    /// <summary>
    /// Gets the state of the random number generator.
    /// </summary>
    public int RandomState => _rng.State;

    /// <summary>
    /// Gets the length of the current frame in milliseconds.
    /// </summary>
    public int FrameTime => Session.CurrentTime - Session.PreviousCurrentTime;

    internal int PreviousCurrentTime => Session.PreviousCurrentTime;

    /// <summary>
    /// Gets a value indicating whether the level script has locked the doors.
    /// </summary>
    public bool AreDoorsLocked => ReadScriptNumber("DoorsIsOpen"u8) == 0;

    /// <summary>
    /// Gets a value indicating whether any electric fence still stands.
    /// </summary>
    public bool AreFencesUp => GetLiving(KnownObject.Fence, _livingFences).Count != 0;

    /// <summary>
    /// Gets the real time of the current frame in milliseconds.
    /// </summary>
    public uint RealCurrentTime => Session.RealCurrentTime;

    /// <summary>
    /// Gets a value indicating whether the map is loading a level.
    /// </summary>
    public bool IsLoading => _flags.HasFlag(MapFlags.Loading);

    /// <summary>
    /// Gets a value indicating whether the level is paused, with only its menu sprites updating.
    /// </summary>
    public bool IsPaused => _flags.HasFlag(MapFlags.Paused);

    /// <summary>
    /// Gets the sound effects and music of the game.
    /// </summary>
    public Sound Sound => Session.Sound!;

    /// <summary>
    /// Gets the tuning values of the game.
    /// </summary>
    public Constant Constant => Session.Constant!;

    /// <summary>
    /// Gets the menu sprites that keep their place on the screen as the view moves.
    /// </summary>
    public ReadOnlySpan<Sprite> MenuSprites => Menu.Items;

    internal Relation Relation { get; } = new();

    internal Menu Menu { get; }

    internal IGameFileSystem FileSystem { get; }

    /// <summary>
    /// Gets the name of the map loaded last.
    /// </summary>
    public ReadOnlyMemory<byte> MapName => _mapName;

    /// <summary>
    /// Gets the name of the map loaded before the last one.
    /// </summary>
    public ReadOnlyMemory<byte> PreviousMapName => _previousMapName;

    /// <summary>
    /// Gets the size of the map, in map pixels.
    /// </summary>
    public SizeF Size { get; private set; }

    /// <summary>
    /// Gets the top-left corner of the screen on the map.
    /// </summary>
    public Vector2 Shift { get; private set; }

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
    public SpriteCollector Sprites { get; private set; }

    internal GameRandom Random => _rng;

    internal bool CanMonstersAttackNeutralTrains => _flags.HasFlag(MapFlags.MonstersAttackNeutralTrains);

    internal Dictionary<int, Sprite> Handles { get; } = [];

    internal int FramesPerSecond => _framesPerSecond;

    private static ReadOnlySpan<byte> SpriteDataTag => "SPRD"u8;

    internal static ReadOnlySpan<byte> GameSection => "game"u8;

    internal static ReadOnlySpan<byte> GraphSection => "graph"u8;

    /// <summary>
    /// Sets a map file, or a demo file, to load at the start of the next step, before that step's frame runs.
    /// </summary>
    /// <param name="name">The path of the map or demo file, relative to the game folder.</param>
    /// <remarks>The random number generator is seeded with the real time when the load runs, unless a demo supplies the seed.</remarks>
    public void RequestLoad(ReadOnlySpan<byte> name)
    {
        _flags |= MapFlags.LoadPending;
        _pendingMapName = name.ToArray();
    }

    /// <summary>
    /// Loads a map file, or the map a demo file plays, and releases the level the map held before.
    /// </summary>
    /// <param name="name">The path of the map or demo file, relative to the game folder; an empty path loads nothing.</param>
    /// <remarks>The random number generator is seeded with the real time unless a demo supplies the seed. If the file is missing or invalid, or a chunk is missing, the problem is logged and the load ends early with the map still loading.</remarks>
    [EngineRoutine(0x0040c960)]
    public void Load(ReadOnlySpan<byte> name)
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
        if (_demoFile is null)
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
            Session.Error.Window($"!!!ERROR!!!LOAD: Invalid map file {MyError.Decode(name)}");
            return;
        }

        Mouse.Disable();
        _previousMapName = _mapName;
        _mapName = name.ToArray();
        int seed = IsPlayingDemo ? _playedDemo!.Value.Seed : (int)Session.TimeGetTime();
        _rng.Seed(seed);
        ShowLoadingStep("Load extra vid"u8);
        LoadVid(ref reader);
        if (reader.GoBegin("GRPH"u8) != Resource.Found)
        {
            if (reader.GoBegin(HeadTag) != Resource.Found)
            {
                LogMissingSection(HeadTag);
                return;
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
            int time = CurrentTime;
            reader.ReadInto(ref time);
            _levelStartTime = time;
            Session.PreviousCurrentTime = time;
            Session.CurrentTime = time;
            int version = 0;
            reader.ReadInto(ref version);
            Session.Graphics.OldLoadParameters(ref reader, this);
            Sprites = new SpriteCollector(Size, Vids, this);
            ResetGroundZ();
            LoadGridZ(ref reader, (int)(Size.Height + OldGridRounding) / GridZCellSize * ((int)(Size.Width + OldGridRounding) / GridZCellSize) * sizeof(short));
            if (!FindSection(ref reader, SpriteTag))
                return;

            while (TryLoadOldSprite(ref reader, out _))
            {
            }

            if (!FindSection(ref reader, SpriteDataTag))
                return;

            while (TryReadPointer(ref reader, out var sprite))
            {
                sprite?.Action(ActionCode.RestoreOld, ref reader, version);

                reader.GoNextSub(SpriteDataTag);
            }
        }
        else
        {
            ShowLoadingStep("Load graph parameters"u8);
            Session.Graphics.Load(ref reader, this);
            if (!FindSection(ref reader, HeadTag))
                return;

            float width = Size.Width;
            reader.ReadInto(ref width);
            float height = Size.Height;
            reader.ReadInto(ref height);
            Size = new SizeF(width, height);
            float scrollX = 0;
            reader.ReadInto(ref scrollX);
            float scrollY = 0;
            reader.ReadInto(ref scrollY);
            var scroll = new Vector2(scrollX, scrollY);
            int savedTime = CurrentTime;
            reader.ReadInto(ref savedTime);
            Session.PreviousCurrentTime = LevelStartPreviousTime;
            Session.CurrentTime = LevelStartTime;
            if (!IsPlayingDemo && _demoFile is not null)
            {
                _recordedDemo = new MemoryStream();
                _flags |= MapFlags.RecordingDemo;
                _recordedDemo.Write(name);
                _recordedDemo.WriteByte(0);
                WriteDemo(_recordedDemo, new DemoHeader(seed, CurrentTime));
            }

            if (IsPlayingDemo)
            {
                SetTime(_playedDemo!.Value.StartTime);
            }

            _levelStartTime = CurrentTime;
            int version = 0;
            reader.ReadInto(ref version);
            if (version < MapHeader.FloatVersion)
            {
                Size = new SizeF(BitConverter.SingleToInt32Bits(Size.Width), BitConverter.SingleToInt32Bits(Size.Height));
                scroll = new Vector2(BitConverter.SingleToInt32Bits(scroll.X), BitConverter.SingleToInt32Bits(scroll.Y));
            }

            Session.Error.Log($"CurrentTime   ={(uint)CurrentTime,-15}   sizeof(SPRITE)={SpriteRecordSize,-8} Map version   ={version}");
            ShowLoadingStep("Create new hash table"u8);
            Sprites = new SpriteCollector(Size, Vids, this);
            SetShiftCoordinates(Session.Graphics.ScreenSize.ToVector2() * HalfShare + scroll, ShiftMove.None);
            ShowLoadingStep("Load gridZ"u8);
            ResetGroundZ();
            LoadGridZ(ref reader, _gridColumns * _gridRows * sizeof(short));
            if (!FindSection(ref reader, SpriteTag))
                return;

            ShowLoadingStep("Load hardware terrain"u8);
            var shownLayer = ObjectLayer.Ground;
            while (TryLoadSprite(ref reader, version, out var loadedSprite))
            {
                if (Constant.IsDebugMode && loadedSprite!.Vid.Layer != shownLayer)
                {
                    shownLayer = loadedSprite.Vid.Layer;
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
            // TODO: Port the trains; the loader builds the rail map (0x43c510) after the terrain and the sprite records, and the Engine links (0x454530) after the sprites
            ShowLoadingStep("Load data for sprite"u8);
            if (!FindSection(ref reader, SpriteDataTag))
                return;

            while (TryReadPointer(ref reader, out var dataSprite))
            {
                Session.Graphics.DrawLoadBar(this, _vids[0]);
                dataSprite?.Action(ActionCode.Restore, ref reader, version);

                reader.GoNextSub(SpriteDataTag);
            }
            ShowLoadingStep("Load players info"u8);
            if (!FindSection(ref reader, "PLAY"u8))
                return;

            foreach (var player in _players)
            {
                player.Load(ref reader);
            }

            ShowLoadingStep("Load groups info"u8);
            if (!FindSection(ref reader, "GROU"u8))
                return;

            LoadGroups(ref reader);
        }

        _flags &= ~MapFlags.Loading;
        Relation.Release();
        Session.Error.Log($"Vid    release {Session.TextureMemoryInUse} {Session.VidMemoryInUse}");

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

            if (entry.Kind != ScriptObjectKind.Function)
                continue;

            var functionName = objects.Name(i).Span;
            int eventFunction = Encoding.ASCII.GetString(functionName) switch
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

            if (eventFunction >= 0)
            {
                _eventFunctionNumbers[eventFunction] = i;
            }

            bool hasShortId = functionName.Length >= IdDigits + 2 && functionName[0] == FunctionPrefix;
            if (hasShortId)
            {
                foreach (byte digit in functionName.Slice(1, IdDigits))
                {
                    if (!CRuntime.IsDigit(digit))
                    {
                        hasShortId = false;
                        break;
                    }
                }
            }

            if (!hasShortId)
                continue;

            if (functionName[IdDigits + 1] == NameSeparator)
            {
                var rest = functionName[(IdDigits + 2)..];
                var vid = FindVid((KnownObject)CRuntime.ParseInteger(functionName.Slice(1, IdDigits)));

                if (rest.StartsWith("DAMAGE"u8))
                {
                    if (entry.Count == DamageParameters && vid is not null)
                    {
                        vid.DamageHandler = i;
                    }

                    continue;
                }

                if (rest.StartsWith("DESTROY"u8))
                {
                    if (entry.Count == DestroyParameters && vid is not null)
                    {
                        vid.DestroyHandler = i;
                    }

                    continue;
                }

                if (rest.StartsWith("COLLISION"u8))
                {
                    if (entry.Count == CollisionParameters && vid is not null)
                    {
                        vid.CollisionHandler = i;
                    }

                    continue;
                }

                if (!rest.IsEmpty && entry.Count == StateHandlerParameters && vid is not null)
                {
                    int state = rest.Length == 1 ? rest[0] - '0' : (rest[0] - '0') * 10 + rest[1] - '0';

                    if (state is >= 0 and < ASToolkit.Engine.Vid.StateCount)
                    {
                        vid.StateHandlers[state] = i;
                    }
                }

                continue;
            }

            if (functionName.Length <= LongIdDigits + 1)
                continue;

            bool hasLongId = true;
            foreach (byte digit in functionName.Slice(1, LongIdDigits))
            {
                if (!CRuntime.IsDigit(digit))
                {
                    hasLongId = false;
                    break;
                }
            }

            if (!hasLongId || functionName[LongIdDigits + 1] != NameSeparator)
                continue;

            var longVid = FindVid((KnownObject)CRuntime.ParseInteger(functionName.Slice(1, LongIdDigits)));
            var longState = functionName[(LongIdDigits + 2)..];
            if (!longState.IsEmpty && entry.Count == StateHandlerParameters && longVid is not null)
            {
                int state = longState.Length == 1 ? longState[0] - '0' : (longState[0] - '0') * 10 + longState[1] - '0';

                if (state is >= 0 and < ASToolkit.Engine.Vid.StateCount)
                {
                    longVid.StateHandlers[state] = i;
                }
            }
        }

        if (IsPlayingDemo)
        {
            _playedOffset = Script.LoadVar(_playedDemo!.Value.Body);
        }
        else if (_flags.HasFlag(MapFlags.RecordingDemo))
        {
            Script.WriteState(_recordedDemo!);
        }

        Session.RealCurrentTime = Session.TimeGetTime();
        ShowLoadingStep("Run scripts for create sprites"u8);
        if (!_flags.HasFlag(MapFlags.AppMenu))
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

        if (!IsPlayingDemo)
        {
            Mouse.Enable(FileSystem);
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
    public Sprite? Flagman(int army) => _players[army & ASToolkit.Engine.Vid.ArmyMask].Flagman;

    /// <summary>
    /// Sets the player a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <param name="sprite">The player, or null to clear the slot.</param>
    [EngineRoutine(0x0040e600)]
    public void SetFlagman(int army, Sprite? sprite) => _players[army & ASToolkit.Engine.Vid.ArmyMask].SetFlagman(sprite);

    /// <summary>
    /// Waits for the real time to move on, advances the game time, compacts the update lists, loads a map if one was requested and processes the window messages.
    /// </summary>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0040f210)]
    public bool StartTact()
    {
        uint elapsed;
        do
        {
            elapsed = Session.TimeGetTime() - Session.RealCurrentTime;
        }
        while (elapsed == 0);

        Session.PreviousRealCurrentTime = Session.RealCurrentTime;
        Session.RealCurrentTime = Session.TimeGetTime();
        uint step = Math.Min(elapsed, MaxRealStep);
        Session.PreviousCurrentTime = Session.CurrentTime;
        Session.CurrentTime += (int)(step * _speed);
        _framesThisSecond++;
        if (Session.RealCurrentTime - Session.FrameRateStartedAt >= FrameRateWindow)
        {
            Session.FrameRateStartedAt = Session.RealCurrentTime;
            _framesPerSecond = _framesThisSecond;
            _framesThisSecond = 0;
        }

        Input.Tact();
        if ((CurrentTime & CompactionPeriodMask) == 0)
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
            Load(_pendingMapName.Span);
        }

        while (Input.TryPeekMessage(out var message))
        {
            if (message.Id == WindowMessageId.Quit)
                return true;

            bool isTranslated = false;
            if (_hasWindow && message.Id is (WindowMessageId.KeyDown or WindowMessageId.SysKeyDown))
            {
                foreach (var accelerator in Accelerators)
                {
                    if (!accelerator.Flags.HasFlag(AcceleratorFlags.VirtualKey) || accelerator.Key != (VirtualKey)message.WParam ||
                        accelerator.Flags.HasFlag(AcceleratorFlags.Shift) != Input.IsKeyHeld(VirtualKey.Shift) ||
                        accelerator.Flags.HasFlag(AcceleratorFlags.Control) != Input.IsKeyHeld(VirtualKey.Control) ||
                        accelerator.Flags.HasFlag(AcceleratorFlags.Alt) != Input.IsKeyHeld(VirtualKey.Alt))
                        continue;

                    DispatchMessage(new WindowMessage(WindowMessageId.Command, (int)accelerator.Command | AcceleratorSource, 0));
                    isTranslated = true;

                    break;
                }
            }

            if (!isTranslated)
            {
                DispatchMessage(message);
            }
        }

        return false;
    }

    /// <summary>
    /// Runs one frame of the map.
    /// </summary>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0041f0e0)]
    public virtual bool Tact() => false;

    /// <summary>
    /// Releases the map, its level and its object IDs.
    /// </summary>
    public void Dispose() => Destruct();

    [EngineRoutine(0x0040b180)]
    private protected virtual void Destruct()
    {
        for (int i = _vidCount - 1; i >= 0; i--)
        {
            if (_vids[i] is not null)
            {
                _vids[i]!.Destruct(Session);
                _vids[i] = null;
            }
        }

        _vidCount = 0;
        Session.Error.Log($"Vid    release {Session.TextureMemoryInUse} {Session.VidMemoryInUse}");
        _mouseTips.Clear();
        CloseDemoFile();
    }

    /// <summary>
    /// Gets the sprites of the update list of a layer.
    /// </summary>
    /// <param name="layer">The layer, from 0 to 16.</param>
    /// <returns>The sprites in the list's order, with <see langword="null"/> where a sprite was removed.</returns>
    public MyList<Sprite?> GetLayer(int layer) => _layers[layer];

    internal void SetMonstersAttackNeutralTrains(bool attack) => SetFlag(MapFlags.MonstersAttackNeutralTrains, attack);

    internal bool IsFree(Vid vid, Vector3 position) => Sprites.CanPlace(vid, position) is null;

    [EngineRoutine(0x0040c570)]
    internal virtual void Release()
    {
        for (int i = 0; i < _vidCount; i++)
        {
            var vid = _vids[i];

            if (vid is null)
                continue;

            var counts = vid.Counts;
            if (counts[0] + counts[1] + counts[2] + counts[3] == 0)
                continue;

            Session.Error.Log($"NoVid[{i,3}]={counts[0]} {counts[1]} {counts[2]} {counts[3]} {vid.Name} Layer={(int)vid.Layer} {vid.Path}");
        }

        Relation.Release();
        _speed = NormalSpeed;
        _framesPerSecond = 0;
        _framesThisSecond = 0;
        Session.Graphics.Movie.Release();
        Session.Graphics.SetWind(ReleaseWindSpeed, new Angle(ReleaseWindDirection));
        Session.Graphics.SetEnvironment((MapEnvironment)(-1));
        if (IsPlayingDemo)
        {
            Mouse.Enable(FileSystem);
        }

        _playedDemo = null;
        _playedOffset = 0;
        Session.DemoPacedAt = 0;
        Session.DemoPacedTime = 0;
        EndDemoRecording();
        _flags &= ~(MapFlags.RecordingDemo | MapFlags.PlayingDemo);
        _scrollType = ScrollType.FromBorder;
        Session.Error.Log("Player release"u8);
        foreach (var player in _players)
        {
            player.Reset();
        }

        Session.Error.Log("Sprite release"u8);
        // TODO: Port the trains; the release sets the flag (DAT_005da58c) that makes the Engine destructors (0x44e9f0, 0x450e20) skip their scripts while it destroys the sprites
        foreach (var player in _players)
        {
            player.Messages.Clear();
        }

        DeleteAll();

        Session.Error.Log("Menu   release"u8);
        if (Menu.Count != 0)
        {
            Session.Error.Error($"SPRITE {(int)Menu[0].Vid.Id}", LogError.Text, 0, "Menu sprite exist after delete"u8);
            Menu.DeleteAll();
        }

        int elapsed = CurrentTime - _levelStartTime;
        Session.Error.Log($"Average fps={(elapsed == 0 ? 0 : (int)((uint)(_stepCount * 1000) / (uint)elapsed))}");
        Session.Error.Log("Script release"u8);
        foreach (var file in _scriptFiles.Values)
        {
            file.Dispose();
        }

        _scriptFiles.Clear();
        Script.Release();
        _stepCount = 0;
        DeleteExtraVid();

        foreach (var vid in Vids)
        {
            vid?.ResetSprites();
        }

        for (int i = 0; i < _eventFunctionNumbers.Length; i++)
        {
            _eventFunctionNumbers[i] = i + NoEventFunctionBase;
        }
    }

    [EngineRoutine(0x00406f20)]
    private static ReadOnlySpan<byte> GetBeforeLast(ReadOnlySpan<byte> text, ReadOnlySpan<byte> delimiter)
    {
        int index = text.LastIndexOf(delimiter);

        return index < 0 ? text : text[..index];
    }

    private void LoadGridZ(ref Resource reader, int expectedSize)
    {
        if (reader.GoNext("GRID"u8) == Resource.Found)
        {
            int count = reader.SubLoad(Session.Error, out byte[] grid);
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

    private bool FindSection(ref Resource reader, ReadOnlySpan<byte> tag)
    {
        if (reader.GoNext(tag) == Resource.Found)
            return true;

        LogMissingSection(tag);
        return false;
    }

    internal static int OpenObjects(IGameFileSystem fileSystem, MyError? error, ref Resource resource)
    {
        var config = new Profile(fileSystem);
        config.Load(ConfigFile);
        var name = config.TryGetString(GameSection, "Resource"u8, out var configured) ? configured.Span : "objects.res"u8;

        return resource.Open(fileSystem, error, name, "DATA"u8);
    }

    private void LogMissingSection(ReadOnlySpan<byte> tag) => Session.Error.Error(MapLogTag, LogError.Section, 0, tag);

    private protected static void OverrideGamma(Sprite sprite, Gamma gamma)
    {
        sprite.SetGamma(gamma);
        var link = sprite.Link;

        if (link is not null && link.Vid == sprite.Vid.LinkVid)
        {
            link.SetGamma(gamma);
        }
    }

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
                _vids[i]!.Destruct(Session);
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
                sprite = new SText(vid, position, direction, parent, this);
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
        if (set.CheckedAt == CurrentTime)
            return set.Sprites;

        set.CheckedAt = CurrentTime;
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

    private void SetTime(int time) => Session.CurrentTime = time;

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
    internal void PauseOn()
    {
        if (!IsPaused)
        {
            _pausedAt = CurrentTime;
        }

        _flags |= MapFlags.Paused;
    }

    [EngineRoutine(0x0040fa00)]
    private void PauseOff()
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

        Session.CurrentTime = _pausedAt;
        Session.PreviousCurrentTime = _pausedAt - ResumeFrameTime;
        _flags &= ~MapFlags.Paused;
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

        int index = removed.ReferenceCount > 1 ? Sprites.Listed.Length - 1 : -1;
        while (index >= 0)
        {
            Sprites.Listed[index].DeletePointerToSprite(removed);

            if (Sprites.Listed.Length < index)
            {
                index = Sprites.Listed.Length;
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
}
