using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a running Alien Shooter level with its sprites, update lists, controllers and compiled level script.
/// </summary>
public sealed partial class GameWorld
{
    private const int MaxStreamPeriod = 30000;
    private const float RescueEffectHeight = 22;
    private const int WornArmorShareCount = 3;
    private const int ArmorRounding = 50;
    private const int PlayerSlots = 4;
    private const int ArmyMask = PlayerSlots - 1;

    private readonly GameRandom _rng;
    private readonly MonsterBrain _brain;
    private readonly List<Unit> _monsters = [];
    private readonly HashSet<Unit> _monsterSet = [];
    private readonly UpdateOrder _updates;
    private readonly bool _loading;
    private readonly List<Unit> _builders = [];
    private readonly List<UnitGroup> _groups = [];
    private readonly PlayerController[] _controllers = new PlayerController[PlayerSlots];
    private readonly LivingSet _livingFences = new();
    private readonly LivingSet _livingSites = new();
    private readonly LivingSet _livingCharges = new();

    private GameClock _clock;
    private MapEnvironment _environment;
    private Vector3 _respawnPosition;
    private int _unitCount;
    private bool _playerDestroyed;

    private static ReadOnlySpan<int> WornArmorShares => [50, 70, 90];

    private static ReadOnlySpan<byte> DoorsOpenVariable => "DoorsIsOpen"u8;

    /// <summary>
    /// Creates a world from a level, building its sprites, binding its compiled script and running the creation scripts.
    /// </summary>
    /// <param name="data">The level to load.</param>
    /// <param name="rng">The random source the engine draws from.</param>
    /// <param name="controls">The control settings, or null for the executable's defaults.</param>
    /// <param name="demo">The demo to play back, or null to play without one.</param>
    /// <param name="registry">The registry the level script reads and writes, or null for an empty one.</param>
    /// <param name="session">The map state earlier levels left, or null for a new session.</param>
    public GameWorld(LevelTemplate data, GameRandom rng, ControlSettings? controls = null, DemoResource? demo = null,
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
        Units = new UnitGrid(Movement.MapSize, definitions.LargestHashedSize);

        Motion = new UnitKinematics(definitions, this);
        Effects = new SpawnedObjectSystem(definitions, Movement, rng, this);
        var start = model.Sprites.FirstOrDefault(sprite => sprite.Id is KnownObject.PlayerStart or KnownObject.PlayerStartFemaleLegs);
        Player = new Unit(_unitCount++, legs, start?.Position ?? new Vector3(data.FallbackStart, 0), Army.Player, legs.MaxHp)
        { CreatedAt = Time };
        Weapons = new WeaponSystem(this, definitions, rng);
        var unitMovement = new UnitCollision(Movement, Units);
        PlayerMovement = new PlayerMovement(unitMovement);
        var settings = controls ?? ControlSettings.Default;
        Input = new PlayerInput(settings);
        for (int slot = 0; slot < PlayerSlots; slot++)
        {
            _controllers[slot] = new PlayerController(this, unitMovement, settings);
        }

        _brain = new MonsterBrain(Motion, unitMovement, rng, _monsters, Session);
        var placedUnits = new Dictionary<int, Unit>();
        _loading = true;

        if (start is null)
        {
            ConstructPlayer(default);
        }

        for (int record = 0; record < model.StoredSprites.Count; record++)
        {
            var sprite = model.StoredSprites[record];
            ConstructLoadingScreen(data.LoadingScreenRecords, record);

            if (sprite == start)
            {
                ConstructPlayer(default);
                placedUnits[sprite.Handle] = Player;
                continue;
            }

            var definition = Definitions.Find(sprite.Id);

            if (definition is null)
                continue;

            var unit = PlaceSprite(sprite, definition);

            if (unit is not null)
            {
                placedUnits[sprite.Handle] = unit;
            }
        }

        ConstructLoadingScreen(data.LoadingScreenRecords, model.StoredSprites.Count);

        foreach (var sprite in model.StoredSprites)
        {
            if (!placedUnits.TryGetValue(sprite.Handle, out var unit))
                continue;

            if (SpriteProgram.TryParse(sprite.Payload, false, out var program))
            {
                LoadOrders(unit, program.GetStack(0), placedUnits);
            }

            var items = SpriteProgram.GetItems(sprite.Payload);
            if (!items.IsEmpty)
            {
                unit.Items.Clear();
                foreach (int item in items)
                {
                    unit.Items.Add(item);
                }
            }
        }

        if (!Script.Load(data.ScriptFiles, data.ScriptPath))
            throw new InvalidDataException($"Cannot compile the script {data.ScriptPath}.");

        BindScript();

        if (demo is not null)
        {
            StartDemoPlayback(demo.Value);
        }

        LoadGroups(model.Groups, placedUnits);
        _loading = false;
        RunCreationScripts();

        SetFlagman(0, Player);
        _respawnPosition = Player.Position;
    }

    [EngineRoutine(0x00440830)]
    private void LoadOrders(Unit unit, ReadOnlySpan<StackAction> stack, Dictionary<int, Unit> units)
    {
        var orders = new UnitOrder[stack.Length];
        for (int i = 0; i < stack.Length; i++)
        {
            var action = stack[i];
            orders[i] = UnitOrder.NamesSprite(action.Code)
                ? new UnitOrder(action.Act, action.Var1, action.Var2, action.Var3, units.GetValueOrDefault(action.Var1))
                : UnitOrder.FromAction(action);
        }

        unit.Orders.Load(orders);

        if (unit.Orders.Count > 0)
        {
            _builders.Add(unit);
        }
    }

    [EngineRoutine(0x00405bd0)]
    [EngineRoutine(0x0040e870)]
    private Unit? PlaceSprite(MergedMapSprite sprite, SpriteDefinition definition)
    {
        if (definition.MaxHp > 0)
        {
            if (sprite.Id != KnownObject.Crate && definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia)
                return AddMonster(definition, sprite.Position, sprite.Direction, sprite.Army);

            if (definition.Object.SpriteClass == SpriteClass.Man)
                return _playerDestroyed ? ConstructNewPlayer(definition, sprite) : null;

            var body = ConstructSprite(definition, sprite.Position, sprite.Direction, sprite.Army, definition.MaxHp, UpdateRole.Sprite, _unitCount++);
            body.Placement = sprite;
            _bodies.Add(sprite, body);

            return body;
        }

        if (definition.Object.SpriteClass == SpriteClass.Cannon)
        {
            AddPlacedCannon(sprite);
            return null;
        }

        var unit = sprite.Id.IsScripted ? AddScripted(definition, sprite.Position, sprite.Direction, sprite.Army, sprite) : AddStatic(sprite, definition);

        return unit;
    }

    private void AddPlacedCannon(MergedMapSprite sprite)
    {
        Effects.Spawn(sprite.Id, sprite.Position, sprite.Direction, Time);
    }


    internal WeaponSystem Weapons { get; }

    /// <summary>
    /// Gets the body of the player, the man the level script made flagman 0.
    /// </summary>
    public Unit Player { get; private set; }

    /// <summary>
    /// Gets the cursor position in map coordinates.
    /// </summary>
    public Vector2 Cursor { get; private set; }

    /// <summary>
    /// Gets the input the controllers read this frame.
    /// </summary>
    public PlayerInput Input { get; }

    /// <summary>
    /// Gets the unit groups of the map.
    /// </summary>
    public IReadOnlyList<UnitGroup> Groups => _groups;

    /// <summary>
    /// Gets a value indicating whether the controllers act on the input.
    /// </summary>
    public bool IsPlayerControlled { get; private set; } = true;

    /// <summary>
    /// Gets the sprite definitions of the level.
    /// </summary>
    public SpriteCatalog Definitions { get; }

    internal MovementMap Movement { get; }

    internal UnitGrid Units { get; }

    internal PlayerMovement PlayerMovement { get; }

    internal ProjectileCollision Projectiles => Weapons.Collision;

    internal SpawnedObjectSystem Effects { get; }

    internal UnitKinematics Motion { get; }

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

    private List<Unit> GetLiving(KnownObject id, LivingSet set)
    {
        if (set.CheckedAt == Time)
            return set.Units;

        set.CheckedAt = Time;
        set.Units.Clear();
        var definition = Definitions.Find(id);

        if (definition is null)
            return set.Units;

        foreach (var entry in _updates.GetLayer(definition.Layer))
        {
            var unit = entry.Unit;

            if (unit is not null && unit.Definition == definition && unit.Animation < SpriteAnimation.Death)
            {
                set.Units.Add(unit);
            }
        }

        return set.Units;
    }

    private sealed class LivingSet
    {
        public List<Unit> Units { get; } = [];

        public int CheckedAt { get; set; } = -1;
    }

    /// <summary>
    /// Gets the units the world updates as monsters.
    /// </summary>
    public IReadOnlyList<Unit> Monsters => _monsters;

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
        bool inView = Input.MoveMouse(screen, windowOrigin, view, camera);
        Cursor = Input.Cursor;

        return inView;
    }

    private void SetTime(int time) => _clock.SetTime(time);

    [EngineRoutine(0x0043ad70)]
    private void LoadGroups(IReadOnlyList<IReadOnlyList<int>> groups, Dictionary<int, Unit> placedUnits)
    {
        foreach (var handles in groups)
        {
            UnitGroup? group = null;
            foreach (int handle in handles)
            {
                if (!placedUnits.TryGetValue(handle, out var unit))
                    continue;

                if (group is null)
                {
                    group = new UnitGroup(unit);
                    _groups.Add(group);
                }
                else
                {
                    group.Add(unit);
                }
            }
        }
    }

    [EngineRoutine(0x0043adf0)]
    private void RemoveFromGroups(Unit unit)
    {
        for (int i = _groups.Count - 1; i >= 0; i--)
        {
            if (_groups[i].Remove(unit) && _groups[i].Members.Count == 0)
            {
                _groups.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Runs the level script's main function and updates every sprite of the update lists.
    /// </summary>
    [EngineRoutine(0x00405190)]
    public void Update()
    {
        RunMain();
        ShakeCamera();

        for (int layer = 0; layer < UpdateOrder.UpdatedLayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i];

                if (entry.Unit is not null && !TickEntry(entry, layer))
                {
                    UpdateOrder.Clear(list, i);
                }
            }
        }

        Effects.Compact();
    }

    private bool TickEntry(UpdateEntry entry, int layer)
    {
        var unit = entry.Unit;
        switch (unit?.Role ?? entry.Role)
        {
            case UpdateRole.Player:
                if (!_playerDestroyed && !Tact(Player))
                {
                    DestroyPlayer();
                }

                return true;
            case UpdateRole.Torso:
                if (unit != Weapons.Torso)
                {
                    ReleaseSprite(unit!);
                    return false;
                }

                if (unit.Definition.Layer != layer)
                    return false;

                if (Player.IsAlive)
                {
                    Weapons.UpdateTorso();
                }

                return true;
            case UpdateRole.Monster:
                return TickMonster(unit!);
            case UpdateRole.Scripted:
                return TickScripted(unit!);
            case UpdateRole.Effect:
                return Effects.Tick(unit!);
            default:
                if (Tact(unit!))
                    return true;

                if (unit!.Placement is null || IsBaked(unit.Definition))
                {
                    RemoveStatic(unit);
                }
                else
                {
                    RemovePlacement(unit.Placement);
                }

                return false;
        }
    }

    /// <summary>
    /// Adds a unit to its definition's update list.
    /// </summary>
    /// <param name="role">The side list that removes the unit when it leaves.</param>
    /// <param name="unit">The unit.</param>
    public void Register(UpdateRole role, Unit unit)
    {
        unit.Role = role;
        CountSprite(unit);
        _updates.Add(unit.Definition.Layer, new UpdateEntry(role, unit));
    }

    [EngineRoutine(0x004545e0)]
    private void ConstructPlayer(Angle direction)
    {
        Player.Start(GetInitialAnimation(Player.Definition), direction, Time, _rng);
        TryAddItem(Player, Unit.WeaponItemBase + Unit.Pistols);
        Weapons.StartTorso(GetInitialAnimation(Weapons.Torso.Definition));
        Register(UpdateRole.Torso, Weapons.Torso);
        Register(UpdateRole.Player, Player);
        Units.Add(Player);
    }

    private Unit ConstructNewPlayer(SpriteDefinition definition, MergedMapSprite sprite)
    {
        var man = new Unit(_unitCount++, definition, sprite.Position, sprite.Army, definition.ArmyMaxHp[(int)sprite.Army & ArmyMask]) { CreatedAt = Time };
        Player = man;
        _playerDestroyed = false;
        Weapons.AttachTorso();
        ConstructPlayer(sprite.Direction);
        _respawnPosition = man.Position;

        return man;
    }

    [EngineRoutine(0x00446c90)]
    private static Unit ConstructCursor(SpriteCatalog definitions) => new(-1, definitions.Empty, Vector3.Zero, Army.Player, 0);

    [EngineRoutine(0x00446810)]
    private static bool TryAddItem(Unit unit, int item)
    {
        if (unit.Items.Contains(item))
            return false;

        unit.Items.Add(item);

        return true;
    }

    [EngineRoutine(0x00454640)]
    private void DestroyPlayer()
    {
        var torso = Weapons.Torso;
        _updates.Remove(torso.Definition.Layer, torso);
        _updates.Remove(Player.Definition.Layer, Player);
        Units.Remove(Player);
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
        new Unit(-1, definition, Vector3.Zero, Army.Player, 0).Start(GetInitialAnimation(definition), default, Time, _rng);
    }

    [EngineRoutine(0x0043f200)]
    private Unit ConstructSprite(SpriteDefinition definition, Vector3 position, Angle direction, Army army, int hp, UpdateRole role, int number = -1)
    {
        var unit = new Unit(number, definition, position, army, hp) { CreatedAt = Time };
        var state = GetInitialAnimation(definition);
        unit.Start(state, direction, Time, _rng);
        Register(role, unit);
        CreateLink(unit);
        Units.Add(unit);

        if (state != SpriteAnimation.Birth && !_loading)
        {
            Effects.Emit(definition, SpriteAnimation.Birth, unit.Position, unit.Direction, Time);
        }

        return unit;
    }

    /// <summary>
    /// Returns the state a new sprite of a definition starts in.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The starting state.</returns>
    [EngineRoutine(0x0043f200)]
    public SpriteAnimation GetInitialAnimation(SpriteDefinition definition)
    {
        var state = definition.InitialAnimation;

        return _loading && state == SpriteAnimation.Birth ? SpriteAnimation.Stand : state;
    }

    /// <summary>
    /// Gets a value indicating whether the map is still loading.
    /// </summary>
    public bool IsLoading => _loading;

    internal IReadOnlyList<UpdateEntry> GetLayer(int layer) => _updates.GetLayer(layer);

    /// <summary>
    /// Gets the sprites whose orders build units.
    /// </summary>
    public IReadOnlyList<Unit> Builders => _builders;

    [EngineRoutine(0x0040c960)]
    private void RunCreationScripts()
    {
        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var unit = list[i].Unit;

                if (unit is not null && unit.Role is not (UpdateRole.Player or UpdateRole.Torso))
                {
                    RunCreationHook(unit);
                }
            }
        }
    }

    /// <summary>
    /// Runs one frame of the update lists and then each controller on the input.
    /// </summary>
    [EngineRoutine(0x00405190)]
    public void Step()
    {
        Update();

        if (IsPlayerControlled)
        {
            foreach (var controller in _controllers)
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
    /// Returns the unit a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <returns>The unit, or null when the slot has none.</returns>
    [EngineRoutine(0x0040e620)]
    public Unit? GetFlagman(int army) => _controllers[army & ArmyMask].Unit;

    /// <summary>
    /// Sets the unit a player slot controls.
    /// </summary>
    /// <param name="army">The player slot.</param>
    /// <param name="unit">The unit, or null to clear the slot.</param>
    [EngineRoutine(0x0040e600)]
    public void SetFlagman(int army, Unit? unit) => _controllers[army & ArmyMask].SetUnit(unit);

    /// <summary>
    /// Advances the game clock to the next frame of wall time and compacts the update lists.
    /// </summary>
    [EngineRoutine(0x0040f210)]
    public void AdvanceClock()
    {
        uint wallTime = _clock.GetNextWallTime();
        _clock.Advance(wallTime, wallTime);
        _updates.Compact(Time);
        Input.ResetFrame();
    }

    private bool TickMonster(Unit unit)
    {
        var owner = unit.Owner;

        if (owner is not null)
        {
            if (owner.Link != unit || (owner.Number >= 0 && !_monsterSet.Contains(owner)))
            {
                RemoveMonster(unit);
                Delete(unit);
                ReleaseSprite(unit);

                return false;
            }

            unit.Position = GetLinkPosition(owner, unit.Definition);
        }

        if (Tact(unit))
            return true;

        RemoveMonster(unit);
        Units.Remove(unit);
        Delete(unit);
        ReleaseSprite(unit);

        return false;
    }

    private void Delete(Unit removed)
    {
        foreach (var unit in _monsters)
        {
            if (UnitKinematics.ForgetObject(unit, removed))
            {
                DrawGoalSpriteRandom();
            }
        }

        if (UnitKinematics.ForgetObject(Weapons.Torso, removed))
        {
            DrawGoalSpriteRandom();
        }
        RemoveFromGroups(removed);
        Effects.Forget(removed);
        foreach (var controller in _controllers)
        {
            controller.Forget(removed);
        }

        if (removed.Link?.Owner == removed)
        {
            Effects.Detach(removed);
        }
    }

    /// <summary>
    /// Updates a sprite for one frame and returns whether it stays in the world.
    /// </summary>
    /// <param name="unit">The sprite.</param>
    /// <returns><see langword="true"/> while the sprite lives; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00440110)]
    public bool Tact(Unit unit)
    {
        var layout = unit.Definition.Layout;
        UpdateKeyframe(unit);

        if (unit.Owner is null)
        {
            MoveByClass(unit);
        }

        EmitStream(unit);
        var state = unit.Animation;

        if (unit.CreatedAt == Time || Time - Time % layout.GetFrameDuration(state) <= Time - FrameTime)
            return true;

        if (unit.Command is not UnitCommand.None && unit is { Command: < UnitCommand.Build, HasGoal: false })
        {
            UnitKinematics.SetCommand(unit, UnitCommand.None);
        }

        if (unit.Timer != 0)
        {
            unit.Timer = Time - unit.LastTickTime < unit.Timer ? unit.Timer - (Time - unit.LastTickTime) : 0;

            if (unit is { Timer: 0, Command: UnitCommand.Pause })
            {
                UnitKinematics.SetCommand(unit, UnitCommand.None);
            }
        }

        bool everyFrame = unit.Definition.Object.Properties.HasFlag(ObjectProperties.Track);

        if (RunStateHandlers(unit, everyFrame))
            return true;

        EmitStateChildren(unit);
        unit.Frame++;

        if (unit.Animation >= SpriteAnimation.Death && (unit.Frame > unit.LastFrame || layout.GetFrameCount(unit.Animation) == 0))
        {
            unit.Frame = unit.LastFrame;

            if (unit.Definition.Damage != 0)
            {
                Weapons.ApplyBlast(unit);
            }

            return false;
        }

        if (unit.Command is UnitCommand.Passive or UnitCommand.Pause)
        {
            HoldStill(unit);
        }
        else if (unit.Frame > unit.LastFrame || everyFrame)
        {
            if (unit.Orders.Count == 0 || unit.Command != UnitCommand.None)
            {
                Action(unit, ActionCode.NextCommand);
            }
            else
            {
                PopOrder(unit);
            }
        }

        if (unit.Lifetime != SpriteDefinition.Unlimited && unit.Animation < SpriteAnimation.Death)
        {
            if (Time - unit.LastTickTime < unit.Lifetime)
            {
                unit.Lifetime -= Time - unit.LastTickTime;
            }
            else
            {
                unit.ChangeAnimation(SpriteAnimation.Death);
            }
        }

        int period = unit.Definition.Weapon.AnimationPeriod;
        if (unit.Definition.HasKeyframes && period != SpriteDefinition.Unlimited && (uint)period < (uint)(Time - unit.KeyframeStart))
        {
            unit.KeyframeStart = Time;
        }

        unit.LastTickTime = Time;

        if (unit.Frame > unit.LastFrame)
        {
            unit.Frame = unit.FirstFrame;
        }

        return true;
    }

    [EngineRoutine(0x00454670)]
    [EngineRoutine(0x00447260)]
    [EngineRoutine(0x0043f820)]
    private void MoveByClass(Unit unit)
    {
        switch (unit.Definition.Object.SpriteClass)
        {
            case SpriteClass.Man when unit == Player:
                PlayerMovement.Tick(Player, IsInCannon ? 0 : Player.MaxSpeed, FrameTime);
                break;
            case SpriteClass.Unit or SpriteClass.Avia:
                if (unit.IsAlive)
                {
                    _brain.Move(unit, FrameTime, _clock.FramesPerSecond);
                }

                break;
            case SpriteClass.Building:
                break;
            default:
                Effects.Move(unit, FrameTime);
                break;
        }
    }

    [EngineRoutine(0x00440110)]
    private static void HoldStill(Unit unit)
    {
        var state = unit.Animation;

        if (state >= SpriteAnimation.Death || state == SpriteAnimation.StandOpen || unit.Frame <= unit.LastFrame)
            return;

        if (unit.Speed.IsZero())
        {
            if (state is SpriteAnimation.Go or > SpriteAnimation.Open)
            {
                unit.ChangeAnimation(SpriteAnimation.Stand);
            }
        }
        else if (state != SpriteAnimation.Go)
        {
            unit.ChangeAnimation(SpriteAnimation.Go);
        }
    }

    [EngineRoutine(0x00440110)]
    private void UpdateKeyframe(Unit unit)
    {
        if (!unit.Definition.HasKeyframes)
            return;

        ref readonly var weapon = ref unit.Definition.Weapon;
        int elapsed = Time - unit.KeyframeStart;
        int period = weapon.AnimationPeriod;
        if (period == SpriteDefinition.Unlimited)
        {
            if (unit.Animation > SpriteAnimation.Birth && unit.LastFrame != unit.FirstFrame)
            {
                period = unit.Definition.Layout.GetFrameDuration(unit.Animation) * (unit.LastFrame - unit.FirstFrame);
            }
            else if (unit.Lifetime == SpriteDefinition.Unlimited)
            {
                return;
            }
            else
            {
                period = unit.Lifetime + elapsed;
            }
        }

        if (period == 0)
            return;

        // Search from the key after the previous position, reading past the key times as the engine does
        int key = (int)unit.KeyframePosition + 1;
        while (key < WeaponRecord.KeyframeCount - 1 && period * GetKeyTime(weapon, key) <= elapsed)
        {
            key++;
        }

        key--;
        float start = period * GetKeyTime(weapon, key);
        unit.KeyframePosition = (elapsed - start) / ((GetKeyTime(weapon, key + 1) - GetKeyTime(weapon, key)) * period) + key;
    }

    private static float GetKeyTime(in WeaponRecord weapon, int key) =>
        key < 0 ? BitConverter.Int32BitsToSingle(weapon.AnimationPeriod)
        : key < WeaponRecord.KeyframeCount ? weapon.KeyTimes[key] : BitConverter.Int32BitsToSingle(weapon.RedKeys[0]);

    private void EmitStream(Unit unit)
    {
        var state = unit.Animation;
        var child = Definitions.Find((KnownObject)Math.Abs(unit.Definition.Children[(int)state]));

        if (child is null || !child.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke) || Time - Time % unit.StreamPeriod <= Time - FrameTime)
            return;

        var velocity = UnitKinematics.Displace(Vector3.Zero, unit.Direction, unit.Speed, 1);
        float across = velocity.X;
        float down = velocity.Y + unit.VerticalSpeed - child.ZSpeed;
        float acrossPeriod = across.IsZero() ? MaxStreamPeriod : child.Object.Size.X / Math.Abs(across);
        float downPeriod = down.IsZero() ? MaxStreamPeriod : child.Object.Size.Y / Math.Abs(down);
        int period = (int)Math.Min(acrossPeriod, downPeriod);

        if (state == SpriteAnimation.Fight && unit.Definition.AttacksTwice)
        {
            period >>= 1;
        }

        unit.StreamPeriod = Math.Clamp(period, 1, MaxStreamPeriod);
        EmitChildrenAs(unit, state);
    }

    /// <summary>
    /// Creates the child objects a sprite's definition lists for a state.
    /// </summary>
    /// <param name="unit">The sprite.</param>
    /// <param name="state">The state whose children to create.</param>
    [EngineRoutine(0x00446430)]
    public void EmitChildrenAs(Unit unit, SpriteAnimation state) =>
        Effects.Emit(unit.Definition, state, unit.Position, unit.Direction, Time, unit.Depth, unit.TakeChildPulse(state), unit);

    [EngineRoutine(0x00444dd0)]
    private void EmitStateChildren(Unit unit)
    {
        var state = unit.Animation;
        int child = unit.Definition.Children[(int)state];

        if (child == 0 || Definitions.Find((KnownObject)Math.Abs(child))?.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke) == true)
            return;

        var flags = unit.Definition.Object.Properties;
        int emissionFrame = flags.HasFlag(ObjectProperties.CreateChildEnd) ? unit.LastFrame : unit.FirstFrame;
        bool emits = state == SpriteAnimation.Fight && unit.Definition.AttacksTwice
            ? unit.Frame == (unit.LastFrame + 1 + unit.FirstFrame) / 2 || unit.Frame == emissionFrame
            : flags.HasFlag(ObjectProperties.Track) || unit.Frame == emissionFrame;

        if (!emits)
            return;

        if (state != SpriteAnimation.Fight)
        {
            EmitChildrenAs(unit, state);

            return;
        }

        Weapons.FireFrom(unit);

        if (unit.Command == UnitCommand.OneAttack && (!flags.HasFlag(ObjectProperties.Track) || unit.Frame == unit.LastFrame))
        {
            UnitKinematics.SetCommand(unit, UnitCommand.None);
        }
    }

    /// <summary>
    /// Returns the next unit number.
    /// </summary>
    /// <returns>The number.</returns>
    public int AllocateNumber() => _unitCount++;

    /// <summary>
    /// Deals damage to a unit.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="damage">The damage, negative to heal.</param>
    [EngineRoutine(0x00440830)]
    [EngineRoutine(0x004454d0)]
    public void Damage(Unit unit, int damage)
    {
        if (damage < 0 && unit != Player && unit.Animation < SpriteAnimation.Death)
        {
            unit.Hp = Math.Min(unit.Definition.MaxHp, unit.Hp - damage);

            return;
        }

        if (damage <= 0 || !unit.IsAlive)
        {
            return;
        }

        if (unit.Definition.Object.SpriteClass == SpriteClass.Man)
        {
            damage = DamageMan(unit, damage);

            if (damage <= 0)
                return;
        }

        if (unit.Animation >= SpriteAnimation.Death || unit.Definition.MaxHp == 0)
            return;

        int previousHp = unit.Hp;
        unit.Hp -= damage;

        if (!unit.IsAlive)
        {
            unit.HpBeforeDeath = previousHp;
            unit.Definition.Killed[(int)unit.Army & 3]++;
        }

        if (unit.IsAlive && previousHp > unit.Definition.MaxHp / 2 && unit.Hp <= unit.Definition.MaxHp / 2)
        {
            unit.ChangeAnimation(SpriteAnimation.Wound);
        }
        bool staggers = unit is { IsAlive: true, Animation: SpriteAnimation.Stand or SpriteAnimation.Go } && unit.Definition.Layout.GetFrameCount(SpriteAnimation.Close) > 0;

        if (staggers)
        {
            unit.ChangeAnimation(SpriteAnimation.Close);
        }
        else
        {
            EmitChildrenAs(unit, SpriteAnimation.Close);
        }

        if (!unit.IsAlive)
        {
            CompleteDeath(unit, damage > unit.Definition.MaxHp ? SpriteAnimation.Death2 : SpriteAnimation.Death);
        }

        _brain.Wander(unit);
    }

    private void AddToMonsters(Unit unit)
    {
        _monsters.Add(unit);
        _monsterSet.Add(unit);
    }

    private void RemoveMonster(Unit unit)
    {
        _builders.Remove(unit);
        _monsters.Remove(unit);
        _monsterSet.Remove(unit);
    }

    [EngineRoutine(0x00454800)]
    private int DamageMan(Unit man, int damage)
    {
        if (man.Definition.Id != KnownObject.MannedCannon)
        {
            for (var link = man.Link; link is not null; link = link.Link)
            {
                if (link.Definition.Id is KnownObject.InvulnerabilityEffect or KnownObject.RescueMedkitEffect)
                    return 0;
            }

            for (var link = man.Link; link is not null; link = link.Link)
            {
                int armor = link.Definition.Id - KnownObject.WornGreenArmor;

                if (armor is < 0 or >= WornArmorShareCount)
                    continue;

                int share = WornArmorShares[armor];
                Damage(link, (share * damage + ArmorRounding) / Percent);
                damage -= share * damage / Percent;
            }
        }

        if (man.Hp > damage || !man.Items.Contains((int)KnownObject.Medkit))
            return damage;

        man.Items.Remove((int)KnownObject.Medkit);
        SetUnitHp(man, man.Definition.ArmyMaxHp[(int)man.Army & ArmyMask]);
        CreateSprite(Definitions.Find(KnownObject.RescueMedkitEffect)!, man.Position + Vector3.UnitZ * RescueEffectHeight, default);

        return 0;
    }

    private void CompleteDeath(Unit unit, SpriteAnimation animation)
    {
        if (unit.Placement is not null && unit != Player)
        {
            RemovePlacement(unit.Placement);
            AddToMonsters(unit);
            unit.Role = UpdateRole.Monster;
        }

        unit.DiedAt = Time;
        unit.ChangeAnimation(animation == SpriteAnimation.Death2 && unit.Definition.Children[(int)SpriteAnimation.Death2] != 0
            ? SpriteAnimation.Death2 : SpriteAnimation.Death);
    }

    private void RemovePlacement(MergedMapSprite placement)
    {
        _removed.Add(placement);


        if (_statics.Remove(placement, out var stand))
        {
            _builders.Remove(stand);
            _updates.Remove(stand.Definition.Layer, stand);
            Units.Remove(stand);
            Effects.Detach(stand);
            ReleaseSprite(stand);
        }

        ReleasePlacement(placement);

        Movement.Remove(placement);
    }

    /// <summary>
    /// Creates a unit the world updates as a monster.
    /// </summary>
    /// <param name="definition">The unit's definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="army">The army.</param>
    /// <returns>The unit.</returns>
    public Unit AddMonster(SpriteDefinition definition, Vector3 position, Angle direction = default, Army army = Army.Monsters)
    {
        var unit = ConstructSprite(definition, position, direction, army, definition.MaxHp, UpdateRole.Monster, _unitCount++);
        AddToMonsters(unit);

        if (!_loading)
        {
            RunCreationHook(unit);
        }

        return unit;
    }

    private void CreateLink(Unit unit) => Effects.CreateLink(unit, Time, 0);

    [EngineRoutine(0x00444970)]
    private static void InsertLink(Unit owner, Unit link)
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
    /// Creates a unit's linked object as a scripted sprite.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="definition">The linked object's definition.</param>
    public void AttachObjectLink(Unit unit, SpriteDefinition definition)
    {
        if (definition.Id.IsScripted)
        {
            var scripted = AddScripted(definition, GetLinkPosition(unit, definition), unit.Direction, unit.Army);
            InsertLink(unit, scripted);

            return;
        }

        var link = new Unit(_unitCount++, definition, GetLinkPosition(unit, definition), unit.Army, definition.MaxHp) { CreatedAt = Time };
        link.Start(definition.InitialAnimation, unit.Direction, Time, _rng);
        InsertLink(unit, link);

        if (definition.Object.SpriteClass == SpriteClass.Creature)
        {
            link.StartMoving(link.MaxSpeed);
        }

        if (unit.ArmedLink is not null && link.Definition.FightChild != 0)
        {
            unit.Behavior = definition.Behavior;
        }

        AddToMonsters(link);
        Register(UpdateRole.Monster, link);
        CreateLink(link);
    }

    private static Vector3 GetLinkPosition(Unit owner, SpriteDefinition definition) =>
        owner.Position + owner.Definition.Object.GetLinkOffset(definition.Object, null, 0, owner.Direction);

    [EngineRoutine(0x0042da30)]
    private bool IsFree(SpriteDefinition definition, Vector3 position) => Movement.IsAboveGround(definition.Object, position) && !Projectiles.IsOccupied(definition, position);


}
