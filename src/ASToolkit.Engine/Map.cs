using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Text;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the game world, holding one loaded level at a time and running it frame by frame.
/// </summary>
public sealed partial class Map : IDisposable
{
    /// <summary>
    /// Represents the number of sprite layers of a map.
    /// </summary>
    public const int LayerCount = 17;

    /// <summary>
    /// Represents the lowest height a blast reaches above its center.
    /// </summary>
    public const float MinBlastHeight = 20;
    private const int SpriteRecordSize = 112;
    private const float OldGridRounding = 7;
    private const int MaxStreamPeriod = 30000;
    private const int ChildPairCount = 2;
    private const int AimAttempts = 5;
    private const float AimRandomScale = 6.1037019E-05f;
    private const float RescueEffectHeight = 22;
    private const int WornArmorShareCount = 3;
    private const int ArmorRounding = 50;
    private const int StartRandomState = 1;
    private const string ScriptExtension = ".lgc";
    private const string CompiledScriptExtension = ".lgd";
    private const int SpawnScatter = 8;
    private const float RegionReach = 25f;
    private const int HashedSpritesKey = 'G';
    private const int HashedNumbersKey = 'H';
    private const int FlagmanKey = 'I';
    private const int GridKey = 'O';
    private const int SingleStepKey = 'P';
    private const int RailsKey = 'R';
    private const int ReloadKey = '~';
    private const int StepKey = 'p';
    private const uint PresentationPeriod = 2000;
    private const int DemoStopResult = 999999;
    private const float PresentationTop = 32;
    private const int PresentationDuration = 1000;
    private const int ScriptHookCount = 64;
    private const int NoHookBase = 1000000;
    private const int ReleaseWindSpeed = 0x19;
    private const int ReleaseWindDirection = 200;

    private static readonly SizeF StartMapSize = new(640, 480);
    private readonly GameRandom _rng;
    private readonly List<Sprite> _menuSprites = [];
    private readonly IGameFileSystem _fileSystem;
    private readonly Dictionary<int, ScriptFile> _scriptFiles = [];
    private readonly ControlSettings _controls;
    private readonly Group _groupHead = new(null, null);
    private readonly Player[] _players = new Player[SpriteDefinition.ArmyCount];
    private readonly LivingSet _livingFences = new();
    private readonly LivingSet _livingSites = new();
    private readonly LivingSet _livingCharges = new();
    private readonly int[] _scriptHooks = new int[ScriptHookCount];
    private UpdateOrder _updates;
    private int _pausedAt;
    private MenuClicks _menuClicks;
    private Sprite? _menuCurrent;
    private GameClock _clock;
    private MapFlags _flags;
    private StepFlags _debug;
    private uint _presentationShownAt;
    private int _stepCount;
    private bool _hasWindow;
    private int _levelStartTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="Map"/> class with no level loaded.
    /// </summary>
    /// <param name="definitions">The sprite definitions of the game.</param>
    /// <param name="fileSystem">The file system that holds the game's files.</param>
    /// <param name="registry">The registry of the level scripts, or null for an empty one.</param>
    /// <param name="session">The session the map belongs to, or null to create one.</param>
    [EngineRoutine(0x00408ff0)]
    public Map(SpriteCatalog definitions, IGameFileSystem fileSystem, ScriptRegistry? registry = null, GameSession? session = null)
    {
        Registry = registry ?? new ScriptRegistry("");
        Session = session ?? new GameSession(Registry);
        SetFlag(MapFlags.AppMenu, Session.HasAppMenu);
        _flags |= MapFlags.PlayerControlled | MapFlags.DrawFlagman | MapFlags.DrawSoundCount | MapFlags.MouseTips;
        Session.ReadConfig(fileSystem);
        SetFlag(MapFlags.DrawFps, Session.ShouldDrawFps);
        SetFlag(MapFlags.DrawPresentation, Session.ShouldDrawPresentation);
        _hasWindow = true;
        _fileSystem = fileSystem;
        Definitions = definitions;
        _rng = new GameRandom(StartRandomState);
        Script.Logger = Session.Logger;
        _updates = new UpdateOrder();
        _clock = new GameClock();
        Size = StartMapSize;
        Movement = new MovementMap(Size);
        Collector = new SpriteCollector(Size, definitions.Table);
        Sound = Session.Sound ??= new SoundSystem(definitions.Sounds, fileSystem, Session.Logger, isHighQuality: Session.IsSoundHighQuality);
        LoadObjectRecords(false);
        CursorSprite = ConstructCursor();
        _controls = ControlSettings.Read(fileSystem);
        Input = new PlayerInput(_controls);
        BuildSystems();
        for (int slot = 0; slot < SpriteDefinition.ArmyCount; slot++)
        {
            _players[slot] = new Player(this, _controls);
        }

        _flags |= MapFlags.Initialized;
    }

    /// <summary>
    /// Gets the player the first player slot controls.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no player in the first player slot.</exception>
    public Sprite Player => GetFlagman(0) ?? throw new InvalidOperationException("There is no player in the first player slot.");

    /// <summary>
    /// Gets a value indicating whether the first player slot has a player.
    /// </summary>
    public bool HasPlayer => GetFlagman(0) is not null;

    /// <summary>
    /// Gets the input of the current frame.
    /// </summary>
    public PlayerInput Input { get; }

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
    /// Gets the sprite definitions of the game.
    /// </summary>
    public SpriteCatalog Definitions { get; }

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

    /// <summary>
    /// Gets a value indicating whether the level script has locked the doors.
    /// </summary>
    public bool AreDoorsLocked => ReadScriptNumber(DoorsOpenVariable) == 0;

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
    public SoundSystem Sound { get; }

    /// <summary>
    /// Gets the menu sprites that keep their place on the screen as the camera moves.
    /// </summary>
    public IReadOnlyList<Sprite> MenuSprites => _menuSprites;

    /// <summary>
    /// Gets the size of the map, in map pixels.
    /// </summary>
    public SizeF Size { get; private set; }

    /// <summary>
    /// Gets the movement map, with the ground heights of the map.
    /// </summary>
    public MovementMap Movement { get; }

    /// <summary>
    /// Gets the spatial hash of the sprites of the map.
    /// </summary>
    public SpriteCollector Collector { get; private set; }

    internal GameRandom Random => _rng;

    internal bool CanMonstersAttackNeutralTrains => _flags.HasFlag(MapFlags.MonstersAttackNeutralTrains);

    internal WeaponSystem Weapons { get; private set; }

    internal SpriteCollision Collision { get; private set; }

    internal PlayerMovement PlayerMovement { get; private set; }

    internal ProjectileCollision Projectiles => Weapons.Collision;

    internal SpawnedObjectSystem Effects { get; private set; }

    internal SpriteKinematics Motion { get; private set; }

    internal MonsterBrain Brain { get; private set; }

    internal int FramesPerSecond => _clock.FramesPerSecond;

    private static ReadOnlySpan<int> WornArmorShares => [50, 70, 90];

    private static ReadOnlySpan<byte> PresentationText => "Presentation version. Not for sale!"u8;

    private static ReadOnlySpan<byte> DoorsOpenVariable => "DoorsIsOpen"u8;

    private static ReadOnlySpan<byte> SpriteExistsText => "Sprite exist after delete"u8;

    private static ReadOnlySpan<byte> MenuSpriteExistsText => "Menu sprite exist after delete"u8;

    private static ReadOnlySpan<byte> IncorrectGroupsText => "Incorrect delete groups in DeleteAll()"u8;

    private static ReadOnlySpan<byte> MapKind => "MAP "u8;

    private static ReadOnlySpan<byte> DemoKind => "DEMO"u8;

    private static ReadOnlySpan<byte> GraphicsTag => "GRPH"u8;

    private static ReadOnlySpan<byte> GridTag => "GRID"u8;

    private static ReadOnlySpan<byte> SpriteDataTag => "SPRD"u8;

    private static ReadOnlySpan<byte> PlayersTag => "PLAY"u8;

    private static ReadOnlySpan<byte> GroupsTag => "GROU"u8;

    private static ReadOnlySpan<byte> GridText => "grid"u8;

    private static ReadOnlySpan<byte> ExtensionMark => "."u8;

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
        _menuClicks = default;
        _livingFences.Clear();
        _livingSites.Clear();
        _livingCharges.Clear();
        _hardwareGround = null;
        if (!Session.IsRecordingNextMap)
        {
            var demo = new Resource();
            if (demo.Open(_fileSystem, Session.Logger, name, DemoKind) == 0)
            {
                _fileSystem.TryRead(Encoding.ASCII.GetString(name), out var data);
                name = demo.ReadText();
                _playedDemo = new DemoResource(data);
                _flags |= MapFlags.PlayingDemo;
            }
        }

        var reader = new Resource();
        if (reader.Open(_fileSystem, Session.Logger, name, MapKind) != 0)
        {
            Session.Logger.Alert($"!!!ERROR!!!LOAD: Invalid map file {GameLogger.Decode(name)}");
            return;
        }

        CursorSprite.TurnCursorOff();
        Session.EnterMap(name.ToArray());
        int seed = IsPlayingDemo ? _playedDemo!.Value.Seed : (int)wallTime;
        _rng.Seed(seed);
        ShowLoadingStep("Load extra vid"u8);
        LoadObjectRecords(true);
        if (reader.FindChunk(GraphicsTag) != Resource.Found)
        {
            if (!LoadOldTypeMap(ref reader))
                return;
        }
        else if (!LoadNewTypeMap(ref reader, name, seed))
            return;

        _flags &= ~MapFlags.Loading;
        _spriteRelations.Clear();
        // TODO: Port the "Vid    release %i %i" log line (0x40d3f0) that writes the texture bytes the renderer holds and the bytes of the loaded definition data
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
            CursorSprite.LoadCursor(_fileSystem);
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
            Session.Logger.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Invalid, count, "noRef at Release"u8);
            return;
        }

        DeleteSprite(sprite);
    }

    /// <summary>
    /// Returns the state a new sprite of a definition starts in.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The starting state.</returns>
    public SpriteAnimation GetInitialAnimation(SpriteDefinition definition)
    {
        return IsLoading && definition.InitialAnimation == SpriteAnimation.Birth ? SpriteAnimation.Stand : definition.InitialAnimation;
    }

    /// <summary>
    /// Runs one frame of the level, advancing the clock and playing or recording the demo frame first.
    /// </summary>
    /// <param name="wallTime">The wall time of the frame in milliseconds.</param>
    /// <param name="now">The real time in milliseconds that paces a played demo.</param>
    /// <param name="waitUntil">The real time to wait for before the frame, or <paramref name="now"/> when the frame is due.</param>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The frame doesn't run while the window is inactive or a menu is open, unless the debug keys allow it.</remarks>
    [EngineRoutine(0x00405190)]
    public bool Step(uint wallTime, uint now, out uint waitUntil)
    {
        waitUntil = now;

        if (AdvanceClock(wallTime))
            return true;

        if (Session.IsDebugMode)
        {
            HandleDebugKey(Input.Key);
        }

        bool isActive = _flags.HasFlag(MapFlags.WindowActive);

        if (!isActive && !_debug.HasFlag(StepFlags.RunInBackground) || Session.Graphics.IsMenuLoopActive || Session.IsDebugMode && _debug.HasFlag(StepFlags.SingleStep) && Input.Key != StepKey)
        {
            _clock.Rewind();

            return false;
        }

        bool canDraw = !(!isActive && Session.IsFullScreen || Session.Graphics.IsMovieOpen);
        if (canDraw && Session.Graphics.PreTact() != 0)
            return false;

        _stepCount++;
        int demoResult = StepDemo(now, out waitUntil) ? 1 : 0;

        if (demoResult == DemoStopResult)
        {
            Session.Graphics.PostTact(true);

            return false;
        }

        bool draw = demoResult != 0 && canDraw;
        ProcessMenuInput(Input);
        CallScript(MainFunction, null);

        if (draw && Session.Graphics.IsMovieOpen)
        {
            draw = false;
            canDraw = false;
        }

        SetGammaOverrides(false);
        Session.Graphics.UpdateScreen(this, draw);
        SetGammaOverrides(true);

        bool isPaused = _flags.HasFlag(MapFlags.Paused);

        if (isPaused)
        {
            TickMenuSprites();
        }
        else
        {
            TickUpdatedSprites();
        }

        TactMouseTip(Input);
        Scroll();

        if (!isPaused && _flags.HasFlag(MapFlags.PlayerControlled))
        {
            if (_flags.HasFlag(MapFlags.DrawPresentation) && _clock.WallTime - _presentationShownAt > PresentationPeriod)
            {
                ShowPresentation();
            }

            foreach (var controller in _players)
            {
                controller.Control(Input);
            }
        }

        if (draw)
        {
            DrawOverlay();
        }

        if (canDraw)
        {
            Session.Graphics.PostTact(true);
        }

        Sound.Tact(_rng, Time, _clock.WallTime);

        return false;
    }

    /// <summary>
    /// Releases the loaded level.
    /// </summary>
    [EngineRoutine(0x00405ac0)]
    public void Dispose()
    {
        _flags &= ~MapFlags.MonstersAttackNeutralTrains;
        DeleteMouseTip();
        Release();
    }

    /// <summary>
    /// Plays a sound from a sprite's position on the screen.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="id">The sound ID.</param>
    [EngineRoutine(0x00445470)]
    public void PlaySfx(Sprite sprite, int id)
    {
        Sound.PlayAt(id, sprite.Position.ToDrawPosition() - Camera - Session.ScreenSize.ToVector2() * HalfShare);
    }

    /// <summary>
    /// Returns the player a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <returns>The player, or null if the slot is empty.</returns>
    [EngineRoutine(0x0040e620)]
    public Sprite? GetFlagman(int army) => _players[army & SpriteDefinition.ArmyMask].Sprite;

    /// <summary>
    /// Sets the player a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <param name="sprite">The player, or null to clear the slot.</param>
    [EngineRoutine(0x0040e600)]
    public void SetFlagman(int army, Sprite? sprite) => _players[army & SpriteDefinition.ArmyMask].SetFlagman(sprite);

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
        _updates.Compact(Time);
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
    /// Updates a sprite for one frame.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns><see langword="true"/> if the sprite stays in the map; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00440110)]
    public bool Tact(Sprite sprite)
    {
        if (sprite.Definition.HasKeyframes)
        {
            AdvanceKeyframes(sprite);
        }

        if (sprite.Owner is null)
        {
            int lastTickTime = sprite.LastTickTime;
            sprite.LastTickTime = _clock.PreviousTime;
            sprite.MoveTact(this);
            sprite.LastTickTime = lastTickTime;
        }

        RaiseHeights(sprite.Definition, sprite);
        EmitStreamChild(sprite);
        var state = sprite.Animation;

        if (sprite.CreatedAt == Time || Time - Time % sprite.Definition.GetVisualFrameDuration(state) <= Time - FrameTime)
            return true;

        if (sprite.Command is not Command.None && sprite is { Command: < Command.Build, HasGoal: false })
        {
            Session.Logger.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Text, (int)sprite.Command, "command need goal, but goal==NULL"u8);
            SpriteKinematics.SetCommand(sprite, Command.None);
        }

        if (sprite.Timer != 0)
        {
            sprite.Timer = (uint)(Time - sprite.LastTickTime) < (uint)sprite.Timer ? sprite.Timer - (Time - sprite.LastTickTime) : 0;

            if (sprite is { Timer: 0, Command: Command.Pause })
            {
                SpriteKinematics.SetCommand(sprite, Command.None);
            }
        }

        bool everyFrame = sprite.Definition.Object.Properties.HasFlag(ObjectProperties.Track);

        if (CallStateHandlers(sprite, everyFrame))
            return true;

        int sound = sprite.Definition.Sounds[(int)sprite.Animation];

        if (sound != 0 && (!sprite.HasPlayedStateSound || Sound.IsLooped(sound)))
        {
            sprite.HasPlayedStateSound = true;
            PlaySfx(sprite, sound);
        }

        CreateFrameChild(sprite);
        sprite.Frame++;

        if (sprite.Animation >= SpriteAnimation.Death && (sprite.Frame > sprite.LastFrame || sprite.Definition.GetFrameCount(sprite.Animation) == 0))
        {
            sprite.Frame = sprite.LastFrame;

            Action(sprite, ActionCode.Death);

            return false;
        }

        AdvanceOrders(sprite, everyFrame);
        TickLifetime(sprite);
        sprite.LastTickTime = Time;

        if (sprite.Frame > sprite.LastFrame)
        {
            sprite.Frame = sprite.FirstFrame;
        }

        return true;
    }

    /// <summary>
    /// Creates the child objects a sprite's definition lists for a state.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="state">The state whose children to create.</param>
    [EngineRoutine(0x00446430)]
    public void CreateChildFor(Sprite sprite, SpriteAnimation state)
    {
        if (Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)state])) is not null)
        {
            CreateChildAs(sprite, state);
        }

        int sound = sprite.Definition.Sounds[(int)state];

        if (sound != 0)
        {
            PlaySfx(sprite, sound);
        }
    }

    /// <summary>
    /// Gets the sprites of the update list of a layer.
    /// </summary>
    /// <param name="layer">The layer, from 0 to 16.</param>
    /// <returns>The sprites in the list's order, with <see langword="null"/> where a sprite was removed.</returns>
    public IReadOnlyList<Sprite?> GetLayer(int layer) => _updates.GetLayer(layer);

    /// <summary>
    /// Creates a sprite of a definition, or of the definition that a region at the position replaces it with, and runs the creation handler of
    /// the sprite's definition.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, and links to if the new sprite is a linker.</param>
    /// <returns>The sprite, or <see langword="null"/> if <paramref name="definition"/> is null, its limit is reached or its class isn't valid.</returns>
    [EngineRoutine(0x00405bd0)]
    public Sprite? AddSprite(SpriteDefinition? definition, Vector3 position, Angle direction, Sprite? parent = null)
    {
        if (definition is null)
            return null;

        if (definition.IsRegionReplaced)
        {
            definition = ReplaceDefinition(definition, position);
        }

        if (definition.MaxUnits >= 0 && definition.MaxUnits <= Sum(definition.Counts))
            return null;

        Sprite sprite;

        switch (definition.Class)
        {
            case SpriteClass.Terrain or SpriteClass.Object:
                sprite = ConstructTerrain(new Terrain(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Building:
                sprite = ConstructBuilding(new Building(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.CivRobot:
                sprite = ConstructCivRobot(new CivRobot(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Engine:
                sprite = ConstructEngine(new Engine(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Rail:
                sprite = ConstructRail(new Rail(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Depo:
                sprite = ConstructDepo(new Depo(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Creature:
                sprite = ConstructCreature(new Creature(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Balloon:
                sprite = ConstructBalloon(new Balloon(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            default:
                return CreateSprite(definition, position, direction, parent);
        }

        if (!_flags.HasFlag(MapFlags.Loading))
        {
            RunCreationHandler(sprite);
        }

        return sprite;
    }

    internal void SetMonstersAttackNeutralTrains(bool attack) => SetFlag(MapFlags.MonstersAttackNeutralTrains, attack);

    [EngineRoutine(0x0043f200)]
    internal void ConstructSprite(Sprite sprite, Angle direction, Sprite? parent)
    {
        var definition = sprite.Definition;
        ref readonly var gameObject = ref definition.Object;
        var position = sprite.Position;

        if (gameObject.Properties.HasFlag(ObjectProperties.Noise))
        {
            position.X += SpawnScatter - _rng.Next() % (SpawnScatter * 2 + 1);
            position.Y += SpawnScatter - _rng.Next() % (SpawnScatter * 2 + 1);
        }

        if (gameObject.Properties.HasFlag(ObjectProperties.ZeroZ))
        {
            float ground = Movement.GetSupportHeight(gameObject, position.AsVector2());
            position.Z = parent is null ? ground : position.Z - parent.Position.Z + ground;
        }

        sprite.Position = position;

        if (parent is not null)
        {
            sprite.Army = parent.Army;
        }
        else
        {
            var link = definition.LinkDefinition;
            sprite.Army = gameObject.WeaponIndex == 0 && link is not null ? link.Army : definition.Army;
        }

        sprite.CreatedAt = Time;
        sprite.IsInvisible = gameObject.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) && (int)sprite.Army != ControlledPlayer;
        sprite.Hp = definition.ArmyMaxHp[(int)sprite.Army & SpriteDefinition.ArmyMask];
        sprite.Logger = Session.Logger;
        var state = GetInitialAnimation(definition);
        sprite.Start(state, direction, Time, _rng);

        if (definition != Definitions.Empty)
        {
            sprite.AddRef();
            AddToUpdates(sprite);
        }

        CreateLink(sprite);
        Collector.InsertToHash(sprite);
        definition.LastCreatedAt = WallTime;
        definition.Counts[(int)sprite.Army & 3]++;

        if (state != SpriteAnimation.Birth && !IsLoading)
        {
            CreateChildFor(sprite, SpriteAnimation.Birth);
        }

        RaiseHeights(definition, sprite);
    }

    [EngineRoutine(0x004145b0)]
    internal void RaiseHeights(SpriteDefinition definition, Sprite sprite)
    {
        if (sprite == CursorSprite || definition.HeightPoints.Count == 0)
            return;

        bool permanent = !_flags.HasFlag(MapFlags.AppMenu) && definition.Class == SpriteClass.BuiltTerrain;

        foreach (var point in definition.GetHeightPoints(sprite.Frame))
        {
            var cell = new Vector3(sprite.Position.X + point.X, sprite.Position.Y + point.Y, sprite.Position.Z + point.Z);

            if (permanent)
            {
                Movement.RaisePermanent(cell);
            }
            else
            {
                Movement.RaiseTemporary(cell);
            }
        }
    }

    [EngineRoutine(0x004146b0)]
    internal void LowerHeights(SpriteDefinition definition, Sprite sprite)
    {
        if (sprite == CursorSprite || definition.HeightPoints.Count == 0 || !_flags.HasFlag(MapFlags.AppMenu) && definition.Class == SpriteClass.BuiltTerrain)
            return;

        foreach (var point in definition.GetHeightPoints(sprite.Frame))
        {
            Movement.Lower(new Vector3(sprite.Position.X + point.X, sprite.Position.Y + point.Y, sprite.Position.Z + point.Z));
        }
    }

    [EngineRoutine(0x00444dd0)]
    internal void CreateChild(Sprite sprite)
    {
        var definition = sprite.Definition;
        var state = sprite.Animation;
        int slot = (int)state;
        var child = Definitions.Find((KnownObject)Math.Abs(definition.Children[slot]));
        int count = Math.Abs(definition.ChildCounts[slot]);
        int i = 0;

        if (child is null || child.NotCreatedAsChild != 0)
            return;

        var offsetDirection = child.Object.Properties.HasFlag(ObjectProperties.FixedOffset) ? default : definition.GetFrameDirection(sprite.Direction);

        if (count == ChildPairCount && definition.HasDoubleAttack)
        {
            if (sprite.IsAwaitingSecondPulse)
            {
                i = 1;
                sprite.HasPlayedStateSound = false;
            }
            else
            {
                count = 1;
            }

            sprite.IsAwaitingSecondPulse = !sprite.IsAwaitingSecondPulse;
        }

        for (; i < count; i++)
        {
            var shift = GetChildShift(sprite, definition, slot, i, offsetDirection, out var lateral);
            var childPosition = new Vector3(shift.X + sprite.Position.X, shift.Y + sprite.Position.Y, definition.Object.ChildZ[slot] + sprite.Position.Z);

            if (child.Class == SpriteClass.Unit && FindPlacementBlocker(child, childPosition) is not null || state == SpriteAnimation.Fight && sprite.Goal is null)
                continue;

            var childDirection = child.Object.Properties.HasFlag(ObjectProperties.RandomBirth) ? new Angle((byte)(_rng.Next() & byte.MaxValue)) : sprite.Direction;
            var created = AddSprite(child, childPosition, childDirection, sprite);

            var goal = sprite.Goal;

            if (created is null || goal is null || state != SpriteAnimation.Fight)
                continue;

            LaunchChild(created, goal, definition, lateral);
        }

        if (state == SpriteAnimation.Fight && !sprite.IsAwaitingSecondPulse && sprite.Command is Command.OneAttack or Command.DistAttack &&
            (!definition.Object.Properties.HasFlag(ObjectProperties.Track) && !child.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke) || sprite.Frame == sprite.LastFrame))
        {
            var owner = sprite.Owner;

            if (sprite.Goal is not null && owner is not null && owner.Goal == sprite.Goal)
            {
                if (owner.Command is Command.OneAttack or Command.DistAttack)
                {
                    SpriteKinematics.SetCommand(owner, Command.None);
                }

                // TODO: Port the trains; the engine owner with command 0x1d takes action 0x1e
            }

            SpriteKinematics.SetCommand(sprite, Command.None);
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

    private static void OverrideGamma(Sprite sprite, Gamma gamma)
    {
        sprite.SetGammaOverride(gamma);
        var link = sprite.Link;

        if (link is not null && link.Definition == sprite.Definition.LinkDefinition)
        {
            link.SetGammaOverride(gamma);
        }
    }

    private static float GetKeyTime(in WeaponRecord weapon, int key) =>
        key < 0 ? BitConverter.Int32BitsToSingle(weapon.AnimationPeriod)
        : key < WeaponRecord.KeyframeCount ? weapon.KeyTimes[key] : BitConverter.Int32BitsToSingle(weapon.RedKeys[0]);

    [EngineRoutine(0x00444970)]
    private static int AddLink(Sprite owner, Sprite? link)
    {
        if (link is null || link.Owner is not null)
            return 1;

        var previous = owner.Link;

        if (previous is not null)
        {
            previous.Owner = null;
            SpawnedObjectSystem.AddLinkToLast(link, previous);
        }

        owner.Link = link;
        link.Owner = owner;

        return 0;
    }

    private bool LoadOldTypeMap(ref Resource reader)
    {
        if (reader.FindChunk(HeadTag) != Resource.Found)
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
        Session.Graphics.LoadOld(ref reader, Definitions);
        Collector = new SpriteCollector(Size, Definitions.Table);
        BuildSystems();
        Movement.Initialize(Size);
        LoadGridZ(ref reader, (int)(Size.Height + OldGridRounding) / MovementMap.CellSize * ((int)(Size.Width + OldGridRounding) / MovementMap.CellSize) * sizeof(short));
        if (!FindSection(ref reader, SpriteTag))
            return false;

        while (TryLoadOldSprite(ref reader, out _))
        {
        }

        if (!FindSection(ref reader, SpriteDataTag))
            return false;

        while (TryReadRelatedSprite(ref reader, out var sprite))
        {
            if (sprite is not null)
            {
                RestoreOld(sprite, ref reader, version);
            }

            reader.NextRecord(SpriteDataTag);
        }

        return true;
    }

    private bool LoadNewTypeMap(ref Resource reader, ReadOnlySpan<byte> name, int seed)
    {
        ShowLoadingStep("Load graph parameters"u8);
        Session.Graphics.Load(ref reader, Definitions);
        if (!FindSection(ref reader, HeadTag))
            return false;

        int version = LoadHead(ref reader, name, seed, out var scroll);
        Session.Logger.Write($"CurrentTime   ={(uint)Time,-15}   sizeof(SPRITE)={SpriteRecordSize,-8} Map version   ={version}");
        ShowLoadingStep("Create new hash table"u8);
        Collector = new SpriteCollector(Size, Definitions.Table);
        BuildSystems();
        SetPosition(Session.ScreenSize.ToVector2() * HalfShare + scroll, CameraMove.None);
        ShowLoadingStep("Load gridZ"u8);
        Movement.Initialize(Size);
        LoadGridZ(ref reader, Movement.Columns * Movement.Rows * sizeof(short));
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
        if (!FindSection(ref reader, PlayersTag))
            return false;

        foreach (var player in _players)
        {
            player.Load(ref reader);
        }

        ShowLoadingStep("Load groups info"u8);
        if (!FindSection(ref reader, GroupsTag))
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
        if (reader.FindNextChunk(GridTag) == Resource.Found)
        {
            int count = reader.Load(Session.Logger, out byte[] grid);
            Movement.SetSceneryHeights(grid);
            if (count != expectedSize)
            {
                Session.Logger.Error(MapLogTag, LogError.Invalid, count, GridText);
                Movement.Initialize(Size);
            }
        }
        else
        {
            reader.FindChunk(Resource.AnyTag);
        }
    }

    private void LoadSprites(ref Resource reader, int version)
    {
        var shownLayer = ObjectLayer.Ground;
        while (TryLoadSprite(ref reader, version, out var sprite))
        {
            if (Session.IsDebugMode && sprite!.Definition.Layer != shownLayer)
            {
                shownLayer = sprite.Definition.Layer;
                Session.Graphics.SetLoadingText(shownLayer switch
                {
                    ObjectLayer.Ground => "Load hardware terrain"u8,
                    ObjectLayer.Built => "Build sprites in terrain"u8,
                    ObjectLayer.BuiltAlpha => "Build sprites with alpha in terrain"u8,
                    _ => "Load sprites"u8
                });
            }

            Session.Graphics.DrawLoadBar(this, Definitions.LoadingScreen);
        }
    }

    private void LoadSpriteData(ref Resource reader, int version)
    {
        while (TryReadRelatedSprite(ref reader, out var sprite))
        {
            Session.Graphics.DrawLoadBar(this, Definitions.LoadingScreen);
            if (sprite is not null)
            {
                Restore(sprite, ref reader, version);
            }

            reader.NextRecord(SpriteDataTag);
        }
    }

    private bool FindSection(ref Resource reader, ReadOnlySpan<byte> tag)
    {
        if (reader.FindNextChunk(tag) == Resource.Found)
            return true;

        LogMissingSection(tag);
        return false;
    }

    private void LoadScript(ReadOnlySpan<byte> name)
    {
        ShowLoadingStep("Load script file"u8);
        string stem = GameTextEncoding.Cyrillic.GetString(GetBeforeLast(name, ExtensionMark));
        string scriptPath = stem + ScriptExtension;
        if (!_fileSystem.TryRead(scriptPath, out _))
        {
            string compiledPath = stem + CompiledScriptExtension;
            if (_fileSystem.TryRead(compiledPath, out _))
            {
                scriptPath = compiledPath;
            }
        }

        Script.Load(_fileSystem, scriptPath);
        ShowLoadingStep("Connect script function with VID"u8);
        var objects = Script.Objects;
        for (int i = 0; i < objects.Length; i++)
        {
            ref readonly var entry = ref objects[i];

            if (entry.Kind == ScriptObjectKind.Function)
            {
                int hook = GetScriptHook(entry.Name.Span);

                if (hook >= 0)
                {
                    _scriptHooks[hook] = i;
                }

                ProcessScriptFunctions(entry.Name.Span, i, entry.Count);
            }
        }
    }

    private void RunCreationHandlers()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
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

    private void LogMissingSection(ReadOnlySpan<byte> tag) => Session.Logger.Error(MapLogTag, LogError.Section, 0, tag);

    [EngineRoutine(0x00411440)]
    private void ReleaseAdditionalLoads()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null && sprite.Definition.FileType.HasFlag(VidType.FrameVector))
                {
                    DeleteSprite(sprite);
                }
            }
        }

        Definitions.ReleaseAdditionalLoads();
    }

    private void ShowLoadingStep(ReadOnlySpan<byte> text)
    {
        if (Session.IsDebugMode)
        {
            Session.Graphics.SetLoadingText(text);
        }
    }

    private void SetFlag(MapFlags flag, bool isSet) => _flags = (isSet ? flag : MapFlags.None) | _flags & ~flag;

    private void ShowPresentation()
    {
        var definition = Definitions.Find(KnownObject.Font) ?? Definitions.Empty;
        var banner = AddSprite(definition, new Vector3(Session.ScreenSize.Width * HalfShare, PresentationTop, 0), default)!;
        _presentationShownAt = _clock.WallTime;
        Action(banner, ActionCode.SetBehave, 1);
        SetTextItems(banner, PresentationText.ToArray());
        Action(banner, ActionCode.Pause, PresentationDuration);
        banner.ActionStack.Insert(0, new Act((int)SpriteAnimation.Death, 0, 0, 0));
    }

    [EngineRoutine(0x0044bd60)]
    private Sprite ConstructTerrain(Terrain sprite, Angle direction, Sprite? parent)
    {
        ConstructSprite(sprite, direction, parent);
        sprite.ShouldRefillAmmo = false;

        return sprite;
    }

    [EngineRoutine(0x0040ec00)]
    private Sprite ConstructPrimitive(Primitive sprite, Angle direction, Sprite? parent)
    {
        ConstructSprite(sprite, direction, parent);

        return sprite;
    }

    [EngineRoutine(0x00447130)]
    private Sprite ConstructUnit(Unit unit, Angle direction, Sprite? parent)
    {
        ConstructTerrain(unit, direction, parent);
        unit.TurnTicks = 0;
        unit.Behavior = (unit.ArmedLink ?? unit).Definition.Behavior;
        unit.FillAmmo();

        return unit;
    }

    [EngineRoutine(0x0044ce60)]
    private Sprite ConstructAvia(Avia sprite, Angle direction, Sprite? parent)
    {
        ConstructUnit(sprite, direction, parent);
        Motion.StartMove(sprite);
        sprite.VerticalSpeed = sprite.Definition.ZSpeed;

        return sprite;
    }

    [EngineRoutine(0x0044dd80)]
    private Sprite ConstructBalloon(Balloon sprite, Angle direction, Sprite? parent) => ConstructAvia(sprite, direction, parent);

    [EngineRoutine(0x0044d3e0)]
    private Sprite ConstructCreature(Creature sprite, Angle direction, Sprite? parent)
    {
        ConstructUnit(sprite, direction, parent);
        sprite.IsSeekingRegion = true;
        sprite.VerticalSpeed = sprite.Definition.ZSpeed;
        Motion.StartMove(sprite);
        sprite.Region = FindRegion(sprite.Position.AsVector2());

        return sprite;
    }

    [EngineRoutine(0x0044ca00)]
    private Sprite ConstructBuilding(Building sprite, Angle direction, Sprite? parent)
    {
        ConstructUnit(sprite, direction, parent);
        _builds.Remove(sprite);

        return sprite;
    }

    [EngineRoutine(0x0044a590)]
    private Sprite ConstructCivRobot(CivRobot sprite, Angle direction, Sprite? parent)
    {
        // TODO: Port the rest of the CivRobot constructor (0x44a590)
        return ConstructCreature(sprite, direction, parent);
    }

    [EngineRoutine(0x0044e8b0)]
    private Sprite ConstructEngine(Engine sprite, Angle direction, Sprite? parent)
    {
        // TODO: Port the Engine, Rail and Depo class tails (0x44e8b0, 0x44a140, 0x44c040); the port builds those classes with their base constructors
        return ConstructUnit(sprite, direction, parent);
    }

    [EngineRoutine(0x0044a140)]
    private Sprite ConstructRail(Rail sprite, Angle direction, Sprite? parent) => ConstructTerrain(sprite, direction, parent);

    [EngineRoutine(0x0044c040)]
    private Sprite ConstructDepo(Depo sprite, Angle direction, Sprite? parent) => ConstructUnit(sprite, direction, parent);

    [EngineRoutine(0x004545e0)]
    private Sprite ConstructMan(Man sprite, Angle direction, Sprite? parent)
    {
        ConstructUnit(sprite, direction, parent);
        sprite.InsertUniqueItem(Sprite.WeaponItemBase + Sprite.Pistols);

        return sprite;
    }

    [EngineRoutine(0x00447fe0)]
    private Sprite ConstructCannon(Cannon sprite, Angle direction, Sprite? parent)
    {
        ConstructSprite(sprite, direction, parent);
        ref readonly var gameObject = ref sprite.Definition.Object;

        if (gameObject.Properties.HasFlag(ObjectProperties.RandomZSpeed) && !sprite.Definition.ZSpeed.IsExactlyZero())
        {
            bool downward = (_rng.Next() & 1) == 0;
            float lift = _rng.Next() * sprite.Definition.ZSpeed;
            sprite.VerticalSpeed = downward ? lift * -GameRandom.UnitScale : lift * GameRandom.UnitScale;
        }
        else if (parent is not null && parent.Animation >= SpriteAnimation.Death && gameObject.TopZ < 0)
        {
            sprite.VerticalSpeed = sprite.Definition.ZSpeed;
            sprite.Speed = sprite.MaxSpeed + parent.Speed;
        }
        else
        {
            sprite.VerticalSpeed = sprite.Definition.ZSpeed;
        }

        if (gameObject.Properties.HasFlag(ObjectProperties.RandomSpeed) && !sprite.Definition.Speed.IsExactlyZero())
        {
            sprite.MaxSpeed = _rng.Next() * sprite.Definition.Speed * GameRandom.UnitScale;
        }

        sprite.IsClimbing = true;
        Motion.StartMove(sprite);

        return sprite;
    }

    [EngineRoutine(0x0040ec60)]
    private Sprite ConstructLinker(Linker linker, Angle direction, Sprite? parent)
    {
        var position = linker.Position;
        ConstructSprite(linker, direction, parent);
        linker.Base = parent;
        linker.StartDirection = direction;

        if (parent is null)
            return linker;

        SpawnedObjectSystem.AddLinkToLast(parent, linker);
        linker.Offset = position - parent.Position;

        return linker;
    }

    [EngineRoutine(0x00448a60)]
    private Sprite ConstructFrame(Frame frame, Angle direction, Sprite? parent)
    {
        frame.Position = new Vector3(frame.Position.AsVector2() + Camera, frame.Position.Z);
        ConstructSprite(frame, direction, parent);
        _menuSprites.Add(frame);
        frame.AddRef();

        return frame;
    }

    [EngineRoutine(0x0040ed40)]
    private Sprite ConstructText(Text text, Angle direction, Sprite? parent) => ConstructFrame(text, direction, parent);

    [EngineRoutine(0x00449640)]
    private Sprite ConstructRegion(Region sprite, Angle direction, Sprite? parent)
    {
        ConstructSprite(sprite, direction, parent);

        return sprite;
    }

    [EngineRoutine(0x0040e870)]
    private Sprite? CreateSprite(SpriteDefinition? definition, Vector3 position, Angle direction, Sprite? parent)
    {
        if (definition is null)
            return null;

        if (definition.IsRegionReplaced)
        {
            definition = ReplaceDefinition(definition, position);
        }

        Sprite sprite;

        switch (definition.Class)
        {
            case SpriteClass.Unit:
                sprite = ConstructUnit(new Unit(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Avia:
                sprite = ConstructAvia(new Avia(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Cannon:
                sprite = ConstructCannon(new Cannon(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Primitive:
                sprite = ConstructPrimitive(new Primitive(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Man:
                sprite = ConstructMan(new Man(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.BuiltTerrain:
                sprite = ConstructBuiltTerrain(new BuiltTerrain(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Sprite:
                sprite = new Sprite(-1, definition, position, default, 0, map: this);
                ConstructSprite(sprite, direction, parent);
                break;
            case SpriteClass.Frame:
                sprite = ConstructFrame(new Frame(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Linker:
                sprite = ConstructLinker(new Linker(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Text:
                sprite = ConstructText(new Text(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            case SpriteClass.Region:
                sprite = ConstructRegion(new Region(-1, definition, position, default, 0, map: this), direction, parent);
                break;
            default:
                Session.Logger.Error(MapLogTag, LogError.Create, (int)definition.Class, "sprite - Behave is invalidate"u8);

                return null;
        }

        if ((_flags & (MapFlags.Loading | MapFlags.AppMenu)) == 0)
        {
            RunCreationHandler(sprite);
        }

        return sprite;
    }

    [MemberNotNull(nameof(Motion), nameof(Effects), nameof(Weapons), nameof(Collision), nameof(PlayerMovement), nameof(Brain))]
    private void BuildSystems()
    {
        Motion = new SpriteKinematics(Definitions, this);
        Effects = new SpawnedObjectSystem(Definitions, Movement, this);
        Weapons = new WeaponSystem(this, Definitions);
        Collision = new SpriteCollision(this);
        PlayerMovement = new PlayerMovement(Collision, Motion);
        Brain = new MonsterBrain(Motion, Collision, _rng, Collector, this);
    }

    private void LoadOrders(Sprite sprite, ReadOnlySpan<StackAction> stack)
    {
        var orders = new Act[stack.Length];
        for (int i = 0; i < stack.Length; i++)
        {
            var action = stack[i];
            orders[i] = Act.HasSpriteTarget(action.Code)
                ? new Act(action.Act, action.Var1, action.Var2, action.Var3, GetRelatedSprite(action.Var1))
                : Act.FromAction(action);
        }

        sprite.ActionStack.Load(orders);
    }

    private List<Sprite> GetLiving(KnownObject id, LivingSet set)
    {
        if (set.CheckedAt == Time)
            return set.Sprites;

        set.CheckedAt = Time;
        set.Sprites.Clear();
        var definition = Definitions.Find(id);

        if (definition is null)
            return set.Sprites;

        foreach (var sprite in _updates.GetLayer(definition.Layer))
        {
            if (sprite is not null && sprite.Definition == definition && sprite.Animation < SpriteAnimation.Death)
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
        while (TryReadRelatedSprite(ref reader, out var first))
        {
            var group = new Group(_groupHead, first);
            while (TryReadRelatedSprite(ref reader, out var sprite))
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

    private void TickEntry(Sprite sprite)
    {
        if (!Tact(sprite))
        {
            DeleteSprite(sprite);
        }
    }

    private void AdvanceKeyframes(Sprite sprite)
    {
        ref readonly var weapon = ref sprite.Definition.Weapon;
        int elapsed = Time - sprite.KeyframeStart;
        int keyPeriod = weapon.AnimationPeriod;
        bool timed = true;

        if (keyPeriod == SpriteDefinition.Unlimited)
        {
            if (sprite.Animation > SpriteAnimation.Birth && sprite.LastFrame != sprite.FirstFrame)
            {
                keyPeriod = sprite.Definition.GetVisualFrameDuration(sprite.Animation) * (sprite.LastFrame - sprite.FirstFrame);
            }
            else if (sprite.Lifetime == SpriteDefinition.Unlimited)
            {
                timed = false;
            }
            else
            {
                keyPeriod = sprite.Lifetime + elapsed;
            }
        }

        if (timed && keyPeriod != 0)
        {
            int key = (int)sprite.KeyframePosition + 1;
            while (key < WeaponRecord.KeyframeCount - 1 && keyPeriod * GetKeyTime(weapon, key) <= elapsed)
            {
                key++;
            }

            key--;
            float start = keyPeriod * GetKeyTime(weapon, key);
            sprite.KeyframePosition = (elapsed - start) / ((GetKeyTime(weapon, key + 1) - GetKeyTime(weapon, key)) * keyPeriod) + key;
        }
    }

    private void EmitStreamChild(Sprite sprite)
    {
        int emitSlot = (int)sprite.Animation;
        var emitChild = Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[emitSlot]));

        if (emitChild is not null && sprite.Definition.Class == SpriteClass.Region && sprite.Definition.Object.ChildX[emitSlot].IsExactlyZero() && sprite.Definition.Object.ChildY[emitSlot].IsExactlyZero())
        {
            var region = sprite as Region;
            bool isMapCovered = region is not null && region.Flags.HasFlag(RegionFlags.CoversMap);
            var regionSize = region?.Size ?? Vector2.Zero;
            float regionWidth = isMapCovered ? Movement.MapSize.Width : regionSize.X;
            float regionHeight = isMapCovered ? Movement.MapSize.Height : regionSize.Y;
            float area = regionHeight * regionWidth / emitChild.Object.Size.X / emitChild.Object.Size.Y;
            int count = (int)area;

            if (count != 0 && FrameTime != 0 && _rng.Next() % (int)(1000u / (uint)FrameTime / (uint)count + 1) == 0)
            {
                CreateChild(sprite);
            }
        }
        else if (emitChild is not null && emitChild.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke) && (uint)(Time - (int)((uint)Time % (uint)sprite.StreamPeriod)) > (uint)(Time - FrameTime))
        {
            float across = sprite.Direction.Sine * sprite.Speed;
            float down = sprite.Direction.Cosine * sprite.Speed + (sprite.VerticalSpeed - emitChild.ZSpeed);

            if (emitChild.Object.Properties.HasFlag(ObjectProperties.Wind))
            {
                across -= Session.Graphics.WindDirection.Sine * Session.Graphics.WindSpeed;
                down -= Session.Graphics.WindDirection.Cosine * Session.Graphics.WindSpeed;
            }

            float acrossPeriod = across.IsExactlyZero() ? MaxStreamPeriod : emitChild.Object.Size.X / Math.Abs(across);
            float downPeriod = down.IsExactlyZero() ? MaxStreamPeriod : emitChild.Object.Size.Y / Math.Abs(down);
            uint streamPeriod = (uint)CRuntime.Ftol(Math.Min(acrossPeriod, downPeriod));

            if ((SpriteAnimation)emitSlot == SpriteAnimation.Fight && sprite.Definition.HasDoubleAttack)
            {
                streamPeriod >>= 1;
            }

            sprite.StreamPeriod = (int)(streamPeriod > MaxStreamPeriod ? MaxStreamPeriod : streamPeriod == 0 ? 1 : streamPeriod);
            CreateChild(sprite);
        }
    }

    private bool CallStateHandlers(Sprite sprite, bool everyFrame)
    {
        var state = sprite.Animation;
        int handler = sprite.Definition.StateHandlers[(int)state];
        while (handler >= 0 && (sprite.Frame == sprite.FirstFrame || everyFrame))
        {
            if (CallScript(handler, sprite) != 0)
                return true;

            if (sprite.Animation == state)
                break;

            state = sprite.Animation;
            handler = sprite.Definition.StateHandlers[(int)state];
        }

        return false;
    }

    private void CreateFrameChild(Sprite sprite)
    {
        var tactChild = Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)sprite.Animation]));

        if (tactChild is null || tactChild.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke))
            return;

        int emissionFrame = sprite.Definition.Object.Properties.HasFlag(ObjectProperties.CreateChildEnd) ? sprite.LastFrame : sprite.FirstFrame;

        if (sprite.Animation == SpriteAnimation.Fight && sprite.Definition.HasDoubleAttack
            ? sprite.Frame == (sprite.LastFrame + 1 + sprite.FirstFrame) / 2 || sprite.Frame == emissionFrame
            : sprite.Definition.Object.Properties.HasFlag(ObjectProperties.Track) || sprite.Frame == emissionFrame)
        {
            CreateChild(sprite);
        }
    }

    private void AdvanceOrders(Sprite sprite, bool everyFrame)
    {
        SpriteAnimation state;

        if (sprite.Command is Command.Passive or Command.Pause)
        {
            state = sprite.Animation;

            if (state < SpriteAnimation.Death && state != SpriteAnimation.StandOpen && sprite.Frame > sprite.LastFrame)
            {
                if (sprite.Speed.IsExactlyZero())
                {
                    if (state is SpriteAnimation.Go or > SpriteAnimation.Open)
                    {
                        sprite.ChangeAnimation(SpriteAnimation.Stand);
                    }
                }
                else if (state != SpriteAnimation.Go)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Go);
                }
            }
        }
        else if (sprite.Frame > sprite.LastFrame || everyFrame)
        {
            if (sprite.ActionStack.Count == 0 || sprite.Command != Command.None)
            {
                Action(sprite, ActionCode.NextCommand);
            }
            else
            {
                var order = sprite.ActionStack.Top;
                state = sprite.Animation;

                if (order.Value > Act.LastState &&
                    (state is > SpriteAnimation.Open and < SpriteAnimation.Death and not SpriteAnimation.StandOpen || state == SpriteAnimation.Go && sprite.Speed.IsExactlyZero()))
                {
                    sprite.ChangeAnimation(SpriteAnimation.Stand);
                }

                if (order.Value != Act.StopMarker)
                {
                    sprite.ActionStack.Pop();

                    if (order.Value <= Act.LastState)
                    {
                        sprite.ChangeAnimation((SpriteAnimation)order.Value);
                    }
                    else
                    {
                        sprite.Action(this, order.Value, order.Var1, order.Var2, order.Var3, order.Target);
                    }
                }
            }
        }
    }

    private void TickLifetime(Sprite sprite)
    {
        if (sprite.HasExtra && sprite.Lifetime != SpriteDefinition.Unlimited && sprite.Animation < SpriteAnimation.Death)
        {
            if ((uint)(Time - sprite.LastTickTime) < (uint)sprite.Lifetime)
            {
                sprite.Lifetime -= Time - sprite.LastTickTime;
            }
            else
            {
                sprite.ChangeAnimation(SpriteAnimation.Death);
            }
        }

        if (sprite.HasExtra && sprite.Definition.Weapon.AnimationPeriod != SpriteDefinition.Unlimited && (uint)sprite.Definition.Weapon.AnimationPeriod < (uint)(Time - sprite.KeyframeStart))
        {
            sprite.KeyframeStart = Time;
        }
    }

    private Vector2 GetChildShift(Sprite sprite, SpriteDefinition definition, int slot, int index, Angle offsetDirection, out Vector2 lateral)
    {
        float childX = definition.Object.ChildX[slot];
        float childY = definition.Object.ChildY[slot];

        if (definition.Children[slot] < 0 && definition.Class == SpriteClass.Region && childX.IsExactlyZero() && childY.IsExactlyZero())
        {
            lateral = Vector2.Zero;

            var region = sprite as Region;
            if (region is null || !region.Flags.HasFlag(RegionFlags.CoversMap))
            {
                var regionSize = region?.Size ?? Vector2.Zero;
                float spreadX = regionSize.X * 0.5f - _rng.Next() * regionSize.X * GameRandom.UnitScale;
                float spreadY = regionSize.Y * 0.5f - _rng.Next() * regionSize.Y * GameRandom.UnitScale + sprite.Position.Z;
                return new Vector2(spreadX, spreadY);
            }

            float mapX = _rng.Next() * Movement.MapSize.Width * GameRandom.UnitScale - sprite.Position.X;
            float mapY = _rng.Next() * Movement.MapSize.Height * GameRandom.UnitScale - sprite.Position.Y + sprite.Position.Z;
            return new Vector2(mapX, mapY);
        }

        if (definition.Children[slot] < 0)
        {
            float spreadX = childX + childX;
            spreadX = childX - _rng.Next() * spreadX * GameRandom.UnitScale;
            float spreadY = childY + childY;
            spreadY = childY - _rng.Next() * spreadY * GameRandom.UnitScale;
            lateral = new Vector2(-(offsetDirection.Cosine * spreadX), -(offsetDirection.ProjectedSine * spreadX));
            return new Vector2(offsetDirection.Sine * spreadY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * spreadY);
        }

        if (index == 1)
        {
            lateral = new Vector2(-(offsetDirection.Cosine * childX), -(offsetDirection.ProjectedSine * childX));
            return new Vector2(offsetDirection.Sine * childY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * childY);
        }

        if (index == 2)
        {
            lateral = Vector2.Zero;
            return new Vector2(offsetDirection.Sine * childY, offsetDirection.ProjectedCosine * childY);
        }

        lateral = new Vector2(offsetDirection.Cosine * childX, offsetDirection.ProjectedSine * childX);
        return new Vector2(offsetDirection.Sine * childY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * childY);
    }

    private void CreateChildAs(Sprite sprite, SpriteAnimation state)
    {
        var previous = sprite.Animation;
        sprite.SetAnimationValue(state);
        CreateChild(sprite);
        sprite.SetAnimationValue(previous);
    }

    private void LaunchChild(Sprite created, Sprite goal, SpriteDefinition definition, Vector2 lateral)
    {
        if (definition.WeaponFlags.HasFlag(WeaponFlags.NoSpread) && goal.Definition != Definitions.Empty)
        {
            Attack(created, goal);
            Motion.StartMove(created);

            return;
        }

        var point = goal.Position;

        for (int attempt = 0; attempt < AimAttempts; attempt++)
        {
            float pointX = definition.Aim + goal.Position.X + lateral.X - _rng.Next() * definition.Aim * AimRandomScale;
            float pointY = definition.Aim + goal.Position.Y + lateral.Y - _rng.Next() * definition.Aim * AimRandomScale;
            point = new Vector3(pointX, pointY, goal.Position.Z);

            if (Movement.GetHeight(new Vector2(pointX, pointY)) < goal.Position.Z && Movement.GetHeight((goal.Position.AsVector2() + point.AsVector2()) * 0.5f) < goal.Position.Z)
                break;
        }

        var aimed = ConstructGoal(point);
        Attack(created, aimed);
        Motion.StartMove(created);
    }

    [EngineRoutine(0x0040fad0)]
    private void AddToLayer(Sprite sprite)
    {
        _updates.Add(sprite.Definition.Layer, sprite);
        sprite.AddRef();
        Release(sprite);
    }

    [EngineRoutine(0x00446c90)]
    private Mouse ConstructCursor()
    {
        var cursor = new Mouse(-1, Definitions.Empty, new Vector3(Session.ScreenSize.ToVector2() * HalfShare, 0), default, 0, map: this);
        ConstructSprite(cursor, default, null);

        if (cursor.Definition != Definitions.Empty)
        {
            RemoveFromUpdates(cursor);
        }
        else if (cursor.Link is not null)
        {
            RemoveFromUpdates(cursor.Link);
        }

        if (cursor.Definition == Definitions.Empty)
        {
            cursor.AddRef();
        }

        return cursor;
    }

    private void SetGammaOverrides(bool clear)
    {
        var flagman = GetFlagman(ControlledPlayer);

        if (flagman is { Army: Army.Player } && Definitions.SelectedGamma != 0)
        {
            OverrideGamma(flagman, clear ? default : Gamma.FromScript(Definitions.SelectedGamma));
        }

        var hovered = _players[ControlledPlayer].Hovered;

        if (hovered is null || Definitions.HoveredGamma == 0 && Definitions.HoveredEnemyGamma == 0 || !clear && !_flags.HasFlag(MapFlags.MouseTips))
            return;

        OverrideGamma(hovered, clear ? default : Gamma.FromScript(hovered.Army == Army.Monsters ? Definitions.HoveredEnemyGamma : Definitions.HoveredGamma));
    }

    [EngineRoutine(0x0040f9e0)]
    private void Pause()
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
            foreach (var entry in _updates.GetLayer(layer))
            {
                entry?.LastTickTime = _pausedAt;
            }
        }

        _clock.Resume(_pausedAt);
        _flags &= ~MapFlags.Paused;
    }

    private void TickMenuSprites()
    {
        for (int i = _menuSprites.Count - 1; i >= 0; i--)
        {
            TickEntry(_menuSprites[i]);
        }
    }

    private void TickUpdatedSprites()
    {
        for (int layer = 0; layer < UpdateOrder.UpdatedLayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null)
                {
                    TickEntry(sprite);
                }
            }
        }
    }

    private void HandleDebugKey(int key)
    {
        switch (key)
        {
            case HashedSpritesKey:
                _debug ^= StepFlags.DrawHashedSprites;
                break;
            case HashedNumbersKey:
                _debug ^= StepFlags.DrawHashedNumbers;
                break;
            case FlagmanKey:
                _flags ^= MapFlags.DrawFlagman;
                break;
            case GridKey:
                _flags ^= MapFlags.DrawGridZ | MapFlags.DrawSpriteBoxes;
                break;
            case SingleStepKey:
                _debug ^= StepFlags.SingleStep;
                break;
            case RailsKey:
                _flags ^= MapFlags.DrawRails;
                break;
            case ReloadKey:
                Definitions.ReloadRecords(Session.Logger, Sound);
                break;
        }
    }

    [EngineRoutine(0x0040c570)]
    private void Release()
    {
        var logger = Session.Logger;
        Definitions.LogCounts(logger);
        _spriteRelations.Clear();
        _clock.ResetRate();
        Session.Graphics.StopMovie();
        Session.Graphics.SetWind(ReleaseWindSpeed, new Angle(ReleaseWindDirection));
        Session.Graphics.SetEnvironment((MapEnvironment)(-1));
        if (IsPlayingDemo)
        {
            CursorSprite.LoadCursor(_fileSystem);
        }

        _playedDemo = null;
        _playedOffset = 0;
        _demoPacedTime = 0;
        _demoPacedAt = 0;
        EndDemoRecording();
        _flags &= ~(MapFlags.RecordingDemo | MapFlags.PlayingDemo);
        _scrollType = ScrollType.FromBorder;
        logger.Write("Player release"u8);
        foreach (var player in _players)
        {
            player.Reset();
        }

        logger.Write("Sprite release"u8);
        // TODO: Port the trains; the release sets the flag (DAT_005da58c) that makes the Engine destructors (0x44e9f0, 0x450e20) skip their scripts while it destroys the sprites
        foreach (var player in _players)
        {
            player.Messages.Clear();
        }

        DeleteAll();
        logger.Write("Menu   release"u8);
        if (_menuSprites.Count != 0)
        {
            logger.Error($"SPRITE {(int)_menuSprites[0].Definition.Id}", LogError.Text, 0, MenuSpriteExistsText);
            _menuSprites.Clear();
        }

        int elapsed = Time - _levelStartTime;
        logger.Write($"Average fps={(elapsed == 0 ? 0 : (int)((uint)(_stepCount * 1000) / (uint)elapsed))}");
        logger.Write("Script release"u8);
        foreach (var file in _scriptFiles.Values)
        {
            file.Dispose();
        }

        _scriptFiles.Clear();
        Script.Reset();
        _stepCount = 0;
        ReleaseAdditionalLoads();
        Definitions.ResetLevelState();
        for (int i = 0; i < _scriptHooks.Length; i++)
        {
            _scriptHooks[i] = i + NoHookBase;
        }
    }

    private void DeleteAll()
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
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
            var list = _updates.GetLayer(layer);
            int top = list.Count - 1;
            while (top >= 0 && list[top] is null)
            {
                top--;
            }

            if (top >= 0)
            {
                Session.Logger.Error($"SPRITE {(int)list[top]!.Definition.Id}", LogError.Text, top, SpriteExistsText);
            }
        }

        if (_groupHead.Next != _groupHead)
        {
            Session.Logger.Error(MapLogTag, LogError.Text, 0, IncorrectGroupsText);
        }
    }

    [EngineRoutine(0x00405160)]
    private void DeletePointerToSprite(Sprite removed)
    {
        ForgetMouseTip(removed);
        DeleteBasePointerToSprite(removed);
    }

    [EngineRoutine(0x0040b4b0)]
    private void DeleteBasePointerToSprite(Sprite removed)
    {
        foreach (var controller in _players)
        {
            controller.Forget(removed);
        }

        if (removed.Handle != 0)
        {
            Script.ClearSpriteHandle(removed.Handle);
        }

        RemoveFromGroups(removed);

        int index = removed.ReferenceCount > 1 ? Collector.Listed.Length - 1 : -1;
        while (index >= 0)
        {
            Collector.Listed[index].DeletePointerToSprite(this, removed);

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

            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null)
                {
                    sprite.DeletePointerToSprite(this, removed);
                }
            }
        }
    }

    [EngineRoutine(0x00449be0)]
    private SpriteDefinition ReplaceDefinition(SpriteDefinition definition, Vector3 position)
    {
        var list = _updates.GetLayer(ObjectLayer.Region);

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is not Region region || region.Definition.Class != SpriteClass.Region || position.Z >= region.Position.Z + RegionReach)
                continue;

            if (!region.Flags.HasFlag(RegionFlags.CoversMap))
            {
                var half = region.Size * 0.5f;

                if (region.Position.X - half.X > position.X || position.X > half.X + region.Position.X ||
                    region.Position.Y - half.Y > position.Y || position.Y > half.Y + region.Position.Y)
                    continue;
            }

            for (int pair = 0; pair < Region.Pairs; pair++)
            {
                if (region.From[pair] == definition)
                    return region.To[pair]!;
            }
        }

        return definition;
    }

    [EngineRoutine(0x00445770)]
    private void CreateLink(Sprite sprite)
    {
        var definition = sprite.Definition.LinkDefinition;

        if (definition is null || sprite.Link?.Definition == definition || definition.NotCreatedAsChild != 0)
            return;

        var position = sprite.Position + sprite.Definition.GetLinkOffset(definition, sprite.Direction);
        var link = AddSprite(definition, position, sprite.Direction);
        AddLink(sprite, link);

        if (sprite.Link is null)
        {
            Session.Logger.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Create, 0, "link"u8);

            return;
        }

        ChangeArmy(sprite.Link, (int)sprite.Army);
    }

    private bool IsFree(SpriteDefinition definition, Vector3 position) => FindPlacementBlocker(definition, position) is null;
}
