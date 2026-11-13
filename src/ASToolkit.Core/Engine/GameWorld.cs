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

    private const int RespawnProtection = 3000;
    private const float RescueEffectHeight = 22;
    private const float ProtectionEffectHeight = 20;
    private const int DroppedItemLifetime = 17000;
    private const int DroppedItemLifetimeVariation = 5000;
    private const float DroppedItemHeight = 10;
    private const float BatItemHeight = -30;

    private readonly GameRandom _rng;
    private readonly IWorldEventSink? _events;
    private readonly MonsterBrain _brain;
    private readonly List<Unit> _monsters = [];
    private readonly List<Unit> _blastCandidates = [];
    private readonly List<ObjectProgram> _programs = [];
    private readonly UpdateOrder _updates;
    private readonly List<WorldPickup> _pickups = [];
    private readonly List<KnownObject> _taken = [];
    private GameClock _clock;
    private int _protectedUntil;
    private int _mountedAt = -1;
    private bool _movesPlayer;
    private Vector3 _respawnPosition;
    private int _unitCount;


    public GameWorld(LevelTemplate data, PlayerLoadout loadout, GameRandom rng, IWorldEventSink? events)
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
        _checkpointsPassed = new bool[level.Checkpoints.Count];
        Kinds = kinds;
        _rng = rng;
        _events = events;
        ScriptTimeline = new ScriptTimeline(doorsLocked, null, null, null);
        Movement = data.Movement.Clone();
        Movement.SetGates(doorsLocked, fencesUp);
        var legs = kinds.Find(KnownObject.PlayerStart)!;
        Units = new UnitGrid(data.Bounds.Minimum, data.Bounds.Maximum);

        Motion = new UnitKinematics(kinds, this);
        Effects = new SpawnedObjectSystem(kinds, Movement, rng, events, this);
        int maxHealth = loadout.Implants.ScaleHealth(legs.MaxHp);
        var start = model.Sprites.FirstOrDefault(sprite => sprite.Id is KnownObject.PlayerStart or KnownObject.PlayerStartFemaleLegs);
        Player = new Unit(_unitCount++, legs, start?.Position ?? new Vector3(data.FallbackStart, 0), PlayerArmy, maxHealth)
        { CreatedAt = 0 };
        Player.Start(legs.InitialAnimation, new Facing(start?.Facing ?? 0), 0, rng);
        Units.Add(Player);
        _respawnPosition = Player.Position;
        Inventory = new PlayerInventory(kinds, data.AmmoBatches, Player, loadout);
        Weapons = new WeaponSystem(this, Inventory, kinds, rng);
        var unitMovement = new UnitCollision(Movement, Units);
        PlayerMovement = new PlayerMovement(unitMovement);
        _brain = new MonsterBrain(Motion, unitMovement, rng);
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
                    orders.Add(new UnitOrder(new Vector2(action.Var1, action.Var2), null));
                    break;
                case ActionCode.Attack when units.TryGetValue(unchecked((uint)action.Var1), out var target):
                    orders.Add(new UnitOrder(null, target));
                    break;
            }
        }

        return orders.ToArray();
    }

    private void AddPlacedCannon(MergedMapSprite sprite)
    {
        Effects.Spawn(sprite.Id, sprite.Position, new Facing(sprite.Facing), Time);

        _events?.RecordSpriteRemoval(sprite, Time);
    }

    public PlayerInventory Inventory { get; }

    public WeaponSystem Weapons { get; }
    public void CopyVisualEffects(List<EffectState> states)
    {
        states.Clear();
        Effects.AddStates(states);
        
        foreach (var pickup in _pickups)
        {
            if (pickup.IsDropped)
            {
                states.Add(pickup.GetState());
            }
        }

        foreach (var unit in _scripted)
        {
            if (!unit.Kind.Id.IsDoor)
            {
                states.Add(SpawnedObjectSystem.GetState(unit));
            }
        }
    }

    public void RecordShot(in ShotTrace shot) => _events?.RecordShot(shot);

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

    public bool DoorsLocked => Movement.DoorsLocked;

    public bool FencesUp => Movement.FencesUp;

    public ScriptTimeline ScriptTimeline { get; private set; }

    public int Kills { get; private set; }

    public int Respawns { get; private set; }

    public IReadOnlyList<KnownObject> TakenPickups => _taken;

    public IReadOnlyList<Unit> Monsters => _monsters;

    public IReadOnlyList<ObjectProgram> Programs => _programs;

    public event Action<Unit>? MonsterAdded;

    public event Action<Unit>? UnitKilled;

    public event Action<int>? PlayerHurt;

    public event Action<TerrainChange>? TerrainChanged;

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

        if (!Player.IsAlive && IsInCannon)
        {
            EjectFromCannon();
        }

        if (!Player.IsAlive)
        {
            TryRespawn();
        }

        _updates.Compact();
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

    // Register the torso before the legs
    private void RegisterPlayer()
    {
        Register(UpdateRole.Torso, Weapons.Torso);
        Register(UpdateRole.Player, Player);
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
        ScriptTimeline = ScriptTimeline with { BlackoutAt = Time };
        Movement.SetGates(true, FencesUp);
    }

    private void OpenDoors()
    {
        ScriptTimeline = ScriptTimeline with { DoorsOpenedAt = Time };
        Movement.SetGates(false, FencesUp);
    }

    private void LowerFences()
    {
        ScriptTimeline = ScriptTimeline with { FencesDownAt = Time };
        Movement.SetGates(DoorsLocked, false);
    }

    [EngineRoutine(0x00440830)]
    [EngineRoutine(0x0044ca70)]
    public bool BuildFromProgram(ObjectProgram program, StackAction action, bool testsPlacement = true)
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

        if (testsPlacement && !IsFree(kind, position))
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

        _events?.RecordSpawn(new SpawnEvent(kind.Id, position, Time, program.Placement));

        return true;
    }

    public void AnimateFromProgram(ObjectProgram program, SpriteAnimation animation)
    {
        if (animation >= SpriteAnimation.Death && program.Placement is not null)
        {
            bool opensWall = program.Kind.Id is KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack or KnownObject.BreakableWall;
            RemovePlacement(program.Placement, program.Position.AsVector2(), program.Kind.Half, opensWall);
        }

        Effects.Play(program.Kind, animation, program.Position, program.Facing, Time);
    }

    private bool TickMonster(Unit unit)
    {
        if (unit.IsAlive)
        {
            _brain.Move(unit, FrameTime, _clock.FramesPerSecond);
        }

        if (TickUnit(unit))
            return true;

        _monsters.Remove(unit);
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
    }

    [EngineRoutine(0x00440110)]
    public bool TickUnit(Unit unit)
    {
        var layout = unit.Kind.Layout;
        var state = unit.Animation;

        if (unit.CreatedAt == Time || Time - Time % layout.GetFrameDuration(state) <= Time - FrameTime)
            return true;

        if (unit.Timer != 0)
        {
            unit.Timer = Time - unit.LastTickTime < unit.Timer ? unit.Timer - (Time - unit.LastTickTime) : 0;
        }

        bool everyFrame = unit.Kind.Object.Flags.HasFlag(ObjectFlags.ActsEveryFrame);

        if (unit.Frame == unit.FirstFrame || everyFrame)
        {
            RunStateHook(unit);
        }

        EmitStateChildren(unit);
        unit.Frame++;

        if (unit.Animation >= SpriteAnimation.Death && (unit.Frame > unit.LastFrame || layout.GetFrameCount(unit.Animation) == 0))
        {
            unit.Frame = unit.LastFrame;

            if (unit.Kind.Damage != 0)
            {
                Weapons.ApplyBlast(unit.Kind, unit.Position, unit.Shooter);
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
            else if (unit.Number >= 0)
            {
                _brain.Decide(unit, Player, FrameTime);
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

        unit.LastTickTime = Time;

        if (unit.Frame > unit.LastFrame)
        {
            unit.Frame = unit.FirstFrame;
        }

        return true;
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

        if (unit.Command == UnitCommand.Engage && (!flags.HasFlag(ObjectFlags.ActsEveryFrame) || unit.Frame == unit.LastFrame))
        {
            UnitKinematics.SetCommand(unit, UnitCommand.None);
        }
    }

    public int AllocateNumber() => _unitCount++;

    private int GetNextAnimationTick(int frameTime) => Time - Time % frameTime + frameTime;

    public void Damage(Unit unit, int damage)
    {
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

            unit.Hp -= damage;
            PlayerHurt?.Invoke(unit.Hp);

            return;
        }

        int previousHp = unit.Hp;
        unit.Hp -= damage;

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
        var reach = Player.Kind.Half + new Vector2(UnitGrid.MaxHalfSize);
        Units.Collect(position - reach, position + reach, _blastCandidates);
        foreach (var unit in _blastCandidates)
        {
            if (unit == Player || !unit.IsAlive || !Player.Kind.GetCollisionVolume(position.AsVector3()).OverlapsFlat(unit.CollisionVolume))
                continue;

            unit.Hp = 0;
            Units.Remove(unit);
            UnitKilled?.Invoke(unit);
        }

        Player.Position = _respawnPosition;
        Player.Speed = 0;
        Player.ClearMove();
        Player.SetState(SpriteAnimation.Stand);
        PlayerMovement.Reset();
        Weapons.ResetPlayer();
        Units.Move(Player);
        _protectedUntil = Time + RespawnProtection;
        Effects.Spawn(KnownObject.InvulnerabilityEffect, Player.Position + Vector3.UnitZ * ProtectionEffectHeight,
            Player.Facing, Time, Player, RespawnProtection);
        Respawns++;
    }

    private void CompleteDeath(Unit unit, SpriteAnimation animation)
    {
        if (unit.Placement is null)
        {
            Kills++;
            UnitKilled?.Invoke(unit);
        }
        else
        {
            RemovePlacement(unit.Placement, unit.Position.AsVector2(), unit.Kind.Half);
            _monsters.Add(unit);
            Register(UpdateRole.Monster, unit);
        }

        unit.DiedAt = Time;
        Units.Remove(unit);
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
                kind.RollInitialFrame(_rng);
                int expires = Time + DroppedItemLifetime + _rng.Next(DroppedItemLifetimeVariation + 1);
                float height = unit.Position.Z + (unit.Kind.Id == KnownObject.Bat ? BatItemHeight : DroppedItemHeight);
                _pickups.Add(new WorldPickup(kind, unit.Position with { Z = height }, null, Time, expires, facing));
            }
        }

        unit.DropItem = 0;
    }

    private void RemovePlacement(MergedMapSprite placement, Vector2 position, Vector2 half, bool opensWall = false)
    {
        if (Movement.Remove(placement) || opensWall)
        {
            TerrainChanged?.Invoke(new TerrainChange(position, half));
        }

        _events?.RecordSpriteRemoval(placement, Time);
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
                continue;
            }

            int interval = pickup.Kind.GetVisualFrameDuration(SpriteAnimation.Stand);

            if (Time - Time % interval <= Time - FrameTime)
                continue;

            if (pickup.Position.AsVector2().MeasureDistance(Player.Position.AsVector2()) < GetPickupReach(pickup.Id) && Inventory.TryTakePickup(pickup.Id))
            {
                _taken.Add(pickup.Id);
                _pickups.RemoveAt(index);

                if (pickup.Placement is not null)
                {
                    _events?.RecordPickup(pickup.Placement, Time);
                }

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

        Kinds.Loot.AssignItem(unit, _rng);

        _monsters.Add(unit);
        Register(UpdateRole.Monster, unit);
        Units.Add(unit);

        MonsterAdded?.Invoke(unit);

        return unit;
    }

    private bool IsFree(ObjectDefinition kind, Vector3 position)
    {
        if (!Movement.CanSpawn(kind.Object, position))
        {
            return false;
        }

        var reach = kind.Half + new Vector2(UnitGrid.MaxHalfSize);
        foreach (var other in Units.GetNear(position.AsVector2() - reach, position.AsVector2() + reach))
        {
            if (other.IsAlive && kind.GetCollisionVolume(position).Overlaps(other.CollisionVolume))
            {
                return false;
            }
        }

        return true;
    }

    public readonly record struct TerrainChange(Vector2 Position, Vector2 HalfSize);

}
