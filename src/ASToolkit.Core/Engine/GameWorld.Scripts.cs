using System.Collections.Frozen;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

internal sealed partial class GameWorld
{
    private static readonly (InputFlags Raw, ScriptInput Script)[] InputStateBits =
    [
        (InputFlags.LeftClick, ScriptInput.LeftClick),
        (InputFlags.RightClick, ScriptInput.RightClick),
        (InputFlags.LeftDown, ScriptInput.LeftDown),
        (InputFlags.RightDown, ScriptInput.RightDown),
        (InputFlags.Shift, ScriptInput.Shift),
        (InputFlags.Ctrl, ScriptInput.Ctrl),
        (InputFlags.Left, ScriptInput.Left),
        (InputFlags.Right, ScriptInput.Right),
        (InputFlags.Up, ScriptInput.Up),
        (InputFlags.Down, ScriptInput.Down),
        (InputFlags.First, ScriptInput.First),
        (InputFlags.Second, ScriptInput.Second)
    ];

    private const int SecondWindow = 1023;
    private const int SwitchReach = 40;
    private const int MountReach = 70;
    private const int MannedHealthScale = 30;
    private const int DismountDelay = 1000;
    private const int SineScale = 1024;
    private const int EjectDivisor = 15;
    private const int TrapPairReach = 10;
    private const int ChargeBox = 100;
    private const int BonusBox = 500;
    private const int BonusReach = 1000;
    private const int FreezeBulletReach = 130;
    private const int FreezeBulletDamage = 30;
    private const int BombDamage = 300;
    private const float AdrenalineBoost = 80;
    private const float DroneFlyHeight = 20;
    private const int DroneFollowReach = 20;
    private const int ElectricBallDrain = 10;
    private const int MedkitProtection = 3000;
    private const int FlashDuration = 0x900;
    private const int ShakeReach = 4;
    private const int ExplosionQuake = 120;
    private const int BlastQuake = 500;
    private const int NukeQuake = 1000;
    private const int BombQuake = 2500;
    private const int ChargeQuake = 4000;
    private const int DynamiteWinDelay = 5000;
    private const int White = 0xffffff;
    private const int FreezeColor = 0x1010ff;
    private const int DeathColor = 0xff0000;
    private const int FrozenDigit = 9;
    private const int MinBossId = 800;
    private const int MaxBossId = 879;
    private const int MarkerReach = 100;
    private const int PlayerChargeReach = 100;
    private const int MonsterChargeReach = 150;
    private const int DoorHoldTime = 2000;
    private const int SmallDoorReach = 80;
    private const int SmallDoorDepth = 64;
    private const int SmallDoorStep = 4;
    private const int DoubleDoorReach = 120;
    private const int DoubleDoorDepth = 72;
    private const int DoubleDoorStep = 2;
    private const int SingleFenceLevel = 10;
    private const int SingleFenceReach = 100;
    private static readonly Vector3 DismountStep = new(90, -90, 0);
    private static readonly Vector2 SingleFenceGenerator = new(7541, 4501);
    private static readonly Vector2 SingleFence = new(5697, 4734);

    private static readonly KnownObject[] MonsterIds =
    [
        KnownObject.Slug, KnownObject.YellowSlug, KnownObject.RedSlug, KnownObject.Monster1, KnownObject.YellowMonster1, KnownObject.RedMonster1,
        KnownObject.Monster2, KnownObject.YellowMonster2, KnownObject.RedMonster2, KnownObject.WeaponMonster, KnownObject.YellowWeaponMonster,
        KnownObject.RedWeaponMonster, KnownObject.TankMonster, KnownObject.YellowTankMonster, KnownObject.RedTankMonster, KnownObject.TankWeaponMonster,
        KnownObject.YellowTankWeaponMonster, KnownObject.RedTankWeaponMonster, KnownObject.Boss1, KnownObject.Boss2, KnownObject.Boss3, KnownObject.Boss4,
        KnownObject.BlueTankMonster, KnownObject.BlueTankWeaponMonster, KnownObject.Flyer, KnownObject.Diver, KnownObject.DiverBoss
    ];

    private static readonly FrozenSet<KnownObject> MonsterIdSet = MonsterIds.ToFrozenSet();

    // Stop at the first boss of the list as the freeze bonus loop does
    private static readonly FrozenSet<KnownObject> FreezableMonsterIds = MonsterIds.TakeWhile(id => !IsBossVid(id)).ToFrozenSet();

    private readonly LevelScript _level;
    private readonly List<Unit> _scripted = [];
    private readonly List<Unit> _sites = [];
    private readonly List<Unit> _charges = [];
    private readonly Dictionary<MergedMapSprite, Unit> _bodies = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MergedMapSprite> _removed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MergedMapSprite, Unit> _statics = new(ReferenceEqualityComparer.Instance);
    private readonly List<MergedMapSprite> _sprites;
    private readonly bool[] _triggersPassed;
    private readonly MergedMapSprite?[][] _crowdSprites;
    private int _sitesClearedAt;
    private int _scriptSecondAt;

    public bool HasWon { get; private set; }

    public IReadOnlyList<Unit> DynamiteSites => _sites;

    public IReadOnlyList<Unit> ArmedDynamite => _charges;

    public IEnumerable<Unit> Bodies => _bodies.Values;

    public IReadOnlyList<MergedMapSprite> Sprites => _sprites;

    public int RemovedCount => _removed.Count;

    public bool IsRemoved(MergedMapSprite sprite) => _removed.Contains(sprite);

    public Unit? FindPlacementUnit(MergedMapSprite sprite) =>
        _bodies.TryGetValue(sprite, out var body) ? body : _statics.GetValueOrDefault(sprite) ?? _scripted.Find(unit => unit.Placement == sprite);

    public IReadOnlyList<Unit> Scripted => _scripted;

    private Vector3 GetPlacementPosition(MergedMapSprite sprite) =>
        _bodies.TryGetValue(sprite, out var body) ? body.Position : _statics.TryGetValue(sprite, out var stand) ? stand.Position : sprite.Position;

    private void AddStatic(MergedMapSprite sprite, ObjectDefinition kind)
    {
        bool hashed = UnitGrid.IsHashed(kind);

        if ((!hashed && kind.Object.Link == 0) || kind.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man or SpriteClass.Cannon)
            return;

        var stand = new Unit(-1, kind, sprite.Position, sprite.Army, 0) { Placement = sprite, Direction = sprite.Direction };
        _statics[sprite] = stand;

        if (hashed)
        {
            Units.Add(stand);
        }

        CreateLink(stand);
    }

    private Unit AddScripted(ObjectDefinition kind, Vector3 position, Angle direction, Army? army = null, MergedMapSprite? placement = null)
    {
        var unit = new Unit(-1, kind, position, army ?? Kinds.GetArmy(kind), 0) { Placement = placement, CreatedAt = Time };
        unit.Start(kind.InitialAnimation, direction, Time, _rng);
        _scripted.Add(unit);
        Register(UpdateRole.Scripted, unit);
        Units.Add(unit);

        if (kind.Id == KnownObject.DynamiteSite)
        {
            _sites.Add(unit);
        }

        CreateLink(unit);

        return unit;
    }

    private bool TickScripted(Unit unit)
    {
        if ((unit.Owner is null || unit.Owner.Link == unit) && Tact(unit))
            return true;

        _scripted.Remove(unit);
        _sites.Remove(unit);
        Units.Remove(unit);
        Effects.Detach(unit);

        if (unit.Placement is not null)
        {
            RemoveSprite(unit.Placement);
        }

        if (unit.Kind.Id == KnownObject.Fence && FencesUp && !_scripted.Exists(other => other.Kind.Id == KnownObject.Fence))
        {
            LowerFences();
        }

        return false;
    }

    [ScriptFunction("main")]
    private void RunLevelScript()
    {
        StopQuake();

        if (Player.IsAlive)
        {
            _deathFlashed = false;
        }
        else if (!_deathFlashed)
        {
            _deathFlashed = true;
            Flash(DeathColor);
        }

        if (_adrenalineActive)
        {
            _adrenalineActive = false;
            Inventory.SetSpeedBoost(0);
        }

        TryDismount();

        if (_level.ClearsMonsters && OneTimeInSecond())
        {
            CheckVictory();
        }

        for (int index = 0; index < _triggersPassed.Length; index++)
        {
            var trigger = _level.Triggers[index];

            if (_triggersPassed[index] || !IsPlayerWithin(trigger.Area))
                continue;

            _triggersPassed[index] = true;
            RunTrigger(trigger);
        }

        for (int index = 0; index < _crowdSprites.Length; index++)
        {
            ApplyCrowdRule(_level.CrowdRules[index], _crowdSprites[index]);
        }

        if (_level.WinsAfterSites && Player.IsAlive)
        {
            int now = GetTime();

            if (_sitesClearedAt == 0 && _sites.Count == 0)
            {
                _sitesClearedAt = now;
            }

            if (_sitesClearedAt != 0 && now - _sitesClearedAt > DynamiteWinDelay)
            {
                HasWon = true;
            }
        }
    }

    [ScriptFunction("iff")]
    private void RunTrigger(ScriptTrigger trigger)
    {
        if (trigger.Respawn == ScriptRespawn.Fixed)
        {
            _respawnPosition = new Vector3(trigger.RespawnPoint.X, trigger.RespawnPoint.Y, 0);
        }
        else if (trigger.Respawn == ScriptRespawn.Player)
        {
            _respawnPosition = new Vector3((int)Player.Position.X, (int)Player.Position.Y, (int)Player.Position.Z);
        }

        if (trigger.Quake != 0)
        {
            EarthQuake(trigger.Quake);
        }

        if (trigger.LocksDoors || trigger.LightOff is not null)
        {
            StartBlackout();
        }

        if (trigger.SwapsKeypads)
        {
            SwapSprites(KnownObject.KeypadOn, KnownObject.KeypadOff);
        }

        KillAll(trigger.Kills);
        HasWon |= trigger.Wins;
    }

    [ScriptFunction("GetVidData")]
    private void ApplyCrowdRule(ScriptCrowdRule rule, MergedMapSprite?[] placed)
    {
        int count = 0;
        foreach (var unit in _monsters)
        {
            if (unit.Kind.Id == rule.Counted)
            {
                count++;
            }
        }

        var kind = Kinds.Find(rule.Placed);
        for (int index = 0; index < placed.Length; index++)
        {
            var sprite = placed[index];

            if (count > rule.Max && sprite is null && kind is not null)
            {
                sprite = CreateSprite(rule.Placed, rule.Points[index], default);
                var body = new Unit(_unitCount++, kind, sprite.Position, 0, kind.MaxHp) { Placement = sprite };
                Units.Add(body);
                _bodies.Add(sprite, body);
                CreateLink(body);
                placed[index] = sprite;
            }
            else if (count <= rule.Max && sprite is not null)
            {
                Destroy(sprite);
                placed[index] = null;
            }
        }
    }

    private void RunLevelHandler(Unit unit)
    {
        foreach (var handler in _level.Handlers)
        {
            if (handler.Id != unit.Kind.Id || handler.State != unit.Animation)
                continue;

            foreach (var id in handler.Destroys)
            {
                Genocide(id);
            }

            KillAll(handler.Kills);
            HasWon |= handler.Wins;
        }
    }

    [ScriptFunction("ACT_CHANGE_ANIMATION")]
    private void KillAll(IReadOnlyList<KnownObject> ids)
    {
        if (ids.Count == 0)
            return;

        foreach (var unit in _monsters.ToArray())
        {
            if (unit.IsAlive && ids.Contains(unit.Kind.Id))
            {
                unit.Hp = 0;
                CompleteDeath(unit, SpriteAnimation.Death);
            }
        }
    }

    [ScriptFunction("Genocide")]
    private void Genocide(KnownObject id)
    {
        foreach (var program in _programs)
        {
            if (program.Kind.Id != id || program.Finished)
                continue;

            program.Stop();

            if (program.Placement is not null)
            {
                RemoveSprite(program.Placement);
            }
        }

        foreach (var unit in _monsters)
        {
            if (unit.IsAlive && unit.Kind.Id == id)
            {
                unit.Hp = 0;
                Units.Remove(unit);
            }
        }
    }

    [ScriptFunction("OneTimeInSecond")]
    private bool OneTimeInSecond()
    {
        int now = GetTime();

        if (now - _scriptSecondAt > SecondWindow || _scriptSecondAt > now)
        {
            _scriptSecondAt = now;
        }

        return now == _scriptSecondAt;
    }

    [ScriptFunction("GetTime")]
    [EngineRoutine(0x00435690)]
    private int GetTime() => Time + FrameTime;

    [ScriptFunction("CompaignGameTact")]
    private void CheckVictory()
    {
        if (_sites.Count != 0)
            return;

        foreach (var unit in _monsters)
        {
            if (unit.Army == Army.Monsters && MonsterIdSet.Contains(unit.Kind.Id))
                return;
        }

        HasWon = true;
    }

    [EngineRoutine(0x0040e870)]
    public void RunCreationHook(Unit unit)
    {
        switch (unit.Kind.Id)
        {
            case KnownObject.ElectricBall:
                DrainPlayer();
                break;
            case KnownObject.RescueMedkitEffect:
                ProtectAfterRescue();
                break;
            default:
                Kinds.Loot.AssignItem(unit, _rng);
                break;
        }
    }

    private void RunStateHook(Unit unit)
    {
        RunLevelHandler(unit);

        var state = unit.Animation;
        switch (unit.Kind.Id)
        {
            case KnownObject.Breaker when state == SpriteAnimation.Stand:
                SwitchBreaker(unit);
                break;
            case KnownObject.MountedCannon when state == SpriteAnimation.Stand:
                MountCannon(unit);
                break;
            case KnownObject.DynamiteSite when state == SpriteAnimation.Stand:
                PlantDynamite(unit);
                break;
            case KnownObject.SmallDoor when state == SpriteAnimation.Stand:
                MoveDoor(unit, SmallDoorReach, SmallDoorDepth, SmallDoorStep);
                break;
            case KnownObject.DoubleDoor when state == SpriteAnimation.Stand:
                MoveDoor(unit, DoubleDoorReach, DoubleDoorDepth, DoubleDoorStep);
                break;
            case KnownObject.Generator when state >= SpriteAnimation.Death:
                DestroyGenerator(unit);
                break;
            case KnownObject.ArmedDynamite when state >= SpriteAnimation.Death:
                Flash(White);
                EarthQuake(ChargeQuake);
                DetonateDynamite(unit);
                break;
            case KnownObject.Missile when state == SpriteAnimation.Death:
                Flash(White);
                EarthQuake(NukeQuake);
                break;
            case KnownObject.Barrel or KnownObject.Cylinder or KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack or KnownObject.ShakeDummy or
                KnownObject.NeuralBullet when state == SpriteAnimation.Death:
                EarthQuake(BlastQuake);
                break;
            case KnownObject.PlasmaBullet or KnownObject.Rocket or KnownObject.HeavyMonsterRocket or KnownObject.CannonShell when state == SpriteAnimation.Death:
            case KnownObject.ElectricBall when state == SpriteAnimation.Death:
                EarthQuake(ExplosionQuake);
                break;
            case KnownObject.FreezeBullet when state >= SpriteAnimation.Death:
                BurstFreezeBullet(unit);
                break;
            case KnownObject.FlyingDrone when state <= SpriteAnimation.StartMove:
                FollowPlayer(unit);
                break;
            case KnownObject.FlyingDrone when state >= SpriteAnimation.Death:
                Inventory.SetItem(KnownObject.Drone, 0);
                break;
            default:
                if (state >= SpriteAnimation.Death)
                {
                    CreateItems(unit);
                }
                break;
        }
    }

    [ScriptFunction("F418_0")]
    private void SwitchBreaker(Unit breaker)
    {
        if (SizeTo(Player.Position, breaker.Position.AsVector2()) >= SwitchReach)
            return;

        CreateSprite(KnownObject.BreakerOn, breaker.Position, breaker.Direction);
        breaker.ChangeAnimation(SpriteAnimation.Death);
        OpenDoors();
        foreach (var sprite in _sprites)
        {
            if (sprite.Id == KnownObject.ObjectMarker && SizeTo(breaker.Position, sprite.Position.AsVector2()) < MarkerReach)
            {
                RemoveSprite(sprite);
            }
        }

        SwapSprites(KnownObject.KeypadOff, KnownObject.KeypadOn);

        var traps = new List<MergedMapSprite>();
        foreach (var sprite in _sprites)
        {
            if (_removed.Contains(sprite) || sprite.Id is not (KnownObject.BreakableWall or KnownObject.SmallMonsterHole) ||
                (sprite.Id == KnownObject.BreakableWall && !_bodies.ContainsKey(sprite)))
                continue;

            int pair = traps.FindIndex(trap => SizeTo(trap.Position, sprite.Position.AsVector2()) < TrapPairReach);

            if (pair < 0)
            {
                traps.Add(sprite);
                continue;
            }

            Destroy(sprite.Id == KnownObject.BreakableWall ? sprite : traps[pair]);
            traps[pair] = traps[^1];
            traps.RemoveAt(traps.Count - 1);
        }
    }

    [ScriptFunction("F179_0")]
    private void PlantDynamite(Unit site)
    {
        if (SizeTo(Player.Position, site.Position.AsVector2()) >= SwitchReach || !Inventory.TryConsumeItem(KnownObject.Dynamite))
            return;

        var charge = Effects.Spawn(KnownObject.ArmedDynamite, site.Position, default, Time);

        if (charge is not null)
        {
            charge.MarkReferenced();
            _charges.Add(charge);
        }

        site.ChangeAnimation(SpriteAnimation.Death);
    }

    [ScriptFunction("F184_15")]
    private void DetonateDynamite(Unit charge)
    {
        _charges.Remove(charge);
        var center = new Vector2((int)charge.Position.X, (int)charge.Position.Y);

        if (IsInChargeCells(Player.Position, center) && SizeTo(Player.Position, center) < PlayerChargeReach && Player.IsAlive)
        {
            Player.Hp = 0;
        }

        foreach (var unit in _scripted)
        {
            if (unit.Kind.Id.IsEndlessGenerator && unit.Animation < SpriteAnimation.Death && IsInChargeCells(unit.Position, center))
            {
                unit.ChangeAnimation(SpriteAnimation.Death);
            }
        }

        foreach (var program in _programs)
        {
            if (!IsInChargeCells(program.Position, center))
                continue;

            bool listed = program.Kind.Id is KnownObject.Spawner or KnownObject.TeleportPad || program.Kind.Id.IsEndlessGenerator;

            if (!listed && !(program.Placement is { Army: Army.Monsters } && IsInChargeReach(charge, program.Position)))
                continue;

            program.Stop();
            AnimateFromProgram(program, SpriteAnimation.Death);
        }

        var owned = GetOwnedPlacements();
        for (int i = 0; i < _sprites.Count; i++)
        {
            var sprite = _sprites[i];

            if (_removed.Contains(sprite) || !IsInChargeCells(GetPlacementPosition(sprite), center))
                continue;

            if (sprite.Id is KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole)
            {
                CreateSprite(KnownObject.RubblePile, sprite.Position, sprite.Direction);
            }
            else if (sprite.Army == Army.Monsters && !owned.Contains(sprite) && IsInChargeReach(charge, GetPlacementPosition(sprite)))
            {
                KillPlacement(sprite);
            }
        }

        for (var unit = Units.First(center - new Vector2(ChargeBox), center + new Vector2(ChargeBox)); unit is not null; unit = Units.Next())
        {
            if (unit is { IsAlive: true, Army: Army.Monsters } &&
                SizeTo(charge.Position, unit.Position.AsVector2()) < MonsterChargeReach)
            {
                unit.Hp = 0;
                CompleteDeath(unit, SpriteAnimation.Death);
            }
        }
    }

    [ScriptFunction("F415_15")]
    [ScriptFunction("F415_16")]
    private void DestroyGenerator(Unit generator)
    {
        if (_level.LevelNumber == SingleFenceLevel && SizeTo(generator.Position, SingleFenceGenerator) < SingleFenceReach)
        {
            var fence = FindSprite(KnownObject.Fence, SingleFence);

            if (fence is not null)
            {
                Destroy(fence);
            }
        }
        else
        {
            foreach (var unit in _scripted)
            {
                if (unit.Kind.Id == KnownObject.Fence)
                {
                    unit.ChangeAnimation(SpriteAnimation.Death);
                }
            }
        }

        var marker = FindSprite(KnownObject.ShootHere, generator.Position.AsVector2());

        if (marker is not null)
        {
            RemoveSprite(marker);
        }
    }

    [ScriptFunction("F605_0")]
    [ScriptFunction("F607_0")]
    private void MoveDoor(Unit door, int reach, int depth, int step)
    {
        int z = (int)door.Position.Z;

        if (SizeTo(Player.Position, door.Position.AsVector2()) < reach && !DoorsLocked)
        {
            if (z != -depth)
            {
                SetDoorHeight(door, z - step);
            }

            door.Timer = DoorHoldTime;
        }
        else if (z < 0)
        {
            if (door.Timer == 0 || DoorsLocked)
            {
                if (z != -depth || !Units.HasUnitAt(door.Position.AsVector2(), Movement.GetHeight(door.Position.AsVector2())) || DoorsLocked)
                {
                    SetDoorHeight(door, z + step);
                }
            }
            else if (z != -depth)
            {
                SetDoorHeight(door, z - step);
            }
        }
    }

    private void SetDoorHeight(Unit door, int height)
    {
        door.Position = door.Position with { Z = height };
        Units.ChangeCoor(door);
    }

    [EngineRoutine(0x00440830)]
    public void DestroySprite(KnownObject id, Vector2 point)
    {
        var sprite = FindSprite(id, point);

        if (sprite is not null)
        {
            Destroy(sprite);
        }
    }

    private void Destroy(MergedMapSprite sprite)
    {
        if (_bodies.Remove(sprite, out var body))
        {
            body.Hp = 0;
            Units.Remove(body);
            Effects.Detach(body);
        }

        for (int index = _scripted.Count - 1; index >= 0; index--)
        {
            var unit = _scripted[index];

            if (ReferenceEquals(unit.Placement, sprite))
            {
                _updates.Remove(unit.Kind.Layer, unit);
                _scripted.RemoveAt(index);
                Units.Remove(unit);
                Effects.Detach(unit);
            }
        }

        RemoveSprite(sprite);
    }

    [ScriptFunction("CreateSprite")]
    private MergedMapSprite CreateSprite(KnownObject id, Vector3 position, Angle direction)
    {
        var sprite = new MergedMapSprite(0, id, position, direction, Army.Player, []);
        _sprites.Add(sprite);

        Kinds.AddTerrain(Movement, sprite);


        var kind = Kinds.Find(id);

        if (kind is not null && id.IsScripted)
        {
            AddScripted(kind, position, direction, placement: sprite);
        }
        else if (kind is not null)
        {
            AddStatic(sprite, kind);
        }

        return sprite;
    }

    public bool IsInCannon => _mountedAt >= 0;

    public bool IsProtected => Time < _protectedUntil;

    [ScriptFunction("F357_0")]
    private void MountCannon(Unit cannon)
    {
        if (IsInCannon || !Player.IsAlive || SizeTo(Player.Position, cannon.Position.AsVector2()) >= MountReach)
            return;

        Inventory.StowArmor();
        _mountedAt = GetTime();
        Player.Position = cannon.Position;
        Player.Speed = 0;
        Action(Player, ActionCode.ChangeVid, (int)KnownObject.MannedCannon);
        Player.SetDirection(cannon.Direction);
        Player.Hp *= MannedHealthScale;
        Units.ChangeCoor(Player);
        cannon.ChangeAnimation(SpriteAnimation.Death);
    }

    [ScriptFunction("GetInputState")]
    private ScriptInput GetInputState()
    {
        var flags = Input.Flags;
        var state = ScriptInput.None;
        foreach (var (raw, script) in InputStateBits)
        {
            if ((flags & raw) != 0)
            {
                state |= script;
            }
        }

        return state;
    }

    private void TryDismount()
    {
        if (!IsInCannon || GetTime() - _mountedAt <= DismountDelay || (GetInputState() & (ScriptInput.Left | ScriptInput.Right | ScriptInput.Up | ScriptInput.Down | ScriptInput.Second)) == 0)
            return;

        CreateSprite(KnownObject.MountedCannon, Player.Position, Weapons.Torso.Direction);
        _mountedAt = -1;
        Action(Player, ActionCode.ChangeVid, (int)KnownObject.PlayerStart);
        Player.Position += DismountStep;
        Player.Hp /= MannedHealthScale;
        Inventory.RestoreArmor();
        Units.ChangeCoor(Player);
    }

    [ScriptFunction("F350_15")]
    private void EjectFromCannon()
    {
        var direction = Weapons.Torso.Direction;
        CreateSprite(KnownObject.MountedCannon, Player.Position, direction);
        _mountedAt = -1;
        float angle = direction.Value * MathF.Tau / Angle.FullTurn;
        int ejectX = (int)(MathF.Sin(angle) * SineScale) / EjectDivisor;
        int ejectY = -(int)(MathF.Cos(angle) * SineScale) / EjectDivisor;
        Player.Position += new Vector3(ejectX, ejectY, 0);
        Action(Player, ActionCode.ChangeVid, (int)KnownObject.PlayerStart);
        Inventory.RestoreArmor();
        Units.ChangeCoor(Player);
    }

    private void SwapSprites(KnownObject from, KnownObject to)
    {
        int count = _sprites.Count;
        for (int index = 0; index < count; index++)
        {
            var sprite = _sprites[index];

            if (sprite.Id != from || _removed.Contains(sprite))
                continue;

            CreateSprite(to, GetPlacementPosition(sprite), sprite.Direction);
            Destroy(sprite);
        }
    }

    private void RemoveSprite(MergedMapSprite sprite)
    {
        if (_removed.Contains(sprite))
            return;

        RemovePlacement(sprite);
    }

    [EngineRoutine(0x0040fc40)]
    private MergedMapSprite? FindSprite(KnownObject id, Vector2 point)
    {
        MergedMapSprite? found = null;
        float foundX = float.MaxValue;
        float foundY = float.MaxValue;
        foreach (var sprite in _sprites)
        {
            var kind = Kinds.Find(id);

            if (sprite.Id != id || _removed.Contains(sprite) || kind is null)
                continue;

            if (kind.GetCollisionVolume(GetPlacementPosition(sprite)).Covers(point) && (found is null || kind.Object.Size.X < foundX || kind.Object.Size.Y < foundY))
            {
                found = sprite;
                foundX = kind.Object.Size.X;
                foundY = kind.Object.Size.Y;
            }
        }

        return found;
    }

    private bool IsPlayerWithin(TriggerArea area) => SizeTo(Player.Position, new Vector2(area.Center.X, area.Center.Y)) < area.Radius;

    [EngineRoutine(0x0042dd60)]
    [ScriptFunction("FirstInBox")]
    private bool IsInChargeCells(Vector3 position, Vector2 center) => Units.IsInQuery(position.AsVector2(), center - new Vector2(ChargeBox), center + new Vector2(ChargeBox));

    private static bool IsInChargeReach(Unit charge, Vector3 position) => SizeTo(charge.Position, position.AsVector2()) < MonsterChargeReach;

    private HashSet<MergedMapSprite> GetOwnedPlacements()
    {
        var owned = new HashSet<MergedMapSprite>(ReferenceEqualityComparer.Instance);
        foreach (var program in _programs)
        {
            if (program.Placement is not null)
            {
                owned.Add(program.Placement);
            }
        }

        foreach (var unit in _scripted.Concat(_monsters).Concat(_bodies.Values))
        {
            if (unit.Placement is not null)
            {
                owned.Add(unit.Placement);
            }
        }

        owned.UnionWith(_bodies.Keys);

        return owned;
    }

    [ScriptFunction("ACT_GET_ARMY")]
    [ScriptFunction("ANI_DEATH")]
    private void KillPlacement(MergedMapSprite sprite)
    {
        var kind = Kinds.Find(sprite.Id);

        if (kind is null || kind.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man)
            return;

        Effects.Play(kind, SpriteAnimation.Death, GetPlacementPosition(sprite), sprite.Direction, Time);
        RemoveSprite(sprite);
    }

    [ScriptFunction("SizeTo")]
    private static int SizeTo(Vector3 position, Vector2 point) =>
        (int)position.AsVector2().MeasureDistance(new Vector2((int)point.X, (int)point.Y));

    [ScriptFunction("F145_15")]
    private void BurstFreezeBullet(Unit bullet)
    {
        var center = bullet.Position.AsVector2();
        for (var unit = Units.First(center - new Vector2(FreezeBulletReach), center + new Vector2(FreezeBulletReach)); unit is not null; unit = Units.Next())
        {
            if (SizeTo(unit.Position, center) >= FreezeBulletReach ||
                unit.Army != Army.Monsters || (int)unit.Kind.Id % 10 == FrozenDigit)
                continue;

            Damage(unit, FreezeBulletDamage);

            if (!IsBossVid(unit.Kind.Id) && unit.Animation >= SpriteAnimation.Death)
            {
                Freeze(unit);
            }
        }
    }

    [ScriptFunction("IsBossVid")]
    private static bool IsBossVid(KnownObject id) => (int)id is >= MinBossId and <= MaxBossId;

    [ScriptFunction("F234_0")]
    private void FreezeMonsters(Vector2 center)
    {
        foreach (var unit in GetBonusTargets(center))
        {
            if (FreezableMonsterIds.Contains(unit.Kind.Id))
            {
                Freeze(unit);
            }
        }

        Flash(FreezeColor);
    }

    [ScriptFunction("ACT_CHANGE_VID")]
    private void Freeze(Unit unit)
    {
        var id = unit.Kind.Id;
        var frozen = Kinds.Find(id - (int)id % 10 + FrozenDigit);

        if (frozen is not null)
        {
            if (!unit.IsAlive)
            {
                unit.Hp = unit.HpBeforeDeath;
            }

            Action(unit, ActionCode.ChangeVid, (int)frozen.Id, (int)SpriteAnimation.Stand);
        }

        unit.Behavior = WeaponBehavior.None;

        unit.Link?.Behavior = WeaponBehavior.None;

        MonsterBrain.Attack(unit, null);
        UnitKinematics.StopMoving(unit);
        unit.Speed = 0;
    }

    [ScriptFunction("F236_0")]
    private void DetonateBomb(Vector2 center)
    {
        EarthQuake(BombQuake);
        Flash(White);
        foreach (var unit in GetBonusTargets(center))
        {
            Damage(unit, BombDamage);
        }
    }

    private IEnumerable<Unit> GetBonusTargets(Vector2 center)
    {
        for (var unit = Units.First(center - new Vector2(BonusBox), center + new Vector2(BonusBox)); unit is not null; unit = Units.Next())
        {
            if (SizeTo(unit.Position, center) < BonusReach && unit.Army == Army.Monsters)
            {
                yield return unit;
            }
        }
    }

    [ScriptFunction("F238_0")]
    private void TakeAdrenaline()
    {
        _adrenalineActive = true;
        Inventory.SetSpeedBoost(AdrenalineBoost);
    }

    [ScriptFunction("F231_0")]
    private bool TakeDrone()
    {
        if (Inventory.Items.GetValueOrDefault(KnownObject.Drone) > 0)
            return false;

        AddDrone();

        return true;
    }

    private void AddDrone()
    {
        var kind = Kinds.Find(KnownObject.FlyingDrone);

        if (kind is null)
            return;

        Inventory.SetItem(KnownObject.Drone, 1);
        AddMonster(kind, Player.Position + Vector3.UnitZ * DroneFlyHeight, Player.Direction, Kinds.GetArmy(kind));
    }

    [ScriptFunction("F290_0")]
    private void FollowPlayer(Unit drone)
    {
        if (SizeTo(Player.Position, drone.Position.AsVector2()) > DroneFollowReach)
        {
            if (_rng.Next() % 2 != 0)
                return;
            
            Action(drone, ActionCode.Move, (int)Player.Position.X, (int)Player.Position.Y, (int)(Player.Position.Z + DroneFlyHeight));
        }
        else
        {
            UnitKinematics.StopMoving(drone);
        }
    }

    [ScriptFunction("F751_14")]
    private void DrainPlayer()
    {
        if (Player.IsAlive)
        {
            Player.Hp -= ElectricBallDrain;
        }
    }

    [ScriptFunction("F181_14")]
    private void ProtectAfterRescue()
    {
        _protectedUntil = Math.Max(_protectedUntil, Time + MedkitProtection);
        Effects.Spawn(KnownObject.InvulnerabilityEffect, Player.Position + Vector3.UnitZ * ProtectionEffectHeight, Player.Direction, Time, Player, MedkitProtection);
    }

    [ScriptFunction("EarthQuake")]
    private void EarthQuake(int delay)
    {
        _quakeStart = GetTime();
        _quakeDelay = delay;
        SetEnvironment(MapEnvironment.Earthquake);
    }

    [ScriptFunction("GameTact")]
    private void StopQuake()
    {
        int now = GetTime();

        if (_quakeStart != 0 && (now - _quakeStart > _quakeDelay || _quakeStart > now))
        {
            _quakeStart = 0;
            SetEnvironment(MapEnvironment.Earthquake | MapEnvironment.Stop);
        }
    }

    [ScriptFunction("SetEnvironment")]
    [EngineRoutine(0x00431cf0)]
    private void SetEnvironment(MapEnvironment environment)
    {
        if ((environment & MapEnvironment.Stop) != 0)
        {
            _environment &= ~environment;
            return;
        }

        if (environment is MapEnvironment.Shadow or MapEnvironment.LongShadow)
        {
            _environment &= ~(MapEnvironment.Shadow | MapEnvironment.LongShadow);
        }

        if ((environment & MapEnvironment.Rain) != 0)
        {
            _environment &= ~MapEnvironment.Rain;
        }

        if ((environment & MapEnvironment.Snow) != 0)
        {
            _environment &= ~MapEnvironment.Snow;
        }

        _environment |= environment;
    }

    [EngineRoutine(0x00430870)]
    [EngineRoutine(0x00430870)]
    private void ShakeCamera()
    {
        if ((_environment & MapEnvironment.Earthquake) == 0)
        {
            CameraShake = null;
            return;
        }

        int y = _rng.Next() % (ShakeReach * 2 + 1);
        int x = _rng.Next() % (ShakeReach * 2 + 1);
        CameraShake = new CameraShake(Time, new Vector2(ShakeReach - x, ShakeReach - y));
    }

    [ScriptFunction("Effect")]
    [EngineRoutine(0x00402690)]
    private void Flash(int color) => ScreenFlash = new ScreenFlash(Time, FlashDuration, color);

    public CameraShake? CameraShake { get; private set; }

    public ScreenFlash? ScreenFlash { get; private set; }
}
