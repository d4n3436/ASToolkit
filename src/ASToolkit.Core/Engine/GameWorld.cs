using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Scripting;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Engine;

internal sealed partial class GameWorld
{
    private const int PlayerArmy = 0;
    public const int MonsterArmy = 1;
    private const float PickupReach = 60;
    private const float HealingEffectHeight = 60;
    private const float AmmoReach = 50;

    private const int MaxStreamPeriod = 30000;
    private const int RespawnProtection = 3000;
    private const float RescueEffectHeight = 22;
    private const float ProtectionEffectHeight = 20;
    private const int DroppedItemLifetime = 17000;
    private const int DroppedItemLifetimeVariation = 5000;
    private const float DroppedItemHeight = 10;
    private const float BatItemHeight = -30;

    private readonly GameRandom _rng;
    private readonly MonsterBrain _brain;
    private readonly List<Unit> _monsters = [];
    private readonly HashSet<Unit> _monsterSet = [];
    private bool _adrenalineActive;
    private int _quakeStart;
    private int _quakeDelay;
    private uint _environment;
    private bool _deathFlashed;
    private readonly List<ObjectProgram> _programs = [];
    private readonly UpdateOrder _updates;
    private readonly List<WorldPickup> _pickups = [];
    private GameClock _clock;
    private int _protectedUntil;
    private int _mountedAt = -1;
    private bool _movesPlayer;
    private Vector3 _respawnPosition;
    private int _unitCount;

    public GameWorld(LevelTemplate data, PlayerLoadout loadout, GameRandom rng)
    {
        _updates = new UpdateOrder();
        var model = data.Model;
        var level = data.Script;
        var kinds = data.Kinds;
        var frameTimes = data.FrameTimes;
        _clock = new GameClock(frameTimes);
        bool doorsLocked = level.DoorsLocked;
        bool fencesUp = model.Sprites.Any(sprite => sprite.Id == KnownObject.Fence);
        _sprites = model.Sprites.ToList();
        _level = level;
        _triggersPassed = new bool[level.Triggers.Count];
        _crowdSprites = level.CrowdRules.Select(rule => new MergedMapSprite?[rule.Points.Count]).ToArray();
        Kinds = kinds;
        _rng = rng;
        Movement = data.Movement.Clone();
        DoorsLocked = doorsLocked;
        FencesUp = fencesUp;
        var legs = kinds.Find(KnownObject.PlayerStart)!;
        Units = new UnitGrid(Movement.MapSize, kinds.LargestHashedSize);

        Motion = new UnitKinematics(kinds, this);
        Effects = new SpawnedObjectSystem(kinds, Movement, rng, this);
        int maxHealth = loadout.Implants.ScaleHealth(legs.MaxHp);
        var start = model.Sprites.FirstOrDefault(sprite => sprite.Id is KnownObject.PlayerStart or KnownObject.PlayerStartFemaleLegs);
        Player = new Unit(_unitCount++, legs, start?.Position ?? new Vector3(data.FallbackStart, 0), PlayerArmy, maxHealth)
        { CreatedAt = 0 };
        Player.Start(legs.InitialAnimation, new Facing(start?.Facing ?? 0), 0, rng);
        _respawnPosition = Player.Position;
        Inventory = new PlayerInventory(kinds, data.AmmoBatches, Player, loadout);
        Weapons = new WeaponSystem(this, Inventory, kinds, rng);
        var unitMovement = new UnitCollision(Movement, Units);
        PlayerMovement = new PlayerMovement(unitMovement);
        _brain = new MonsterBrain(Motion, unitMovement, rng, _monsters);
        var placedUnits = new Dictionary<uint, Unit>();

        if (start is null)
        {
            RegisterPlayer();
        }

        foreach (var sprite in model.Sprites)
        {
            if (sprite == start)
            {
                RegisterPlayer();
            }

            var kind = Kinds.Find(sprite.Id);

            if (kind is null)
                continue;

            if (kind.MaxHp > 0)
            {
                if (sprite.Id != KnownObject.Crate && kind.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia)
                {
                    placedUnits[sprite.Handle] = AddMonster(kind, sprite.Position, new Facing(sprite.Facing), unchecked((int)sprite.Army), KnownObject.GetCrateItem(sprite.Payload));
                }
                else if (kind.Object.SpriteClass != SpriteClass.Player)
                {
                    var body = new Unit(_unitCount++, kind, sprite.Position, unchecked((int)sprite.Army), kind.MaxHp)
                    {
                        Facing = new Facing(sprite.Facing), Placement = sprite,
                        DropItem = KnownObject.GetCrateItem(sprite.Payload)
                    };
                    
                    Units.Add(body);
                    _bodies.Add(sprite, body);
                    placedUnits[sprite.Handle] = body;
                    AttachLink(body);
                }

                continue;
            }

            if (kind.Object.SpriteClass == SpriteClass.Cannon)
            {
                AddPlacedCannon(sprite);
                continue;
            }

            if (sprite.Id.IsScripted)
            {
                AddScripted(kind, sprite.Position, new Facing(sprite.Facing), unchecked((int)sprite.Army), sprite);
            }
            else
            {
                AddStatic(sprite, kind);
            }

            if (sprite.Id.PickupCategory is not null)
            {
                AddPickup(sprite.Position, sprite.Id, sprite);
            }

            if (!SpriteProgram.TryParse(sprite.Payload, false, out var program))
                continue;

            var objectProgram = new ObjectProgram(kind, sprite.Position, new Facing(sprite.Facing), program.GetStack(0).ToArray(), _rng, this, sprite);
            _programs.Add(objectProgram);
            _updates.Add(kind.Layer, new UpdateEntry(UpdateRole.Program, null, objectProgram));
        }

        foreach (var sprite in model.Sprites)
        {
            if (placedUnits.TryGetValue(sprite.Handle, out var unit) && unit.Placement is null && SpriteProgram.TryParse(sprite.Payload, false, out var program))
            {
                unit.Orders = GetOrders(program.GetStack(0), placedUnits);
            }
        }

        if (Inventory.Items.GetValueOrDefault(KnownObject.Drone) > 0)
        {
            AddDrone();
        }
    }

    // Takes a placed unit's stack from its last entry, with its attack targets named by map handles
    private static UnitOrder[] GetOrders(ReadOnlySpan<StackAction> stack, Dictionary<uint, Unit> units)
    {
        var orders = new List<UnitOrder>(stack.Length);
        for (int index = stack.Length - 1; index >= 0; index--)
        {
            var action = stack[index];

            switch (action.Code)
            {
                case ActionCode.Move:
                    orders.Add(new UnitOrder(action.Code, new Vector2(action.Var1, action.Var2), null));
                    break;
                case ActionCode.Attack:
                    orders.Add(new UnitOrder(action.Code, null, units.GetValueOrDefault(unchecked((uint)action.Var1))));
                    break;
                default:
                    orders.Add(UnitOrder.FromAction(action));
                    break;
            }
        }

        return orders.ToArray();
    }

    private void AddPlacedCannon(MergedMapSprite sprite)
    {
        Effects.Spawn(sprite.Id, sprite.Position, new Facing(sprite.Facing), Time);
    }

    public PlayerInventory Inventory { get; }

    public WeaponSystem Weapons { get; }

    public Unit Player { get; }

    public Vector2 Cursor { get; private set; }

    public ObjectCatalog Kinds { get; }

    public MovementMap Movement { get; }

    public UnitGrid Units { get; }

    public PlayerMovement PlayerMovement { get; }

    public ProjectileCollision Projectiles => Weapons.Collision;

    public SpawnedObjectSystem Effects { get; }

    public UnitKinematics Motion { get; }

    public int Time => _clock.Time;

    public int FrameTime => _clock.FrameTime;

    public bool DoorsLocked { get; private set; }

    public bool FencesUp { get; private set; }

    public IReadOnlyList<Unit> Monsters => _monsters;

    public IReadOnlyList<ObjectProgram> Programs => _programs;

    public ReadOnlySpan<WorldPickup> Pickups => CollectionsMarshal.AsSpan(_pickups);

    public void ApplyPlayerCommand(in PlayerCommand command)
    {
        if (Player.IsAlive)
        {
            if (command.Fires)
            {
                Weapons.AimPlayer(command.Cursor, command.Weapon);
            }

            Weapons.TrackCursor(command.Cursor, FrameTime, command.Destination is not null);
        }

        _movesPlayer = command.Destination is not null;
        Cursor = command.Cursor;
        PlayerMovement.Tick(Player, IsInCannon ? null : command.Destination, IsInCannon ? 0 : Inventory.Speed * ObjectDefinition.PerMillisecond, FrameTime);
    }

    [EngineRoutine(0x00405190)]
    public void Update()
    {
        for (int layer = 0; layer < UpdateOrder.LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int index = list.Count - 1; index >= 0; index--)
            {
                var entry = list[index];

                if (entry.Role != UpdateRole.None && !TickEntry(entry))
                {
                    _updates.Clear(list, index);
                }
            }
        }

        RunLevelScript();
        ShakeCamera();

        if (!Player.IsAlive && IsInCannon)
        {
            EjectFromCannon();
        }

        if (!Player.IsAlive)
        {
            TryRespawn();
        }

        _updates.Compact();
        Effects.Compact();
    }

    private bool TickEntry(UpdateEntry entry)
    {
        var unit = entry.Unit;
        switch (entry.Role)
        {
            case UpdateRole.Player:
                if (Player.IsAlive)
                {
                    TickUnit(Player);
                }

                return true;
            case UpdateRole.Torso:
                if (unit != Weapons.Torso)
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
                return Effects.Tick(unit!, FrameTime);
            default:
                entry.Program!.Tick(Player.Position.AsVector2(), Time);

                return true;
        }
    }

    public void Register(UpdateRole role, Unit unit) => _updates.Add(unit.Kind.Layer, new UpdateEntry(role, unit, null));

    // Registers the torso before the legs
    private void RegisterPlayer()
    {
        Register(UpdateRole.Torso, Weapons.Torso);
        Register(UpdateRole.Player, Player);
        Units.Add(Player);
    }

    public void Step(in PlayerCommand command)
    {
        TakePickups();
        ApplyPlayerCommand(command);
        Update();
        AdvanceClock();
    }

    public void AdvanceClock() => _clock.Advance();

    public void AddPickup(Vector3 position, KnownObject id, MergedMapSprite? placement)
    {
        var kind = Kinds.Find(id);

        if (kind is not null)
        {
            _pickups.Add(new WorldPickup(kind, position, placement));
        }
    }

    public void StartBlackout()
    {
        DoorsLocked = true;
    }

    private void OpenDoors()
    {
        DoorsLocked = false;
    }

    private void LowerFences()
    {
        FencesUp = false;
    }

    [EngineRoutine(0x00440830)]
    [EngineRoutine(0x0044ca70)]
    public bool BuildFromProgram(ObjectProgram program, StackAction action, bool testPlacement = true)
    {
        var kind = Kinds.Find((KnownObject)action.Var1);

        if (kind is null)
        {
            return true;
        }

        float x = action is { Var2: 0, Var3: 0 } ? program.Position.X : action.Var2;
        float y = action is { Var2: 0, Var3: 0 } ? program.Position.Y : action.Var3;

        if (action.Var2 < 0)
        {
            x = program.Position.X - action.Var2 - _rng.Next(1 - action.Var2) * 2;
        }

        if (action.Var3 < 0)
        {
            y = program.Position.Y - action.Var3 - _rng.Next(1 - action.Var3) * 2;
        }
        var position = new Vector3(x, y, program.Position.Z);

        if (testPlacement && !IsFree(kind, position))
        {
            return false;
        }

        if (kind.MaxHp > 0)
        {
            var unit = AddMonster(kind, position, program.Facing);
            unit.Orders = program.Orders;
        }
        else if (kind.Id.IsEndlessGenerator)
        {
            AddScripted(kind, position, program.Facing);
        }
        else
        {
            Effects.Spawn(kind.Id, position, program.Facing, Time);
        }


        return true;
    }

    public void AnimateFromProgram(ObjectProgram program, SpriteAnimation animation)
    {
        if (animation >= SpriteAnimation.Death && program.Placement is not null)
        {
            RemovePlacement(program.Placement);
        }

        Effects.Play(program.Kind, animation, program.Position, program.Facing, Time);
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

                return false;
            }

            unit.Position = GetLinkPosition(owner, unit.Kind);
        }

        if (unit.IsAlive)
        {
            _brain.Move(unit, FrameTime, _clock.FramesPerSecond);
        }

        if (TickUnit(unit))
            return true;

        RemoveMonster(unit);
        Units.Remove(unit);
        Delete(unit);

        return false;
    }

    private void Delete(Unit removed)
    {
        foreach (var unit in _monsters)
        {
            UnitKinematics.ForgetObject(unit, removed);
        }

        UnitKinematics.ForgetObject(Weapons.Torso, removed);
        Effects.Forget(removed);

        if (removed.Link?.Owner == removed)
        {
            Effects.Detach(removed);
        }
    }

    [EngineRoutine(0x00440110)]
    public bool TickUnit(Unit unit)
    {
        var layout = unit.Kind.Layout;
        UpdateKeyframe(unit);
        EmitStream(unit);
        var state = unit.Animation;

        if (unit.CreatedAt == Time || Time - Time % layout.GetFrameDuration(state) <= Time - FrameTime)
            return true;

        if (unit.Command is not UnitCommand.None && unit.Command < UnitCommand.Build && !unit.HasGoal)
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

        bool everyFrame = unit.Kind.Object.Flags.HasFlag(ObjectFlags.ActsEveryFrame);

        if (unit.Frame == unit.FirstFrame || everyFrame)
        {
            var hooked = unit.Animation;
            RunStateHook(unit);
            while (unit.Animation != hooked)
            {
                hooked = unit.Animation;
                RunStateHook(unit);
            }
        }

        EmitStateChildren(unit);
        unit.Frame++;

        if (unit.Animation >= SpriteAnimation.Death && (unit.Frame > unit.LastFrame || layout.GetFrameCount(unit.Animation) == 0))
        {
            unit.Frame = unit.LastFrame;

            if (unit.Kind.Damage != 0)
            {
                Weapons.ApplyBlast(unit);
            }

            return unit == Player;
        }

        if ((unit.Frame > unit.LastFrame || everyFrame) && unit.Animation < SpriteAnimation.Death)
        {
            if (unit == Player)
            {
                Weapons.DecidePlayer(FrameTime);
                unit.SetState(!unit.Speed.IsZero() ? SpriteAnimation.Go : SpriteAnimation.Stand);
            }
            else if (unit == Weapons.Torso)
            {
                SpawnedObjectSystem.DecideObject(unit);
            }
            else if (unit.Command is UnitCommand.Passive or UnitCommand.Pause)
            {
                HoldStill(unit);
            }
            else if (unit.Number >= 0)
            {
                _brain.Decide(unit, Player, FrameTime, Time);
            }
            else if (unit.Kind.Object.SpriteClass == SpriteClass.Cannon)
            {
                SpawnedObjectSystem.DecideCannon(unit);
            }
            else
            {
                SpawnedObjectSystem.DecideObject(unit);
            }
        }

        if (unit.Lifetime != ObjectDefinition.Unlimited && unit.Animation < SpriteAnimation.Death)
        {
            if (Time - unit.LastTickTime < unit.Lifetime)
            {
                unit.Lifetime -= Time - unit.LastTickTime;
            }
            else
            {
                unit.SetState(SpriteAnimation.Death);
            }
        }

        int period = unit.Kind.Weapon.AnimationPeriod;
        if (unit.Kind.HasKeyframes && period != ObjectDefinition.Unlimited && (uint)period < (uint)(Time - unit.KeyframeStart))
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
                unit.SetState(SpriteAnimation.Stand);
            }
        }
        else if (state != SpriteAnimation.Go)
        {
            unit.SetState(SpriteAnimation.Go);
        }
    }

    [EngineRoutine(0x00440110)]
    private void UpdateKeyframe(Unit unit)
    {
        if (!unit.Kind.HasKeyframes)
            return;

        ref readonly var weapon = ref unit.Kind.Weapon;
        int elapsed = Time - unit.KeyframeStart;
        int period = weapon.AnimationPeriod;
        if (period == ObjectDefinition.Unlimited)
        {
            if (unit.Animation > SpriteAnimation.Birth && unit.LastFrame != unit.FirstFrame)
            {
                period = unit.Kind.Layout.GetFrameDuration(unit.Animation) * (unit.LastFrame - unit.FirstFrame);
            }
            else if (unit.Lifetime == ObjectDefinition.Unlimited)
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

    // Lays each stream child one body length after the last at the emitter's speed relative to the child
    private void EmitStream(Unit unit)
    {
        var state = unit.Animation;
        var child = Kinds.Find((KnownObject)Math.Abs(unit.Kind.Object.Children[(int)state]));

        if (child is null || !child.Object.Flags.HasFlag(ObjectFlags.Stream) || Time - Time % unit.StreamPeriod <= Time - FrameTime)
            return;

        var velocity = UnitKinematics.Displace(Vector3.Zero, unit.Facing, unit.Speed, 1);
        float across = velocity.X;
        float down = velocity.Y + unit.VerticalSpeed - child.ZSpeed;
        float acrossPeriod = across.IsZero() ? MaxStreamPeriod : child.Object.Size.X / Math.Abs(across);
        float downPeriod = down.IsZero() ? MaxStreamPeriod : child.Object.Size.Y / Math.Abs(down);
        int period = (int)Math.Min(acrossPeriod, downPeriod);

        if (state == SpriteAnimation.Fight && unit.Kind.AttacksTwice)
        {
            period >>= 1;
        }

        unit.StreamPeriod = Math.Clamp(period, 1, MaxStreamPeriod);
        EmitChildrenAs(unit, state);
    }

    [EngineRoutine(0x00446430)]
    public void EmitChildrenAs(Unit unit, SpriteAnimation state) =>
        Effects.Emit(unit.Kind, state, unit.Position, unit.Facing, Time, unit.Depth, unit.TakeChildPulse(state), unit);

    [EngineRoutine(0x00444dd0)]
    private void EmitStateChildren(Unit unit)
    {
        var state = unit.Animation;
        int child = unit.Kind.Object.Children[(int)state];

        if (child == 0 || Kinds.Find((KnownObject)Math.Abs(child))?.Object.Flags.HasFlag(ObjectFlags.Stream) == true)
            return;

        var flags = unit.Kind.Object.Flags;
        int emissionFrame = flags.HasFlag(ObjectFlags.LastFrameEmission) ? unit.LastFrame : unit.FirstFrame;
        bool emits = state == SpriteAnimation.Fight && unit.Kind.AttacksTwice
            ? unit.Frame == (unit.LastFrame + 1 + unit.FirstFrame) / 2 || unit.Frame == emissionFrame
            : flags.HasFlag(ObjectFlags.ActsEveryFrame) || unit.Frame == emissionFrame;

        if (!emits)
            return;

        if (state != SpriteAnimation.Fight)
        {
            EmitChildrenAs(unit, state);

            return;
        }

        Weapons.FireFrom(unit);

        if (unit.Command == UnitCommand.OneAttack && (!flags.HasFlag(ObjectFlags.ActsEveryFrame) || unit.Frame == unit.LastFrame))
        {
            UnitKinematics.SetCommand(unit, UnitCommand.None);
        }
    }

    public int AllocateNumber() => _unitCount++;

    private int GetNextAnimationTick(int frameTime) => Time - Time % frameTime + frameTime;

    [EngineRoutine(0x00440830)]
    [EngineRoutine(0x004454d0)]
    public void Damage(Unit unit, int damage)
    {
        if (damage < 0 && unit != Player && unit.Animation < SpriteAnimation.Death)
        {
            unit.Hp = Math.Min(unit.Kind.MaxHp, unit.Hp - damage);

            return;
        }

        if (damage <= 0 || !unit.IsAlive)
        {
            return;
        }

        if (unit == Player)
        {
            if (Time < _protectedUntil)
                return;

            damage = Inventory.AbsorbDamage(damage);

            if (damage >= unit.Hp && Inventory.TryRescue())
            {
                var kind = Kinds.Find(KnownObject.RescueMedkitEffect)!;
                int duration = kind.GetVisualFrameDuration(SpriteAnimation.Death);
                _protectedUntil = GetNextAnimationTick(duration) + (kind.GetVisualFrameCount(SpriteAnimation.Death) - 1) * duration;
                Effects.Spawn(kind.Id, unit.Position + Vector3.UnitZ * RescueEffectHeight, unit.Facing, Time, unit);

                return;
            }

            if (damage <= 0)
                return;

            int previous = unit.Hp;
            unit.Hp -= damage;

            if (unit.IsAlive && previous > unit.Kind.MaxHp / 2 && unit.Hp <= unit.Kind.MaxHp / 2)
            {
                unit.SetState(SpriteAnimation.Wound);
            }

            EmitChildrenAs(unit, SpriteAnimation.Close);

            return;
        }

        if (unit.Animation >= SpriteAnimation.Death || unit.Kind.MaxHp == 0)
            return;

        int previousHp = unit.Hp;
        unit.Hp -= damage;

        if (!unit.IsAlive)
        {
            unit.HpBeforeDeath = previousHp;
        }

        if (unit.IsAlive && previousHp > unit.Kind.MaxHp / 2 && unit.Hp <= unit.Kind.MaxHp / 2)
        {
            unit.SetState(SpriteAnimation.Wound);
        }
        bool staggers = unit is { IsAlive: true, Animation: SpriteAnimation.Stand or SpriteAnimation.Go } && unit.Kind.Layout.GetFrameCount(SpriteAnimation.Close) > 0;

        if (staggers)
        {
            unit.SetState(SpriteAnimation.Close);
        }
        else
        {
            EmitChildrenAs(unit, SpriteAnimation.Close);
        }

        if (!unit.IsAlive)
        {
            CompleteDeath(unit, damage > unit.Kind.MaxHp ? SpriteAnimation.Death2 : SpriteAnimation.Death);
        }

        _brain.Wander(unit);
    }

    public void SetRespawnPoint() => _respawnPosition = Player.Position;

    private void TryRespawn()
    {
        if (!Inventory.TryRespawn())
            return;

        var position = _respawnPosition.AsVector2();
        for (var unit = Units.First(position - Player.Kind.Half, position + Player.Kind.Half); unit is not null; unit = Units.Next())
        {
            if (unit == Player || !unit.IsAlive || !Player.Kind.GetCollisionVolume(position.AsVector3()).OverlapsFlat(unit.CollisionVolume))
                continue;

            unit.Hp = 0;
            Units.Remove(unit);
        }

        Player.Position = _respawnPosition;
        Player.Speed = 0;
        Player.ClearMove();
        Player.SetState(SpriteAnimation.Stand);
        PlayerMovement.Reset();
        Weapons.ResetPlayer();
        Units.Move(Player);
        _protectedUntil = Time + RespawnProtection;

        if (Inventory.Items.GetValueOrDefault(KnownObject.Drone) > 0)
        {
            AddDrone();
        }

        Effects.Spawn(KnownObject.InvulnerabilityEffect, Player.Position + Vector3.UnitZ * ProtectionEffectHeight,
            Player.Facing, Time, Player, RespawnProtection);
    }

    private void AddToMonsters(Unit unit)
    {
        _monsters.Add(unit);
        _monsterSet.Add(unit);
    }

    private void RemoveMonster(Unit unit)
    {
        _monsters.Remove(unit);
        _monsterSet.Remove(unit);
    }

    private void CompleteDeath(Unit unit, SpriteAnimation animation)
    {
        if (unit.Placement is not null)
        {
            RemovePlacement(unit.Placement);
            AddToMonsters(unit);
            Register(UpdateRole.Monster, unit);
        }

        unit.DiedAt = Time;
        unit.SetState(animation == SpriteAnimation.Death2 && unit.Kind.Object.Children[(int)SpriteAnimation.Death2] != 0
            ? SpriteAnimation.Death2 : SpriteAnimation.Death);
    }

    [ScriptFunction("CreateItems")]
    private void DropItem(Unit unit)
    {
        if (unit.DropItem != 0 && Kinds.Loot.DropsItem(unit.Kind.Id))
        {
            var kind = Kinds.Find(unit.DropItem);

            if (kind is not null)
            {
                _rng.Next(1);
                var facing = new Facing((byte)_rng.Next(Facing.FullTurn));
                float height = unit.Position.Z + (unit.Kind.Id == KnownObject.Bat ? BatItemHeight : DroppedItemHeight);
                var body = new Unit(-1, kind, unit.Position with { Z = height }, 0, 0);
                body.Start(kind.InitialAnimation, facing, Time, _rng);
                AttachLink(body);
                int expires = Time + DroppedItemLifetime + _rng.Next(DroppedItemLifetimeVariation + 1);
                _pickups.Add(new WorldPickup(kind, body.Position, null, Time, expires, facing, body));
            }
        }

        unit.DropItem = 0;
    }

    private void RemovePlacement(MergedMapSprite placement)
    {
        _removed.Add(placement);

        if (_statics.Remove(placement, out var stand))
        {
            Units.Remove(stand);
            Effects.Detach(stand);
        }

        Movement.Remove(placement);
    }

    public static float GetPickupReach(KnownObject id) => id.IsAmmo ? AmmoReach : PickupReach;

    public void TakePickups()
    {
        for (int index = _pickups.Count - 1; index >= 0; index--)
        {
            var pickup = _pickups[index];

            if (Time >= pickup.ExpiresAt)
            {
                _pickups.RemoveAt(index);
                ReleasePickup(pickup);
                continue;
            }

            int interval = pickup.Kind.GetVisualFrameDuration(SpriteAnimation.Stand);

            if (Time - Time % interval <= Time - FrameTime)
                continue;

            if (pickup.Position.AsVector2().MeasureDistance(Player.Position.AsVector2()) < GetPickupReach(pickup.Id) && TryTakePickup(pickup))
            {
                _pickups.RemoveAt(index);
                ReleasePickup(pickup);

                if (pickup.Id == KnownObject.Invulnerability)
                {
                    int lifetime = Kinds.Find(KnownObject.InvulnerabilityEffect)!.Object.Lifetime;
                    _protectedUntil = Time + lifetime;
                    Effects.Spawn(KnownObject.InvulnerabilityEffect, Player.Position + Vector3.UnitZ * ProtectionEffectHeight,
                        Player.Facing, Time, Player);
                }

                if (pickup.Id is KnownObject.Health or KnownObject.BigHealth or KnownObject.MegaHealth)
                {
                    Effects.Spawn(KnownObject.HealingEffect, Player.Position + Vector3.UnitZ * HealingEffectHeight, Player.Facing, Time);
                }
            }
        }
    }

    private void ReleasePickup(WorldPickup pickup)
    {
        var owner = pickup.Body ?? (pickup.Placement is null ? null : _statics.GetValueOrDefault(pickup.Placement));

        if (owner is not null)
        {
            Effects.Detach(owner);
        }
    }

    private bool TryTakePickup(WorldPickup pickup)
    {
        var center = pickup.Position.AsVector2();

        switch (pickup.Id)
        {
            case KnownObject.FreezeMonsters:
                FreezeMonsters(center);
                return true;
            case KnownObject.Bomb:
                DetonateBomb(center);
                return true;
            case KnownObject.Adrenaline:
                TakeAdrenaline();
                return true;
            case KnownObject.Drone:
                return TakeDrone();
            default:
                return Inventory.TryTakePickup(pickup.Id);
        }
    }

    public Unit AddMonster(ObjectDefinition kind, Vector3 position, Facing facing = default, int army = MonsterArmy, KnownObject dropItem = 0)
    {
        var unit = new Unit(_unitCount++, kind, position, army, kind.MaxHp)
        { DropItem = dropItem, CreatedAt = Time };
        var state = kind.InitialAnimation;
        unit.Start(state, facing, Time, _rng);

        if (state != SpriteAnimation.Birth)
        {
            Effects.Emit(kind, SpriteAnimation.Birth, unit.Position, unit.Facing, Time);
        }

        RunCreationHook(unit);

        AddToMonsters(unit);
        Register(UpdateRole.Monster, unit);
        Units.Add(unit);
        AttachLink(unit);

        return unit;
    }

    private void AttachLink(Unit unit) => Effects.AttachLink(unit, Time, 0);

    public void AttachObjectLink(Unit unit, ObjectDefinition kind)
    {
        if (kind.Id.IsScripted)
        {
            var scripted = AddScripted(kind, GetLinkPosition(unit, kind), unit.Facing, unit.Army);
            scripted.Owner = unit;
            unit.Link = scripted;

            return;
        }

        var link = new Unit(_unitCount++, kind, GetLinkPosition(unit, kind), unit.Army, kind.MaxHp) { Owner = unit, CreatedAt = Time };
        link.Start(kind.InitialAnimation, unit.Facing, Time, _rng);
        unit.Link = link;

        if (kind.Object.SpriteClass == SpriteClass.Creature)
        {
            link.StartMoving(link.MaxSpeed);
        }

        if (unit.ArmedLink is not null && link.Kind.FightChild != 0)
        {
            unit.Behavior = kind.Behavior;
        }

        AddToMonsters(link);
        Register(UpdateRole.Monster, link);
        AttachLink(link);
    }

    private static Vector3 GetLinkPosition(Unit owner, ObjectDefinition kind) =>
        owner.Position + owner.Kind.Object.GetLinkOffset(kind.Object, null, 0, owner.Facing.Value);

    [EngineRoutine(0x0042da30)]
    private bool IsFree(ObjectDefinition kind, Vector3 position) => Movement.IsAboveGround(kind.Object, position) && !Projectiles.IsOccupied(kind, position);


}
