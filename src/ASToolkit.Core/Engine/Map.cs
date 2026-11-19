using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a running Alien Shooter level with its sprites, update lists, controllers and compiled level script.
/// </summary>
public sealed partial class Map
{
    private const int WindowMessageHitTest = 0x84;
    private const int MaxStreamPeriod = 30000;
    private const float RescueEffectHeight = 22;
    private const int WornArmorShareCount = 3;
    private const int ArmorRounding = 50;
    private const int PlayerSlots = 4;
    private const int ArmyMask = PlayerSlots - 1;

    private readonly GameRandom _rng;
    private readonly MonsterBrain _brain;
    private readonly List<Sprite> _monsters = [];
    private readonly HashSet<Sprite> _monsterSet = [];
    private readonly UpdateOrder _updates;
    private readonly List<Sprite> _builders = [];
    private readonly List<Group> _groups = [];
    private readonly Player[] _players = new Player[PlayerSlots];
    private readonly LivingSet _livingFences = new();
    private readonly LivingSet _livingSites = new();
    private readonly LivingSet _livingCharges = new();

    private GameClock _clock;
    private MapEnvironment _environment;
    private int _spriteCount;
    private bool _playerDestroyed;

    private static ReadOnlySpan<int> WornArmorShares => [50, 70, 90];

    private static ReadOnlySpan<byte> DoorsOpenVariable => "DoorsIsOpen"u8;

    /// <summary>
    /// Creates a map from a level, building its sprites, binding its compiled script and running the creation scripts.
    /// </summary>
    /// <param name="data">The level to load.</param>
    /// <param name="rng">The random source the engine draws from.</param>
    /// <param name="controls">The control settings, or null for the executable's defaults.</param>
    /// <param name="demo">The demo to play back, or null to play without one.</param>
    /// <param name="registry">The registry the level script reads and writes, or null for an empty one.</param>
    /// <param name="session">The map state earlier levels left, or null for a new session.</param>
    [EngineRoutine(0x0040c960)]
    public Map(LevelTemplate data, GameRandom rng, ControlSettings? controls = null, DemoResource? demo = null,
        ScriptRegistry? registry = null, GameSession? session = null)
    {
        Registry = registry ?? new ScriptRegistry("");
        Session = session ?? new GameSession();
        Session.Cursor ??= ConstructCursor(data.Definitions);
        _updates = new UpdateOrder();
        data.Definitions.ReloadRecords();
        data.Definitions.ResetLevelState();
        var model = data.Model;
        var definitions = data.Definitions;
        var frameTimes = data.FrameTimes;
        _clock = new GameClock(frameTimes);
        _sprites = model.Sprites.ToList();
        _environment = model.Environment;
        Definitions = definitions;
        _rng = rng;
        Movement = data.Movement.Clone();
        var legs = definitions.Find(KnownObject.PlayerStart)!;
        Collector = new SpriteCollector(Movement.MapSize, definitions.LargestHashedSize);

        Motion = new SpriteKinematics(definitions, this);
        Effects = new SpawnedObjectSystem(definitions, Movement, rng, this);
        var start = model.Sprites.FirstOrDefault(placement => placement.Id is KnownObject.PlayerStart or KnownObject.PlayerStartFemaleLegs);
        Player = new Sprite(_spriteCount++, legs, start?.Position ?? new Vector3(data.FallbackStart, 0), Army.Player, legs.MaxHp)
        { CreatedAt = Time };
        Weapons = new WeaponSystem(this, definitions, rng);
        var spriteCollision = new SpriteCollision(Movement, Collector);
        PlayerMovement = new PlayerMovement(spriteCollision);
        var settings = controls ?? ControlSettings.Default;
        Input = new PlayerInput(settings);
        for (int slot = 0; slot < PlayerSlots; slot++)
        {
            _players[slot] = new Player(this, spriteCollision, settings);
        }

        _brain = new MonsterBrain(Motion, spriteCollision, rng, _monsters, Session);
        var placedSprites = new Dictionary<int, Sprite>();
        IsLoading = true;

        if (start is null)
        {
            ConstructPlayer(default);
        }

        for (int record = 0; record < model.StoredSprites.Count; record++)
        {
            var placement = model.StoredSprites[record];
            ConstructLoadingScreen(data.LoadingScreenRecords, record);

            if (placement == start)
            {
                ConstructPlayer(default);
                placedSprites[placement.Handle] = Player;
                continue;
            }

            var definition = Definitions.Find(placement.Id);

            if (definition is null)
                continue;

            var sprite = PlaceSprite(placement, definition);

            if (sprite is not null)
            {
                placedSprites[placement.Handle] = sprite;
            }
        }

        ConstructLoadingScreen(data.LoadingScreenRecords, model.StoredSprites.Count);

        foreach (var placement in model.StoredSprites)
        {
            if (!placedSprites.TryGetValue(placement.Handle, out var sprite))
                continue;

            if (SpriteProgram.TryParse(placement.Payload, false, out var program))
            {
                LoadOrders(sprite, program.GetStack(0), placedSprites);
            }

            var items = SpriteProgram.GetItems(placement.Payload);
            if (!items.IsEmpty)
            {
                sprite.Items.Clear();
                foreach (int item in items)
                {
                    sprite.Items.Add(item);
                }
            }
        }

        if (!Script.Load(data.ScriptFiles, data.ScriptPath))
            throw new InvalidDataException($"Cannot compile the script {data.ScriptPath}.");

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

        LoadGroups(model.Groups, placedSprites);
        IsLoading = false;

        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i].Sprite;

                if (sprite is not null && sprite.Role is not (UpdateRole.Player or UpdateRole.Torso))
                {
                    RunCreationHook(sprite);
                }
            }
        }

        SetFlagman(0, Player);
    }

    private void LoadOrders(Sprite sprite, ReadOnlySpan<StackAction> stack, Dictionary<int, Sprite> sprites)
    {
        var orders = new Act[stack.Length];
        for (int i = 0; i < stack.Length; i++)
        {
            var action = stack[i];
            orders[i] = Act.NamesSprite(action.Code)
                ? new Act(action.Act, action.Var1, action.Var2, action.Var3, sprites.GetValueOrDefault(action.Var1))
                : Act.FromAction(action);
        }

        sprite.ActionStack.Load(orders);

        if (sprite.ActionStack.Count > 0)
        {
            _builders.Add(sprite);
        }
    }

    private Sprite? PlaceSprite(MergedMapSprite placement, SpriteDefinition definition)
    {
        if (definition.MaxHp > 0)
        {
            if (placement.Id != KnownObject.Crate && definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia)
                return AddMonster(definition, placement.Position, placement.Direction, placement.Army);

            if (definition.Object.SpriteClass == SpriteClass.Man)
                return _playerDestroyed ? ConstructNewPlayer(definition, placement) : null;

            var body = ConstructSprite(definition, placement.Position, placement.Direction, placement.Army, definition.MaxHp, UpdateRole.Sprite, _spriteCount++);
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


    internal WeaponSystem Weapons { get; }

    /// <summary>
    /// Gets the body of the player, the man the level script made flagman 0.
    /// </summary>
    public Sprite Player { get; private set; }

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
    /// Gets the sprite definitions of the level.
    /// </summary>
    public SpriteCatalog Definitions { get; }

    internal MovementMap Movement { get; }

    internal SpriteCollector Collector { get; }

    internal PlayerMovement PlayerMovement { get; }

    internal ProjectileCollision Projectiles => Weapons.Collision;

    internal SpawnedObjectSystem Effects { get; }

    internal SpriteKinematics Motion { get; }

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

    private List<Sprite> GetLiving(KnownObject id, LivingSet set)
    {
        if (set.CheckedAt == Time)
            return set.Sprites;

        set.CheckedAt = Time;
        set.Sprites.Clear();
        var definition = Definitions.Find(id);

        if (definition is null)
            return set.Sprites;

        foreach (var entry in _updates.GetLayer(definition.Layer))
        {
            var sprite = entry.Sprite;

            if (sprite is not null && sprite.Definition == definition && sprite.Animation < SpriteAnimation.Death)
            {
                set.Sprites.Add(sprite);
            }
        }

        return set.Sprites;
    }

    private sealed class LivingSet
    {
        public List<Sprite> Sprites { get; } = [];

        public int CheckedAt { get; set; } = -1;
    }

    /// <summary>
    /// Gets the sprites the map updates as monsters.
    /// </summary>
    public IReadOnlyList<Sprite> Monsters => _monsters;

    /// <summary>
    /// Sets whether the controllers act on the input.
    /// </summary>
    /// <param name="controlled">A value indicating whether the controllers act on the input.</param>
    public void SetPlayerControl(bool controlled) => IsPlayerControlled = controlled;

    /// <summary>
    /// Moves the cursor to a screen point and returns whether the map cursor changed.
    /// </summary>
    /// <param name="screen">The mouse position on the screen.</param>
    /// <param name="windowOrigin">The window's top-left corner on the screen.</param>
    /// <param name="view">The visible part of the map.</param>
    /// <param name="camera">The camera position on the map.</param>
    /// <returns><see langword="true"/> when the cursor moved; otherwise, <see langword="false"/>.</returns>
    public bool MoveMouse(Point screen, Point windowOrigin, RectangleF view, Vector2 camera)
    {
        bool inView = Input.WindowProc(WindowMessageHitTest, 0, (screen.Y & 0xFFFF) << 16 | (screen.X & 0xFFFF), windowOrigin, view, camera);
        Cursor = Input.Cursor;

        return inView;
    }

    private void SetTime(int time) => _clock.SetTime(time);

    [EngineRoutine(0x0043ad70)]
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
            if (_groups[i].Remove(sprite) && _groups[i].Members.Count == 0)
            {
                _groups.RemoveAt(i);
            }
        }
    }

    private bool TickEntry(UpdateEntry entry, int layer)
    {
        var sprite = entry.Sprite;
        switch (sprite?.Role ?? entry.Role)
        {
            case UpdateRole.Player:
                if (!_playerDestroyed && !Tact(Player))
                {
                    DestroyPlayer();
                }

                return true;
            case UpdateRole.Torso:
                if (sprite != Weapons.Torso)
                {
                    ReleaseSprite(sprite!);
                    return false;
                }

                if (sprite.Definition.Layer != layer)
                    return false;

                Weapons.UpdateTorso();

                return true;
            case UpdateRole.Monster:
                return TickMonster(sprite!);
            case UpdateRole.Scripted:
                return TickScripted(sprite!);
            case UpdateRole.Effect:
                return Effects.Tick(sprite!);
            default:
                if (Tact(sprite!))
                    return true;

                if (sprite!.Placement is null || IsBaked(sprite.Definition))
                {
                    RemoveStatic(sprite);
                }
                else
                {
                    RemovePlacement(sprite.Placement);
                }

                return false;
        }
    }

    /// <summary>
    /// Adds a sprite to its definition's update list.
    /// </summary>
    /// <param name="role">The side list that removes the sprite when it leaves.</param>
    /// <param name="sprite">The sprite.</param>
    public void Register(UpdateRole role, Sprite sprite)
    {
        sprite.Role = role;
        CountSprite(sprite);
        _updates.Add(sprite.Definition.Layer, new UpdateEntry(role, sprite));
    }

    [EngineRoutine(0x004545e0)]
    private void ConstructPlayer(Angle direction)
    {
        Player.Start(GetInitialAnimation(Player.Definition), direction, Time, _rng);
        TryAddItem(Player, Sprite.WeaponItemBase + Sprite.Pistols);
        Weapons.StartTorso(GetInitialAnimation(Weapons.Torso.Definition));
        Register(UpdateRole.Torso, Weapons.Torso);
        Register(UpdateRole.Player, Player);
        Collector.InsertToHash(Player);
    }

    private Sprite ConstructNewPlayer(SpriteDefinition definition, MergedMapSprite placement)
    {
        var man = new Sprite(_spriteCount++, definition, placement.Position, placement.Army, definition.ArmyMaxHp[(int)placement.Army & ArmyMask]) { CreatedAt = Time };
        Player = man;
        _playerDestroyed = false;
        Weapons.AttachTorso();
        ConstructPlayer(placement.Direction);

        return man;
    }

    [EngineRoutine(0x00446c90)]
    private static Sprite ConstructCursor(SpriteCatalog definitions) => new(-1, definitions.Empty, Vector3.Zero, Army.Player, 0);

    [EngineRoutine(0x00446810)]
    private static bool TryAddItem(Sprite sprite, int item)
    {
        if (sprite.Items.Contains(item))
            return false;

        sprite.Items.Add(item);

        return true;
    }

    [EngineRoutine(0x00454640)]
    private void DestroyPlayer()
    {
        var torso = Weapons.Torso;
        _updates.Remove(torso.Definition.Layer, torso);
        _updates.Remove(Player.Definition.Layer, Player);
        Collector.DeleteFromHash(Player);
        Effects.Detach(Player);
        Delete(Player);
        ReleaseSprite(torso);
        ReleaseSprite(Player);
        _playerDestroyed = true;
    }

    [EngineRoutine(0x004333d0)]
    private void ConstructLoadingScreen(IReadOnlyList<int> redraws, int record)
    {
        if (!redraws.Contains(record))
            return;

        var definition = Definitions.LoadingScreen ?? throw new InvalidOperationException("Missing the loading screen definition.");
        new Sprite(-1, definition, Vector3.Zero, Army.Player, 0).Start(GetInitialAnimation(definition), default, Time, _rng);
    }

    [EngineRoutine(0x0043f200)]
    private Sprite ConstructSprite(SpriteDefinition definition, Vector3 position, Angle direction, Army army, int hp, UpdateRole role, int number = -1)
    {
        var sprite = new Sprite(number, definition, position, army, hp) { CreatedAt = Time };
        var state = GetInitialAnimation(definition);
        sprite.Start(state, direction, Time, _rng);
        Register(role, sprite);
        CreateLink(sprite);
        Collector.InsertToHash(sprite);

        if (state != SpriteAnimation.Birth && !IsLoading)
        {
            Effects.Emit(definition, SpriteAnimation.Birth, sprite.Position, sprite.Direction, Time);
        }

        return sprite;
    }

    /// <summary>
    /// Returns the state a new sprite of a definition starts in.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The starting state.</returns>
    public SpriteAnimation GetInitialAnimation(SpriteDefinition definition)
    {
        var state = definition.InitialAnimation;

        return IsLoading && state == SpriteAnimation.Birth ? SpriteAnimation.Stand : state;
    }

    /// <summary>
    /// Gets a value indicating whether the map is still loading.
    /// </summary>
    public bool IsLoading { get; }

    internal IReadOnlyList<UpdateEntry> GetLayer(int layer) => _updates.GetLayer(layer);

    /// <summary>
    /// Gets the sprites whose orders build units.
    /// </summary>
    public IReadOnlyList<Sprite> Builders => _builders;

    /// <summary>
    /// Runs the script's main function, one frame of the update lists and then each controller on the input.
    /// </summary>
    [EngineRoutine(0x00405190)]
    public void Step()
    {
        Script.CallFunction(this, MainFunction, 0, 0, 0);
        ShakeCamera();

        for (int layer = 0; layer < UpdateOrder.UpdatedLayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i];

                if (entry.Sprite is not null && !TickEntry(entry, layer))
                {
                    UpdateOrder.Clear(list, i);
                }
            }
        }

        Effects.Compact();

        if (IsPlayerControlled)
        {
            foreach (var controller in _players)
            {
                controller.Control(Input);
            }
        }

        PlayMusic();
    }

    // TODO: Port the music
    [EngineRoutine(0x0041d770)]
    private void PlayMusic() => _rng.Next();

    /// <summary>
    /// Returns the sprite a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <returns>The sprite, or null when the slot has none.</returns>
    [EngineRoutine(0x0040e620)]
    public Sprite? GetFlagman(int army) => _players[army & ArmyMask].Sprite;

    /// <summary>
    /// Sets the sprite a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <param name="sprite">The sprite, or null to clear the slot.</param>
    [EngineRoutine(0x0040e600)]
    public void SetFlagman(int army, Sprite? sprite) => _players[army & ArmyMask].SetSprite(sprite);

    /// <summary>
    /// Advances the game clock to the next frame of wall time and compacts the update lists.
    /// </summary>
    [EngineRoutine(0x0040f210)]
    public void AdvanceClock()
    {
        uint wallTime = _clock.GetNextWallTime();
        _clock.StartTact(wallTime, wallTime);
        _updates.Compact(Time);
        Input.ResetFrame();
    }

    private bool TickMonster(Sprite sprite)
    {
        var owner = sprite.Owner;

        if (owner is not null)
        {
            if (owner.Link != sprite || (owner.Number >= 0 && !_monsterSet.Contains(owner)))
            {
                RemoveMonster(sprite);
                Delete(sprite);
                ReleaseSprite(sprite);

                return false;
            }
        }

        if (Tact(sprite))
            return true;

        RemoveMonster(sprite);
        Collector.DeleteFromHash(sprite);
        Delete(sprite);
        ReleaseSprite(sprite);

        return false;
    }

    private void Delete(Sprite removed)
    {
        foreach (var sprite in _monsters)
        {
            if (SpriteKinematics.ForgetObject(sprite, removed))
            {
                DrawGoalSpriteRandom();
            }
        }

        if (SpriteKinematics.ForgetObject(Weapons.Torso, removed))
        {
            DrawGoalSpriteRandom();
        }
        RemoveFromGroups(removed);
        Effects.Forget(removed);
        foreach (var controller in _players)
        {
            controller.Forget(removed);
        }

        if (removed.Link?.Owner == removed)
        {
            Effects.Detach(removed);
        }
    }

    /// <summary>
    /// Updates a sprite for one frame and returns whether it stays in the map.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns><see langword="true"/> while the sprite lives; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00440110)]
    public bool Tact(Sprite sprite)
    {
        var layout = sprite.Definition.Layout;

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
                    keyPeriod = layout.GetFrameDuration(sprite.Animation) * (sprite.LastFrame - sprite.FirstFrame);
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
                // Search from the key after the previous position, reading past the key times as the engine does
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

        EmitStream(sprite);
        var state = sprite.Animation;

        if (sprite.CreatedAt == Time || Time - Time % layout.GetFrameDuration(state) <= Time - FrameTime)
            return true;

        if (sprite.Command is not Command.None && sprite is { Command: < Command.Build, HasGoal: false })
        {
            SpriteKinematics.SetCommand(sprite, Command.None);
        }

        if (sprite.Timer != 0)
        {
            sprite.Timer = Time - sprite.LastTickTime < sprite.Timer ? sprite.Timer - (Time - sprite.LastTickTime) : 0;

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

        CreateChild(sprite);
        sprite.Frame++;

        if (sprite.Animation >= SpriteAnimation.Death && (sprite.Frame > sprite.LastFrame || layout.GetFrameCount(sprite.Animation) == 0))
        {
            sprite.Frame = sprite.LastFrame;

            if (sprite.Definition.Damage != 0)
            {
                Weapons.ApplyBlast(sprite);
            }

            return false;
        }

        if (sprite.Command is Command.Passive or Command.Pause)
        {
            state = sprite.Animation;

            if (state < SpriteAnimation.Death && state != SpriteAnimation.StandOpen && sprite.Frame > sprite.LastFrame)
            {
                if (sprite.Speed.IsZero())
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
                    (state is > SpriteAnimation.Open and < SpriteAnimation.Death and not SpriteAnimation.StandOpen || state == SpriteAnimation.Go && sprite.Speed.IsZero()))
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
                        Action(sprite, order.Code, order.Var1, order.Var2, order.Var3, order.Target);
                    }
                }
            }
        }

        if (sprite.Lifetime != SpriteDefinition.Unlimited && sprite.Animation < SpriteAnimation.Death)
        {
            if (Time - sprite.LastTickTime < sprite.Lifetime)
            {
                sprite.Lifetime -= Time - sprite.LastTickTime;
            }
            else
            {
                sprite.ChangeAnimation(SpriteAnimation.Death);
            }
        }

        int period = sprite.Definition.Weapon.AnimationPeriod;
        if (sprite.Definition.HasKeyframes && period != SpriteDefinition.Unlimited && (uint)period < (uint)(Time - sprite.KeyframeStart))
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

    private void MoveByClass(Sprite sprite)
    {
        switch (sprite.Definition.Object.SpriteClass)
        {
            case SpriteClass.Man when sprite == Player:
                PlayerMovement.Tick(Player, IsInCannon ? 0 : Player.MaxSpeed, FrameTime);
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

    private static float GetKeyTime(in WeaponRecord weapon, int key) =>
        key < 0 ? BitConverter.Int32BitsToSingle(weapon.AnimationPeriod)
        : key < WeaponRecord.KeyframeCount ? weapon.KeyTimes[key] : BitConverter.Int32BitsToSingle(weapon.RedKeys[0]);

    private void EmitStream(Sprite sprite)
    {
        var state = sprite.Animation;
        var child = Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)state]));

        if (child is null || !child.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke) || Time - Time % sprite.StreamPeriod <= Time - FrameTime)
            return;

        var velocity = SpriteKinematics.Displace(Vector3.Zero, sprite.Direction, sprite.Speed, 1);
        float across = velocity.X;
        float down = velocity.Y + sprite.VerticalSpeed - child.ZSpeed;
        float acrossPeriod = across.IsZero() ? MaxStreamPeriod : child.Object.Size.X / Math.Abs(across);
        float downPeriod = down.IsZero() ? MaxStreamPeriod : child.Object.Size.Y / Math.Abs(down);
        int period = (int)Math.Min(acrossPeriod, downPeriod);

        if (state == SpriteAnimation.Fight && sprite.Definition.AttacksTwice)
        {
            period >>= 1;
        }

        sprite.StreamPeriod = Math.Clamp(period, 1, MaxStreamPeriod);
        CreateChildFor(sprite, state);
    }

    /// <summary>
    /// Creates the child objects a sprite's definition lists for a state.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="state">The state whose children to create.</param>
    [EngineRoutine(0x00446430)]
    public void CreateChildFor(Sprite sprite, SpriteAnimation state) =>
        Effects.Emit(sprite.Definition, state, sprite.Position, sprite.Direction, Time, sprite.Depth, sprite.TakeChildPulse(state), sprite);

    [EngineRoutine(0x00444dd0)]
    private void CreateChild(Sprite sprite)
    {
        var state = sprite.Animation;
        int child = sprite.Definition.Children[(int)state];

        if (child == 0 || Definitions.Find((KnownObject)Math.Abs(child))?.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke) == true)
            return;

        var flags = sprite.Definition.Object.Properties;
        int emissionFrame = flags.HasFlag(ObjectProperties.CreateChildEnd) ? sprite.LastFrame : sprite.FirstFrame;
        bool emits = state == SpriteAnimation.Fight && sprite.Definition.AttacksTwice
            ? sprite.Frame == (sprite.LastFrame + 1 + sprite.FirstFrame) / 2 || sprite.Frame == emissionFrame
            : flags.HasFlag(ObjectProperties.Track) || sprite.Frame == emissionFrame;

        if (!emits)
            return;

        if (state != SpriteAnimation.Fight)
        {
            CreateChildFor(sprite, state);

            return;
        }

        Weapons.FireFrom(sprite);

        if (sprite.Command == Command.OneAttack && (!flags.HasFlag(ObjectProperties.Track) || sprite.Frame == sprite.LastFrame))
        {
            SpriteKinematics.SetCommand(sprite, Command.None);
        }
    }

    /// <summary>
    /// Returns the next sprite number.
    /// </summary>
    /// <returns>The number.</returns>
    public int AllocateNumber() => _spriteCount++;

    private void AddToMonsters(Sprite sprite)
    {
        _monsters.Add(sprite);
        _monsterSet.Add(sprite);
    }

    private void RemoveMonster(Sprite sprite)
    {
        _builders.Remove(sprite);
        _monsters.Remove(sprite);
        _monsterSet.Remove(sprite);
    }

    private void CompleteDeath(Sprite sprite, SpriteAnimation animation)
    {
        if (sprite.Placement is not null && sprite != Player)
        {
            RemovePlacement(sprite.Placement);
            AddToMonsters(sprite);
            sprite.Role = UpdateRole.Monster;
        }

        sprite.DiedAt = Time;
        sprite.ChangeAnimation(animation == SpriteAnimation.Death2 && sprite.Definition.Children[(int)SpriteAnimation.Death2] != 0
            ? SpriteAnimation.Death2 : SpriteAnimation.Death);
    }

    private void RemovePlacement(MergedMapSprite placement)
    {
        _removed.Add(placement);


        if (_statics.Remove(placement, out var stand))
        {
            _builders.Remove(stand);
            _updates.Remove(stand.Definition.Layer, stand);
            Collector.DeleteFromHash(stand);
            Effects.Detach(stand);
            ReleaseSprite(stand);
        }

        ReleasePlacement(placement);

        Movement.Remove(placement);
    }

    /// <summary>
    /// Creates a sprite the map updates as a monster.
    /// </summary>
    /// <param name="definition">The sprite's definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="army">The army.</param>
    /// <returns>The sprite.</returns>
    public Sprite AddMonster(SpriteDefinition definition, Vector3 position, Angle direction = default, Army army = Army.Monsters)
    {
        var sprite = ConstructSprite(definition, position, direction, army, definition.MaxHp, UpdateRole.Monster, _spriteCount++);
        AddToMonsters(sprite);

        if (!IsLoading)
        {
            RunCreationHook(sprite);
        }

        return sprite;
    }

    private void CreateLink(Sprite sprite) => Effects.CreateLink(sprite, Time, 0);

    [EngineRoutine(0x00444970)]
    private static void AddLink(Sprite owner, Sprite link)
    {
        var previous = owner.Link;
        owner.Link = link;
        link.Owner = owner;

        if (previous is null || previous == link)
            return;

        previous.Owner = null;
        SpawnedObjectSystem.AppendLink(link, previous);
    }

    /// <summary>
    /// Creates a sprite's linked object as a scripted sprite.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="definition">The linked object's definition.</param>
    public void AttachObjectLink(Sprite sprite, SpriteDefinition definition)
    {
        if (definition.Id.IsScripted)
        {
            var scripted = AddScripted(definition, GetLinkPosition(sprite, definition), sprite.Direction, sprite.Army);
            AddLink(sprite, scripted);

            return;
        }

        var link = new Sprite(_spriteCount++, definition, GetLinkPosition(sprite, definition), sprite.Army, definition.MaxHp) { CreatedAt = Time };
        link.Start(definition.InitialAnimation, sprite.Direction, Time, _rng);
        AddLink(sprite, link);

        if (definition.Object.SpriteClass == SpriteClass.Creature)
        {
            Motion.StartMove(link);
        }

        if (sprite.ArmedLink is not null && link.Definition.FightChild != 0)
        {
            sprite.Behavior = definition.Behavior;
        }

        AddToMonsters(link);
        Register(UpdateRole.Monster, link);
        CreateLink(link);
    }

    private static Vector3 GetLinkPosition(Sprite owner, SpriteDefinition definition) =>
        owner.Position + owner.Definition.GetLinkOffset(definition, owner.Direction);

    private bool IsFree(SpriteDefinition definition, Vector3 position) => Movement.IsAboveGround(definition.Object, position) && !Projectiles.IsOccupied(definition, position);


}
