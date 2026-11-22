using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the game world of a session, which holds one loaded level at a time and runs it frame by frame.
/// </summary>
public sealed partial class Map : IDisposable
{
    private const int WindowMessageHitTest = 0x84;
    private const int SpriteRecordSize = 112;
    private const int MaxStreamPeriod = 30000;
    private const int ChildPairCount = 2;
    private const int AimAttempts = 5;
    private const float ChildRandomScale = 3.0518509E-05f;
    private const float AimRandomScale = 6.1037019E-05f;
    private const float RescueEffectHeight = 22;
    private const int WornArmorShareCount = 3;
    private const int ArmorRounding = 50;
    private const int PlayerSlots = 4;
    private const int ArmyMask = PlayerSlots - 1;
    private const int StartRandomState = 1;
    private const int SpawnScatter = 8;
    private const float RegionReach = 25f;

    private static readonly SizeF StartMapSize = new(640, 480);
    private readonly GameRandom _rng;
    private readonly List<Sprite> _menuSprites = [];
    private readonly IGameFileSystem _fileSystem;
    private readonly Dictionary<int, ScriptFile> _scriptFiles = [];
    private readonly ControlSettings _controls;
    private readonly List<Group> _groups = [];
    private readonly Player[] _players = new Player[PlayerSlots];
    private readonly LivingSet _livingFences = new();
    private readonly LivingSet _livingSites = new();
    private readonly LivingSet _livingCharges = new();
    private MonsterBrain _brain;
    private UpdateOrder _updates;
    private int _pausedAt;
    private MenuClicks _menuClicks;
    private Sprite? _menuCurrent;
    private IGameFileSystem? _scriptFileSystem;
    private GameClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="Map"/> class with no level loaded.
    /// </summary>
    /// <param name="definitions">The sprite definitions of the game.</param>
    /// <param name="fileSystem">The file system the game's files are read from.</param>
    /// <param name="controls">The control settings, or null for the executable's defaults.</param>
    /// <param name="registry">The registry the level scripts read and write, or null for an empty one.</param>
    /// <param name="session">The session the map belongs to, or null to create one.</param>
    [EngineRoutine(0x00408ff0)]
    public Map(SpriteCatalog definitions, IGameFileSystem fileSystem, ControlSettings? controls = null, ScriptRegistry? registry = null, GameSession? session = null)
    {
        Registry = registry ?? new ScriptRegistry("");
        Session = session ?? new GameSession(Registry);
        _fileSystem = fileSystem;
        Definitions = definitions;
        _rng = new GameRandom(StartRandomState);
        Script.Logger = Session.Logger;
        _updates = new UpdateOrder();
        _clock = new GameClock(GameClock.DisplayFrameTimes);
        _sprites = [];
        Movement = new MovementMap(StartMapSize);
        Collector = new SpriteCollector(StartMapSize, definitions.LargestHashedSize);
        Sound = Session.Sound ??= new SoundSystem(definitions.Sounds, fileSystem) { Logger = Session.Logger };
        CursorSprite = ConstructCursor();
        LoadObjectRecords(false);
        _controls = (controls ?? ControlSettings.Default).Clone();
        Input = new PlayerInput(_controls);
        BuildSystems();
        for (int slot = 0; slot < PlayerSlots; slot++)
        {
            _players[slot] = new Player(this, _controls);
        }
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
    /// Gets the cursor position in map coordinates.
    /// </summary>
    public Vector2 Cursor { get; private set; }

    /// <summary>
    /// Gets the input the controllers read this frame.
    /// </summary>
    public PlayerInput Input { get; }

    /// <summary>
    /// Gets the sprite groups of the map.
    /// </summary>
    public IReadOnlyList<Group> Groups => _groups;

    /// <summary>
    /// Gets a value indicating whether the controllers act on the input.
    /// </summary>
    public bool IsPlayerControlled { get; private set; } = true;

    /// <summary>
    /// Gets the sprite definitions of the game.
    /// </summary>
    public SpriteCatalog Definitions { get; }

    /// <summary>
    /// Gets the sprite of the mouse cursor.
    /// </summary>
    public Sprite CursorSprite { get; }

    /// <summary>
    /// Gets the game clock in milliseconds.
    /// </summary>
    public int Time => _clock.Time;

    /// <summary>
    /// Gets the state of the engine's random source.
    /// </summary>
    public int RandomState => _rng.State;

    /// <summary>
    /// Gets the length of the current frame in milliseconds.
    /// </summary>
    public int FrameTime => _clock.FrameTime;

    /// <summary>
    /// Gets a value indicating whether the level script keeps the doors from working.
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
    public bool IsLoading { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the level is paused, in which case only its menu sprites update.
    /// </summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Gets the sound effects and music of the session.
    /// </summary>
    public SoundSystem Sound { get; }

    internal WeaponSystem Weapons { get; private set; }

    internal IReadOnlyList<Sprite> MenuSprites => _menuSprites;

    internal MovementMap Movement { get; private set; }

    internal SpriteCollector Collector { get; private set; }

    internal SpriteCollision Collision { get; private set; }

    internal PlayerMovement PlayerMovement { get; private set; }

    internal ProjectileCollision Projectiles => Weapons.Collision;

    internal SpawnedObjectSystem Effects { get; private set; }

    internal SpriteKinematics Motion { get; private set; }

    private static ReadOnlySpan<int> WornArmorShares => [50, 70, 90];

    private static ReadOnlySpan<byte> DoorsOpenVariable => "DoorsIsOpen"u8;

    private static ReadOnlySpan<byte> SpriteLogTag => "SPRITE %i"u8;

    /// <summary>
    /// Loads a level into the map and releases the level it held before.
    /// </summary>
    /// <param name="data">The level to load.</param>
    /// <param name="seed">The seed of the random source, used unless <paramref name="demo"/> supplies its own.</param>
    /// <param name="demo">The demo to play back, or null to play without one.</param>
    /// <exception cref="ArgumentException">The level does not use the map's sprite catalog.</exception>
    /// <exception cref="InvalidDataException">The level script does not compile.</exception>
    [EngineRoutine(0x0040c960)]
    public void Load(LevelTemplate data, int seed, DemoResource? demo = null)
    {
        if (!ReferenceEquals(data.Definitions, Definitions))
        {
            throw new ArgumentException("The level does not use the map's sprite catalog.", nameof(data));
        }

        IsLoading = true;
        Release();
        TurnCursorOff();
        Session.EnterMap(GameTextEncoding.Cyrillic.GetBytes(data.MapName));
        if (demo is not null)
        {
            seed = demo.Value.Seed;
        }

        _rng.Seed(seed);
        LoadObjectRecords(true);
        Session.Logger.Write("CurrentTime   =%-15u   sizeof(SPRITE)=%-8i Map version   =%i"u8, GameClock.LevelStartTime, SpriteRecordSize, data.MapData.Version);
        var mapData = data.MapData;
        Definitions.ReloadRecords();
        Definitions.ResetLevelState();
        _clock = new GameClock(data.FrameTimes);
        _sprites = mapData.Sprites.ToList();
        Session.Graphics.Load(mapData.Environment, mapData.Ambient, mapData.WindDirection, mapData.WindSpeed);
        Movement = data.Movement.Clone();
        // TODO: Port the GRID chunk load (0x408080 into +0x220, then the size check against +0x228 x +0x22c x 2 that reinitializes on a mismatch); the 35 shipped maps that have the chunk hold an all-zero grid
        Collector = new SpriteCollector(Movement.MapSize, Definitions.LargestHashedSize);
        BuildSystems();
        SetPosition(Session.ScreenSize.ToVector2() * HalfShare + mapData.Camera, CameraMove.None);
        // TODO: Port the trains; the loader builds the rail map (0x43c510) after the terrain and the sprite records, and the Engine links (0x454530) after the sprites
        var placedSprites = new Dictionary<int, Sprite>();

        for (int record = 0; record < mapData.StoredSprites.Count; record++)
        {
            var placement = mapData.StoredSprites[record];
            ConstructLoadingScreen(data.LoadingScreenRecords, record);

            var definition = Definitions.Find(placement.Id);

            if (definition is null)
                continue;

            var sprite = PlaceSprite(placement, definition);

            if (sprite is not null && placement.Handle != NoHandle)
            {
                placedSprites[placement.Handle] = sprite;
                _spriteRelations.Add((placement.Handle, sprite));
            }
        }

        ConstructLoadingScreen(data.LoadingScreenRecords, mapData.StoredSprites.Count);

        foreach (var placement in mapData.StoredSprites)
        {
            if (!placedSprites.TryGetValue(placement.Handle, out var sprite))
                continue;

            var record = Resource.FromRecord(placement.Payload);
            Restore(sprite, ref record, mapData.Version);
        }

        LoadPlayers(mapData.PlayerHandles, placedSprites);
        LoadGroups(mapData.Groups, placedSprites);
        IsLoading = false;
        _spriteRelations.Clear();
        // TODO: Port the "Vid    release %i %i" log line (0x40d3f0), which writes the texture bytes the renderer holds and the bytes of the loaded definition data
        if (!Script.Load(data.FileSystem, data.ScriptPath))
        {
            throw new InvalidDataException($"Could not compile the level script '{data.ScriptPath}'.");
        }

        var objects = Script.Objects;
        for (int i = 0; i < objects.Length; i++)
        {
            ref readonly var entry = ref objects[i];

            if (entry.Kind == ScriptObjectKind.Function)
            {
                ProcessScriptFunctions(entry.Name.Span, i, entry.Count);
            }
        }

        if (demo is not null)
        {
            _playedDemo = demo.Value;
            _playedOffset = Script.ReadState(demo.Value.Body);
            SetTime(demo.Value.StartTime);
        }
        else if (Session.IsRecordingNextMap)
        {
            Session.IsRecordingNextMap = false;
            StartDemoRecording(GameTextEncoding.Cyrillic.GetBytes(data.MapName), seed);
        }

        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is null)
                    continue;

                if (sprite.Definition.Object.SpriteClass == SpriteClass.BuiltTerrain)
                {
                    PostCreationBuiltTerrain(sprite);
                }
                else
                {
                    PostCreation(sprite);
                }
            }
        }

        if (!IsPlayingDemo)
        {
            LoadCursor();
        }
    }

    /// <summary>
    /// Sets whether the controllers act on the input.
    /// </summary>
    /// <param name="controlled">A value indicating whether the controllers act on the input.</param>
    public void SetPlayerControl(bool controlled) => IsPlayerControlled = controlled;

    /// <summary>
    /// Moves the cursor to a screen point.
    /// </summary>
    /// <param name="screen">The mouse position on the screen.</param>
    /// <param name="windowOrigin">The window's top-left corner on the screen.</param>
    /// <param name="view">The visible part of the map.</param>
    /// <param name="camera">The camera position on the map.</param>
    /// <returns><see langword="true"/> if the map cursor changed; otherwise, <see langword="false"/>.</returns>
    public bool MoveMouse(Point screen, Point windowOrigin, RectangleF view, Vector2 camera)
    {
        bool inView = Input.WindowProc(WindowMessageHitTest, 0, (screen.Y & 0xFFFF) << 16 | (screen.X & 0xFFFF), windowOrigin, view, camera);
        Cursor = Input.Cursor;

        CursorSprite.ChangeCoor(new Vector3(Input.Cursor.X, Input.Cursor.Y, CursorSprite.Position.Z));

        return inView;
    }

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
            Session.Logger.Error(SpriteLogTag, LogError.Invalid, 0, default);
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
    /// Runs one frame of the level by calling the script's main function, updating the sprites and then applying the input.
    /// </summary>
    /// <param name="drawFrame"><see langword="true"/> to draw the frame, as <see cref="StepDemo"/> returns; <see langword="false"/> to skip drawing a late frame.</param>
    [EngineRoutine(0x00405190)]
    public void Step(bool drawFrame = true)
    {
        ProcessMenuInput(Input);
        Script.CallFunction(this, MainFunction, 0, 0, 0);
        SetTints(false);
        UpdateScreen(drawFrame);
        SetTints(true);

        if (IsPaused)
        {
            TickMenuSprites();
        }

        for (int layer = 0; layer < UpdateOrder.UpdatedLayerCount && !IsPaused; layer++)
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

        TactMouseTip(Input);

        Scroll();

        if (IsPlayerControlled && !IsPaused)
        {
            foreach (var controller in _players)
            {
                controller.Control(Input);
            }
        }

        Sound.Tact(_rng, Time, _clock.WallTime);
    }

    /// <summary>
    /// Releases the loaded level.
    /// </summary>
    [EngineRoutine(0x00405ac0)]
    public void Dispose() => Release();

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
    public Sprite? GetFlagman(int army) => _players[army & ArmyMask].Sprite;

    /// <summary>
    /// Sets the player a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <param name="sprite">The player, or null to clear the slot.</param>
    [EngineRoutine(0x0040e600)]
    public void SetFlagman(int army, Sprite? sprite) => _players[army & ArmyMask].SetFlagman(sprite);

    /// <summary>
    /// Advances the game clock to the next frame of wall time and compacts the update lists.
    /// </summary>
    public void AdvanceClock() => AdvanceClock(_clock.GetNextWallTime());

    /// <summary>
    /// Advances the game clock to a wall time reading and compacts the update lists.
    /// </summary>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0040f210)]
    public void AdvanceClock(uint wallTime)
    {
        _clock.StartTact(wallTime, wallTime);
        _updates.Compact(Time);
        Input.ResetFrame();
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
            ref readonly var weapon = ref sprite.Definition.Weapon;
            int elapsed = Time - sprite.KeyframeStart;
            int keyPeriod = weapon.AnimationPeriod;
            bool timed = true;

            if (keyPeriod == SpriteDefinition.Unlimited)
            {
                if (sprite.Animation > SpriteAnimation.Birth && sprite.LastFrame != sprite.FirstFrame)
                {
                    keyPeriod = sprite.Definition.Layout.GetFrameDuration(sprite.Animation) * (sprite.LastFrame - sprite.FirstFrame);
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

        if (sprite.Owner is null)
        {
            MoveByClass(sprite);
        }

        RaiseHeights(sprite.Definition, sprite);

        int emitSlot = (int)sprite.Animation;
        var emitChild = Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[emitSlot]));

        if (emitChild is not null && sprite.Definition.Object.SpriteClass == SpriteClass.Region && sprite.Definition.Object.ChildX[emitSlot].IsExactlyZero() && sprite.Definition.Object.ChildY[emitSlot].IsExactlyZero())
        {
            float regionWidth = sprite.IsCoveringMap ? Movement.MapSize.Width : sprite.RegionSize.X;
            float regionHeight = sprite.IsCoveringMap ? Movement.MapSize.Height : sprite.RegionSize.Y;
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
            uint streamPeriod = (uint)(int)Math.Min(acrossPeriod, downPeriod);

            if ((SpriteAnimation)emitSlot == SpriteAnimation.Fight && sprite.Definition.HasDoubleAttack)
            {
                streamPeriod >>= 1;
            }

            sprite.StreamPeriod = (int)(streamPeriod > MaxStreamPeriod ? MaxStreamPeriod : streamPeriod == 0 ? 1 : streamPeriod);
            CreateChild(sprite);
        }

        var state = sprite.Animation;

        if (sprite.CreatedAt == Time || Time - Time % sprite.Definition.Layout.GetFrameDuration(state) <= Time - FrameTime)
            return true;

        if (sprite.Command is not Command.None && sprite is { Command: < Command.Build, HasGoal: false })
        {
            Session.Logger.Error(SpriteLogTag, LogError.Text, (int)sprite.Command, "command need goal, but goal==NULL"u8, (int)sprite.Definition.Id);
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

        state = sprite.Animation;
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

        int sound = sprite.Definition.Object.Sounds[(int)sprite.Animation];

        if (sound != 0 && (!sprite.HasPlayedStateSound || Sound.IsLooped(sound)))
        {
            sprite.HasPlayedStateSound = true;
            PlaySfx(sprite, sound);
        }

        var tactChild = Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)sprite.Animation]));

        if (tactChild is not null && !tactChild.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke))
        {
            int emissionFrame = sprite.Definition.Object.Properties.HasFlag(ObjectProperties.CreateChildEnd) ? sprite.LastFrame : sprite.FirstFrame;

            if (sprite.Animation == SpriteAnimation.Fight && sprite.Definition.HasDoubleAttack
                ? sprite.Frame == (sprite.LastFrame + 1 + sprite.FirstFrame) / 2 || sprite.Frame == emissionFrame
                : sprite.Definition.Object.Properties.HasFlag(ObjectProperties.Track) || sprite.Frame == emissionFrame)
            {
                CreateChild(sprite);
            }
        }

        sprite.Frame++;

        if (sprite.Animation >= SpriteAnimation.Death && (sprite.Frame > sprite.LastFrame || sprite.Definition.Layout.GetFrameCount(sprite.Animation) == 0))
        {
            sprite.Frame = sprite.LastFrame;

            Action(sprite, ActionCode.Death);

            return false;
        }

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
                        SendAction(sprite, order.Value, order.Var1, order.Var2, order.Var3, order.Target);
                    }
                }
            }
        }

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
            var previous = sprite.Animation;
            sprite.SetAnimationValue(state);
            CreateChild(sprite);
            sprite.SetAnimationValue(previous);
        }

        int sound = sprite.Definition.Object.Sounds[(int)state];

        if (sound != 0)
        {
            PlaySfx(sprite, sound);
        }
    }

    /// <summary>
    /// Creates a sprite with the maximum health of its definition.
    /// </summary>
    /// <param name="definition">The sprite's definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="army">The army.</param>
    /// <param name="parent">The sprite that creates the new sprite and whose height a ground-bound object is placed relative to.</param>
    /// <returns>The sprite.</returns>
    public Sprite AddMonster(SpriteDefinition definition, Vector3 position, Angle direction = default, Army army = Army.Monsters, Sprite? parent = null)
    {
        return ConstructSprite(definition, position, direction, army, definition.MaxHp, parent: parent);
    }

    [EngineRoutine(0x0043f200)]
    internal Vector3 ScatterPosition(SpriteDefinition definition, Vector3 position, Sprite? parent)
    {
        ref readonly var gameObject = ref definition.Object;

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

        return position;
    }

    [EngineRoutine(0x0043f200)]
    internal Sprite ConstructSprite(SpriteDefinition definition, Vector3 position, Angle direction, Army army, int hitPoints, int number = -1, Sprite? parent = null, Sprite? owner = null, Sprite? shooter = null, int? lifetime = null)
    {
        bool isMenu = IsMenuClass(definition);

        if (isMenu)
        {
            position = new Vector3(position.AsVector2() + Camera, position.Z);
        }

        var sprite = new Sprite(number, definition, position, army, hitPoints, map: this, parent: parent) { CreatedAt = Time, Logger = Session.Logger, Owner = owner, Shooter = shooter };
        var state = GetInitialAnimation(definition);
        sprite.Start(state, direction, Time, _rng);

        if (lifetime is not null)
        {
            sprite.Lifetime = lifetime.Value;
        }

        CountSprite(sprite);

        if (definition != Definitions.Empty)
        {
            sprite.AddRef();
            AddToUpdates(sprite);
        }

        CreateLink(sprite);
        Collector.InsertToHash(sprite);

        if (state != SpriteAnimation.Birth && !IsLoading)
        {
            CreateChildFor(sprite, SpriteAnimation.Birth);
        }

        if (isMenu)
        {
            _menuSprites.Add(sprite);
            sprite.AddRef();
        }

        if (definition.Object.SpriteClass == SpriteClass.Text)
        {
            sprite.Text = new SpriteText();
        }

        RaiseHeights(definition, sprite);
        ConstructClass(sprite);

        return sprite;
    }

    internal void ConstructClass(Sprite sprite)
    {
        switch (sprite.Definition.Object.SpriteClass)
        {
            case SpriteClass.Unit:
                ConstructUnit(sprite);
                break;
            case SpriteClass.Avia:
            case SpriteClass.Balloon:
                ConstructAvia(sprite);
                break;
            case SpriteClass.Man:
                ConstructUnit(sprite);
                sprite.InsertUniqueItem(Sprite.WeaponItemBase + Sprite.Pistols);
                break;
            case SpriteClass.Creature:
                ConstructCreature(sprite);
                break;
            case SpriteClass.Building:
                ConstructBuilding(sprite);
                break;
            case SpriteClass.CivRobot:
                ConstructCivRobot(sprite);
                break;
        }
    }

    internal IReadOnlyList<Sprite?> GetLayer(int layer) => _updates.GetLayer(layer);

    [EngineRoutine(0x004145b0)]
    internal void RaiseHeights(SpriteDefinition definition, Sprite sprite)
    {
        if (sprite == CursorSprite || definition.HeightPointCount == 0)
            return;

        // TODO: Port the map flag 0xc mask 0x1 that moves built terrain heights to the temporary grid; the port treats it as clear.
        bool permanent = definition.Object.SpriteClass == SpriteClass.BuiltTerrain;

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
        if (sprite == CursorSprite || definition.HeightPointCount == 0 || definition.Object.SpriteClass == SpriteClass.BuiltTerrain)
            return;

        foreach (var point in definition.GetHeightPoints(sprite.Frame))
        {
            Movement.Lower(new Vector3(sprite.Position.X + point.X, sprite.Position.Y + point.Y, sprite.Position.Z + point.Z));
        }
    }

    [EngineRoutine(0x00405bd0)]
    internal Sprite? AddSprite(SpriteDefinition definition, Vector3 position, Angle direction, Sprite? parent)
    {
        if (definition.IsRegionReplaced)
        {
            var replaced = ReplaceDefinition(definition, position);

            if (replaced is null)
                return null;

            definition = replaced;
        }

        if (definition.MaxUnits >= 0 && definition.MaxUnits <= Sum(definition.Counts))
            return null;

        if (!IsDedicatedClass(definition.Object.SpriteClass) && definition.IsRegionReplaced)
        {
            var again = ReplaceDefinition(definition, position);

            if (again is null)
                return null;

            definition = again;
        }

        // TODO: Port the Engine, Rail and Depo constructors (0x44e8b0, 0x44a140, 0x44c040); the port builds those classes through Effects.Create.

        var sprite = Effects.Create(definition, position, direction, Time, parent);

        if (definition.Object.SpriteClass == SpriteClass.Linker)
        {
            SpawnedObjectSystem.ConstructLinker(sprite, parent, position, direction);
        }

        return sprite;
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
            float childX = definition.Object.ChildX[slot];
            float childY = definition.Object.ChildY[slot];
            Vector2 lateral;
            Vector2 shift;

            if (definition.Children[slot] < 0 && definition.Object.SpriteClass == SpriteClass.Region && childX.IsExactlyZero() && childY.IsExactlyZero())
            {
                lateral = Vector2.Zero;

                if (!sprite.IsCoveringMap)
                {
                    float spreadX = sprite.RegionSize.X * 0.5f - _rng.Next() * sprite.RegionSize.X * ChildRandomScale;
                    float spreadY = sprite.RegionSize.Y * 0.5f - _rng.Next() * sprite.RegionSize.Y * ChildRandomScale + sprite.Position.Z;
                    shift = new Vector2(spreadX, spreadY);
                }
                else
                {
                    float spreadX = _rng.Next() * Movement.MapSize.Width * ChildRandomScale - sprite.Position.X;
                    float spreadY = _rng.Next() * Movement.MapSize.Height * ChildRandomScale - sprite.Position.Y + sprite.Position.Z;
                    shift = new Vector2(spreadX, spreadY);
                }
            }
            else if (definition.Children[slot] < 0)
            {
                float spreadX = childX + childX;
                spreadX = childX - _rng.Next() * spreadX * ChildRandomScale;
                float spreadY = childY + childY;
                spreadY = childY - _rng.Next() * spreadY * ChildRandomScale;
                lateral = new Vector2(-(offsetDirection.Cosine * spreadX), -(offsetDirection.ProjectedSine * spreadX));
                shift = new Vector2(offsetDirection.Sine * spreadY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * spreadY);
            }
            else if (i == 1)
            {
                lateral = new Vector2(-(offsetDirection.Cosine * childX), -(offsetDirection.ProjectedSine * childX));
                shift = new Vector2(offsetDirection.Sine * childY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * childY);
            }
            else if (i == 2)
            {
                lateral = Vector2.Zero;
                shift = new Vector2(offsetDirection.Sine * childY, offsetDirection.ProjectedCosine * childY);
            }
            else
            {
                lateral = new Vector2(offsetDirection.Cosine * childX, offsetDirection.ProjectedSine * childX);
                shift = new Vector2(offsetDirection.Sine * childY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * childY);
            }

            var childPosition = new Vector3(shift.X + sprite.Position.X, shift.Y + sprite.Position.Y, definition.Object.ChildZ[slot] + sprite.Position.Z);

            if (child.Object.SpriteClass == SpriteClass.Unit && FindPlacementBlocker(child, childPosition) is not null || state == SpriteAnimation.Fight && sprite.Goal is null)
                continue;

            var childDirection = child.Object.Properties.HasFlag(ObjectProperties.RandomBirth) ? new Angle((byte)(_rng.Next() & byte.MaxValue)) : sprite.Direction;
            var created = AddSprite(child, childPosition, childDirection, sprite);

            var goal = sprite.Goal;

            if (created is null || goal is null || state != SpriteAnimation.Fight)
                continue;

            if (definition.WeaponFlags.HasFlag(WeaponFlags.NoSpread) && goal.Definition != Definitions.Empty)
            {
                Attack(created, goal);
                Motion.StartMove(created);

                continue;
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

    [EngineRoutine(0x00447130)]
    private static void ConstructUnit(Sprite unit)
    {
        unit.Behavior = (unit.ArmedLink ?? unit).Definition.Behavior;
        unit.FillAmmo();
    }

    private static void Tint(Sprite sprite, Gamma gamma)
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

    private static bool IsDedicatedClass(SpriteClass spriteClass) =>
        spriteClass is SpriteClass.Terrain or SpriteClass.Object or SpriteClass.Building or SpriteClass.CivRobot or SpriteClass.Engine or SpriteClass.Rail or SpriteClass.Depo or SpriteClass.Creature or SpriteClass.Balloon;

    [EngineRoutine(0x00444970)]
    private static bool AddLink(Sprite owner, Sprite? link)
    {
        if (link is null || link.Owner is not null)
            return false;

        var previous = owner.Link;

        if (previous is not null)
        {
            previous.Owner = null;
            SpawnedObjectSystem.AppendLink(link, previous);
        }

        owner.Link = link;
        link.Owner = owner;

        return true;
    }

    [MemberNotNull(nameof(Motion), nameof(Effects), nameof(Weapons), nameof(Collision), nameof(PlayerMovement), nameof(_brain))]
    private void BuildSystems()
    {
        Motion = new SpriteKinematics(Definitions, this);
        Effects = new SpawnedObjectSystem(Definitions, Movement, _rng, this);
        Weapons = new WeaponSystem(this, Definitions, _rng);
        Collision = new SpriteCollision(this);
        PlayerMovement = new PlayerMovement(Collision, Motion);
        _brain = new MonsterBrain(Motion, Collision, _rng, Collector, Session);
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

    private Sprite? PlaceSprite(MergedMapSprite placement, SpriteDefinition definition)
    {
        if (definition.MaxHp > 0)
        {
            if (placement.Id != KnownObject.Crate && definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia)
            {
                var unit = AddMonster(definition, placement.Position, placement.Direction, Definitions.GetArmy(definition));
                ChangeArmy(unit, (int)placement.Army);

                return unit;
            }

            if (definition.Object.SpriteClass == SpriteClass.Man)
                return ConstructMan(definition, placement);

            var army = Definitions.GetArmy(definition);
            var body = ConstructSprite(definition, placement.Position, placement.Direction, army, definition.ArmyMaxHp[(int)army & ArmyMask]);
            ChangeArmy(body, (int)placement.Army);
            body.Placement = placement;
            _bodies.Add(placement, body);

            return body;
        }

        if (definition.Object.SpriteClass == SpriteClass.Cannon)
        {
            AddPlacedCannon(placement);
            return null;
        }

        var sprite = placement.Id.IsScripted ? AddScripted(definition, placement.Position, placement.Direction, placement.Army, placement) : AddStatic(placement, definition);

        return sprite;
    }

    private void AddPlacedCannon(MergedMapSprite placement)
    {
        Effects.Spawn(placement.Id, placement.Position, placement.Direction, Time);
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
    private void LoadPlayers(IReadOnlyList<int> handles, Dictionary<int, Sprite> placedSprites)
    {
        for (int army = 0; army < handles.Count && army < PlayerSlots; army++)
        {
            _players[army].Load(placedSprites.GetValueOrDefault(handles[army]));
        }
    }

    private void LoadGroups(IReadOnlyList<IReadOnlyList<int>> groups, Dictionary<int, Sprite> placedSprites)
    {
        foreach (var handles in groups)
        {
            Group? group = null;
            foreach (int handle in handles)
            {
                if (!placedSprites.TryGetValue(handle, out var sprite))
                    continue;

                if (group is null)
                {
                    group = new Group(sprite);
                    _groups.Add(group);
                }
                else
                {
                    group.Add(sprite);
                }
            }
        }
    }

    [EngineRoutine(0x0043adf0)]
    private void RemoveFromGroups(Sprite sprite)
    {
        for (int i = _groups.Count - 1; i >= 0; i--)
        {
            if (!_groups[i].Remove(sprite))
                continue;

            Release(sprite);

            if (_groups[i].Members.Count == 0)
            {
                _groups.RemoveAt(i);
            }
        }
    }

    private void TickEntry(Sprite sprite)
    {
        if (!Tact(sprite))
        {
            DeleteSprite(sprite);
        }
    }

    [EngineRoutine(0x0040fad0)]
    private void AddToLayer(Sprite sprite)
    {
        _updates.Add(sprite.Definition.Layer, sprite);
        sprite.AddRef();
        Release(sprite);
    }

    [EngineRoutine(0x004545e0)]
    private Sprite ConstructMan(SpriteDefinition definition, MergedMapSprite placement)
    {
        var army = Definitions.GetArmy(definition);
        var man = ConstructSprite(definition, placement.Position, placement.Direction, army, definition.ArmyMaxHp[(int)army & ArmyMask]);
        ChangeArmy(man, (int)placement.Army);

        return man;
    }

    [EngineRoutine(0x00446c90)]
    private Sprite ConstructCursor()
    {
        // TODO: Port the cursor class vtable and the zeroing of its unit fields (0x74-0x103); the port tells the cursor apart by reference
        var cursor = ConstructSprite(Definitions.Empty, new Vector3(Session.ScreenSize.ToVector2() * HalfShare, 0), default, Army.Player, 0);

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

    [EngineRoutine(0x004333d0)]
    private void ConstructLoadingScreen(IReadOnlyList<int> redraws, int record)
    {
        bool isRedrawn = false;
        for (int i = 0; i < redraws.Count && !isRedrawn; i++)
        {
            isRedrawn = redraws[i] == record;
        }

        if (!isRedrawn)
            return;

        var definition = Definitions.LoadingScreen ?? throw new InvalidOperationException("Could not find the loading screen definition.");
        ConstructSprite(definition, Vector3.Zero, default, Army.Player, 0);
    }

    [EngineRoutine(0x0044ce60)]
    private void ConstructAvia(Sprite sprite)
    {
        ConstructUnit(sprite);
        Motion.StartMove(sprite);
        sprite.VerticalSpeed = sprite.Definition.ZSpeed;
    }

    [EngineRoutine(0x0044d3e0)]
    private void ConstructCreature(Sprite sprite)
    {
        ConstructUnit(sprite);
        sprite.SeeksRegion = true;
        sprite.VerticalSpeed = sprite.Definition.ZSpeed;
        Motion.StartMove(sprite);
        sprite.Region = FindRegion(sprite.Position.AsVector2());
    }

    private void SetTints(bool clear)
    {
        var flagman = GetFlagman(ControlledPlayer);

        if (flagman is { Army: Army.Player } && Definitions.SelectedGamma != 0)
        {
            Tint(flagman, clear ? default : Gamma.FromScript(Definitions.SelectedGamma));
        }

        var hovered = _players[ControlledPlayer].Hovered;

        if (hovered is null || Definitions.HoveredGamma == 0 && Definitions.HoveredEnemyGamma == 0)
            return;

        Tint(hovered, clear ? default : Gamma.FromScript(hovered.Army == Army.Monsters ? Definitions.HoveredEnemyGamma : Definitions.HoveredGamma));
    }

    [EngineRoutine(0x0040f9e0)]
    private void Pause()
    {
        if (!IsPaused)
        {
            _pausedAt = Time;
        }

        IsPaused = true;
    }

    [EngineRoutine(0x0040fa00)]
    private void Resume()
    {
        if (!IsPaused)
            return;

        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
        {
            foreach (var entry in _updates.GetLayer(layer))
            {
                entry?.LastTickTime = _pausedAt;
            }
        }

        _clock.Resume(_pausedAt);
        IsPaused = false;
    }

    private void TickMenuSprites()
    {
        for (int i = _menuSprites.Count - 1; i >= 0; i--)
        {
            TickEntry(_menuSprites[i]);
        }
    }

    [EngineRoutine(0x0040c570)]
    private void Release()
    {
        var logger = Session.Logger;
        Definitions.LogCounts(logger);
        _spriteRelations.Clear();
        Camera = default;
        _scrollType = ScrollType.FromBorder;
        if (IsPlayingDemo)
        {
            LoadCursor();
        }

        _playedDemo = null;
        _playedOffset = 0;
        _demoPacedTime = 0;
        _demoPacedAt = 0;
        EndDemoRecording();
        IsPaused = false;
        _pausedAt = 0;
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

        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
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

        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
        {
            if (_updates.HasSprites(layer))
            {
                logger.Error(SpriteLogTag, LogError.Text, 0, default);
            }
        }

        // TODO: Port the check of the map's +0x2a8 list head, which logs a map error 10 when the list is not empty at release
        logger.Write("Menu   release"u8);
        if (_menuSprites.Count != 0)
        {
            logger.Error(SpriteLogTag, LogError.Text, 0, default);
            _menuSprites.Clear();
        }

        logger.Write("Average fps=%i"u8, _clock.FramesPerSecond);
        logger.Write("Script release"u8);
        foreach (var file in _scriptFiles.Values)
        {
            file.Dispose();
        }

        _scriptFiles.Clear();
        Script.Reset();
        // TODO: Port the trains; the release sets the global table of script function indexes (DAT_00490628, read only by the Engine class) back to 1000000 + i, and the loader fills it by name
        _updates = new UpdateOrder();
        _bodies.Clear();
        _removed.Clear();
        _statics.Clear();
        _builds.Clear();
        _handles.Clear();
        _livingFences.Clear();
        _livingSites.Clear();
        _livingCharges.Clear();
        _lastHandle = 0;
        _unitIndex = 0;
        _unitMask = 0;
        _spriteLayer = 0;
        _spriteIndex = 0;
        _hardwareGround = null;
        _mouseTip = null;
        _groups.Clear();
        Cursor = default;
        CameraShake = null;
        ScreenFlash = null;
        IsPlayerControlled = true;
        MapToLoad = null;
        IsQuitting = false;
        _menuClicks = default;
        _menuCurrent = null;
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
            Motion.DeletePointerToSprite(Collector.Listed[index], removed);

            if (Collector.Listed.Length < index)
            {
                index = Collector.Listed.Length;
            }

            index--;
        }

        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
        {
            if (removed.ReferenceCount <= 1)
                continue;

            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null)
                {
                    Motion.DeletePointerToSprite(sprite, removed);
                }
            }
        }
    }

    private void MoveByClass(Sprite sprite)
    {
        switch (sprite.Definition.Object.SpriteClass)
        {
            case SpriteClass.Man:
                PlayerMovement.MoveTact(sprite, FrameTime);
                break;
            case SpriteClass.Unit or SpriteClass.Avia:
                _brain.Move(sprite, FrameTime, _clock.FramesPerSecond);
                break;
            case SpriteClass.Building:
                break;
            default:
                Effects.Move(sprite, FrameTime);
                break;
        }
    }

    [EngineRoutine(0x00449be0)]
    private SpriteDefinition? ReplaceDefinition(SpriteDefinition definition, Vector3 position)
    {
        var list = _updates.GetLayer(RegionLayer);

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var region = list[i];

            if (region is null || region.Definition.Object.SpriteClass != SpriteClass.Region || position.Z >= region.Position.Z + RegionReach)
                continue;

            if (!region.IsCoveringMap)
            {
                var half = region.RegionSize * 0.5f;

                if (region.Position.X - half.X > position.X || position.X > half.X + region.Position.X ||
                    region.Position.Y - half.Y > position.Y || position.Y > half.Y + region.Position.Y)
                    continue;
            }

            for (int pair = 0; pair < Sprite.RegionPairs; pair++)
            {
                if (region.RegionFrom[pair] == definition)
                    return region.RegionTo[pair];
            }
        }

        return definition;
    }

    private void CompleteDeath(Sprite sprite, SpriteAnimation animation)
    {
        if (sprite.Placement is not null)
        {
            RemovePlacement(sprite.Placement);
        }

        sprite.DiedAt = Time;
        sprite.ChangeAnimation(animation == SpriteAnimation.Death2 && Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)SpriteAnimation.Death2])) is not null
            ? SpriteAnimation.Death2 : SpriteAnimation.Death);
    }

    private void RemovePlacement(MergedMapSprite placement)
    {
        _removed.Add(placement);


        if (_statics.Remove(placement, out var stand))
        {
            DeleteSprite(stand);
        }
    }

    [EngineRoutine(0x00445770)]
    private void CreateLink(Sprite sprite)
    {
        var definition = sprite.Definition.LinkDefinition;

        if (definition is null || sprite.Link?.Definition == definition || definition.NotCreatedAsChild != 0)
            return;

        var position = sprite.Position + sprite.Definition.GetLinkOffset(definition, sprite.Direction);
        var link = AddSprite(definition, position, sprite.Direction, null);
        AddLink(sprite, link);

        if (sprite.Link is null)
        {
            Session.Logger.Error(SpriteLogTag, LogError.Create, 0, "link"u8, (int)sprite.Definition.Id);

            return;
        }

        ChangeArmy(sprite.Link, (int)sprite.Army);
    }

    private bool IsFree(SpriteDefinition definition, Vector3 position) => Movement.IsAboveGround(definition.Object, position) && !Projectiles.IsOccupied(definition, position);

    private sealed class LivingSet
    {
        public List<Sprite> Sprites { get; } = [];

        public int CheckedAt { get; set; } = -1;

        public void Clear()
        {
            Sprites.Clear();
            CheckedAt = -1;
        }
    }
}
