using System.Collections.Frozen;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

internal sealed partial class GameWorld
{
    private const int SecondWindow = 1023;
    private const int LoadedClock = 10;
    private const int SwitchReach = 40;
    private const int MountReach = 70;
    private const int MannedHealthScale = 30;
    private const int DismountDelay = 1000;
    private const int SineScale = 1024;
    private const int EjectDivisor = 15;
    private const int TrapPairReach = 10;
    private const int ChargeBox = 100;
    private const float HashCell = 128;
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

    private static readonly FrozenSet<KnownObject> VictoryMonsters = new[]
    {
        KnownObject.Slug, KnownObject.YellowSlug, KnownObject.RedSlug, KnownObject.Monster1, KnownObject.YellowMonster1, KnownObject.RedMonster1,
        KnownObject.Monster2, KnownObject.YellowMonster2, KnownObject.RedMonster2, KnownObject.WeaponMonster, KnownObject.YellowWeaponMonster,
        KnownObject.RedWeaponMonster, KnownObject.TankMonster, KnownObject.YellowTankMonster, KnownObject.RedTankMonster, KnownObject.TankWeaponMonster,
        KnownObject.YellowTankWeaponMonster, KnownObject.RedTankWeaponMonster, KnownObject.Boss1, KnownObject.Boss2, KnownObject.Boss3, KnownObject.Boss4,
        KnownObject.TankBoss, KnownObject.BlueTankWeaponBoss, KnownObject.Bat, KnownObject.Diver, KnownObject.DiverBoss
    }.ToFrozenSet();

    private readonly LevelScript _level;
    private readonly List<Unit> _scripted = [];
    private readonly List<Unit> _sites = [];
    private readonly List<Unit> _charges = [];
    private readonly Dictionary<MergedMapSprite, Unit> _bodies = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MergedMapSprite> _removed = new(ReferenceEqualityComparer.Instance);
    private readonly List<MergedMapSprite> _sprites;
    private readonly bool[] _checkpointsPassed;
    private bool _blackoutPassed;
    private int _scriptSecondAt;

    public bool HasWon { get; private set; }

    public IReadOnlyList<Unit> DynamiteSites => _sites;

    public IReadOnlyList<Unit> ArmedDynamite => _charges;

    public IEnumerable<Unit> Bodies => _bodies.Values;

    public IReadOnlyList<Unit> Scripted => _scripted;

    private void AddScripted(ObjectDefinition kind, Vector3 position, Facing facing, int army = 0, MergedMapSprite? placement = null)
    {
        var unit = new Unit(-1, kind, position, army, 0) { Placement = placement, CreatedAt = Time };
        unit.Start(kind.InitialAnimation, facing, Time, _rng);
        _scripted.Add(unit);
        Register(UpdateRole.Scripted, unit);

        if (kind.Id == KnownObject.DynamiteSite)
        {
            _sites.Add(unit);
        }
        else if (placement is not null && kind.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor)
        {
            _events?.RecordDoorPosition(placement, 0, position);
        }
    }

    private bool TickScripted(Unit unit)
    {
        if (TickUnit(unit))
            return true;

        _scripted.Remove(unit);
        _sites.Remove(unit);

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
        TryDismount();

        if (_level.ClearsMonsters && IsSecondTick())
        {
            CheckVictory();
        }

        if (_level.Exit is not null && IsPlayerWithin(_level.Exit.Value))
        {
            HasWon = true;
        }

        if (_level.Blackout is not null && !_blackoutPassed && IsPlayerWithin(_level.Blackout.Value))
        {
            _blackoutPassed = true;
            _respawnPosition = new Vector3((int)Player.Position.X, (int)Player.Position.Y, (int)Player.Position.Z);
            StartBlackout();
            SwapSprites(KnownObject.KeypadOn, KnownObject.KeypadOff);
        }

        for (int index = 0; index < _checkpointsPassed.Length; index++)
        {
            var checkpoint = _level.Checkpoints[index];

            if (_checkpointsPassed[index] || !IsPlayerWithin(checkpoint))
                continue;

            _checkpointsPassed[index] = true;
            _respawnPosition = new Vector3(checkpoint.Center.X, checkpoint.Center.Y, 0);
        }
    }

    [ScriptFunction("OneTimeInSecond")]
    private bool IsSecondTick()
    {
        int now = GetScriptTime();

        if (now - _scriptSecondAt > SecondWindow || _scriptSecondAt > now)
        {
            _scriptSecondAt = now;
        }

        return now == _scriptSecondAt;
    }

    [ScriptFunction("GetTime")]
    [EngineRoutine(0x00435690)]
    private int GetScriptTime() => Time + LoadedClock + FrameTime;

    [ScriptFunction("CompaignGameTact")]
    private void CheckVictory()
    {
        if (_sites.Count != 0)
            return;

        foreach (var unit in _monsters)
        {
            if (unit.Army == MonsterArmy && VictoryMonsters.Contains(unit.Kind.Id))
                return;
        }

        HasWon = true;
    }

    private void RunStateHook(Unit unit)
    {
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
            case KnownObject.ArmedDynamite when state >= SpriteAnimation.Death:
                DetonateDynamite(unit);
                break;
            case KnownObject.Generator when state >= SpriteAnimation.Death:
                DestroyGenerator(unit);
                break;
            default:
                if (state >= SpriteAnimation.Death)
                {
                    DropItem(unit);
                }
                break;
        }
    }

    [ScriptFunction("F418_0")]
    private void SwitchBreaker(Unit breaker)
    {
        if (MeasureScriptDistance(Player.Position, breaker.Position.AsVector2()) >= SwitchReach)
            return;

        CreateSprite(KnownObject.BreakerOn, breaker.Position, breaker.Facing);
        breaker.SetState(SpriteAnimation.Death);
        OpenDoors();
        foreach (var sprite in _sprites)
        {
            if (sprite.Id == KnownObject.ObjectMarker && MeasureScriptDistance(breaker.Position, sprite.Position.AsVector2()) < MarkerReach)
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

            int pair = traps.FindIndex(trap => MeasureScriptDistance(trap.Position, sprite.Position.AsVector2()) < TrapPairReach);

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
        if (MeasureScriptDistance(Player.Position, site.Position.AsVector2()) >= SwitchReach || !Inventory.TryConsumeItem(KnownObject.Dynamite))
            return;

        var charge = Effects.Spawn(KnownObject.ArmedDynamite, site.Position, default, Time);

        if (charge is not null)
        {
            charge.MarkReferenced();
            _charges.Add(charge);
        }

        site.SetState(SpriteAnimation.Death);
    }

    [ScriptFunction("F184_15")]
    private void DetonateDynamite(Unit charge)
    {
        _charges.Remove(charge);
        var center = new Vector2((int)charge.Position.X, (int)charge.Position.Y);

        if (IsInChargeBuckets(Player.Position, center) && MeasureScriptDistance(Player.Position, center) < PlayerChargeReach && Player.IsAlive)
        {
            Player.Hp = 0;
            PlayerHurt?.Invoke(0);
        }

        foreach (var unit in _scripted)
        {
            if (unit.Kind.Id.IsEndlessGenerator && unit.Animation < SpriteAnimation.Death && IsInChargeBuckets(unit.Position, center))
            {
                unit.SetState(SpriteAnimation.Death);
            }
        }

        foreach (var program in _programs)
        {
            if (!IsInChargeBuckets(program.Position, center) || !(program.Kind.Id is KnownObject.Spawner or KnownObject.TeleportPad || program.Kind.Id.IsEndlessGenerator))
                continue;

            program.Stop();
            AnimateFromProgram(program, SpriteAnimation.Death);
        }

        for (int index = 0; index < _sprites.Count; index++)
        {
            var sprite = _sprites[index];

            if (sprite.Id is KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole && !_removed.Contains(sprite) && IsInChargeBuckets(sprite.Position, center))
            {
                CreateSprite(KnownObject.RubblePile, sprite.Position, new Facing(sprite.Facing));
            }
        }

        var reach = new Vector2(ChargeBox + 2 * HashCell);
        Units.Collect(center - reach, center + reach, _blastCandidates);
        foreach (var unit in _blastCandidates)
        {
            if (unit is { IsAlive: true, Army: MonsterArmy } && IsInChargeBuckets(unit.Position, center) &&
                MeasureScriptDistance(charge.Position, unit.Position.AsVector2()) < MonsterChargeReach)
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
        if (_level.LevelNumber == SingleFenceLevel && MeasureScriptDistance(generator.Position, SingleFenceGenerator) < SingleFenceReach)
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
                    unit.SetState(SpriteAnimation.Death);
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

        if (MeasureScriptDistance(Player.Position, door.Position.AsVector2()) < reach && !DoorsLocked)
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
                if (z != -depth || !Units.HasUnitAt(door.Position.AsVector2()) || DoorsLocked)
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
        Movement.SetDoorHeight(door.Placement!, height);

        _events?.RecordDoorPosition(door.Placement!, Time, door.Position);
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
        }

        for (int index = _scripted.Count - 1; index >= 0; index--)
        {
            var unit = _scripted[index];

            if (ReferenceEquals(unit.Placement, sprite))
            {
                _updates.Remove(unit.Kind.Layer, unit);
                _scripted.RemoveAt(index);
            }
        }

        RemoveSprite(sprite);
    }

    [ScriptFunction("CreateSprite")]
    private MergedMapSprite CreateSprite(KnownObject id, Vector3 position, Facing facing)
    {
        var sprite = new MergedMapSprite(0, id, position, facing.Value, 0, []);
        _sprites.Add(sprite);

        if (Kinds.AddTerrain(Movement, sprite))
        {
            TerrainChanged?.Invoke(new TerrainChange(position.AsVector2(), Kinds.Find(id)?.Half ?? Vector2.Zero));
        }

        _events?.RecordSpriteCreation(sprite, Time);

        if (id.IsScripted)
        {
            AddScripted(Kinds.Find(id)!, position, facing, 0, sprite);
        }

        return sprite;
    }

    public bool IsInCannon => _mountedAt >= 0;

    public bool IsProtected => Time < _protectedUntil;

    [ScriptFunction("F357_0")]
    private void MountCannon(Unit cannon)
    {
        if (IsInCannon || !Player.IsAlive || MeasureScriptDistance(Player.Position, cannon.Position.AsVector2()) >= MountReach)
            return;

        Inventory.StowArmor();
        _mountedAt = GetScriptTime();
        Player.Position = cannon.Position;
        Player.Speed = 0;
        PlayerMovement.Reset();
        Player.ChangeKind(Kinds.Find(KnownObject.MannedCannon)!);
        Player.SetFacing(cannon.Facing);
        Player.Hp *= MannedHealthScale;
        Units.Move(Player);
        cannon.SetState(SpriteAnimation.Death);
    }

    // Leaves the cannon on a move order a second after mounting
    private void TryDismount()
    {
        if (!IsInCannon || !_movesPlayer || GetScriptTime() - _mountedAt <= DismountDelay)
            return;

        CreateSprite(KnownObject.MountedCannon, Player.Position, Weapons.Torso.Facing);
        _mountedAt = -1;
        Player.ChangeKind(Kinds.Find(KnownObject.PlayerStart)!);
        Player.Position += DismountStep;
        Player.Hp /= MannedHealthScale;
        Inventory.RestoreArmor();
        PlayerMovement.Reset();
        Units.Move(Player);
    }

    [ScriptFunction("F350_15")]
    private void EjectFromCannon()
    {
        var facing = Weapons.Torso.Facing;
        CreateSprite(KnownObject.MountedCannon, Player.Position, facing);
        _mountedAt = -1;
        float angle = facing.Value * MathF.Tau / Facing.FullTurn;
        int ejectX = (int)(MathF.Sin(angle) * SineScale) / EjectDivisor;
        int ejectY = -(int)(MathF.Cos(angle) * SineScale) / EjectDivisor;
        Player.Position += new Vector3(ejectX, ejectY, 0);
        Player.ChangeKind(Kinds.Find(KnownObject.PlayerStart)!);
        Inventory.RestoreArmor();
        Units.Move(Player);
    }

    private void SwapSprites(KnownObject from, KnownObject to)
    {
        int count = _sprites.Count;
        for (int index = 0; index < count; index++)
        {
            var sprite = _sprites[index];

            if (sprite.Id != from || _removed.Contains(sprite))
                continue;

            CreateSprite(to, sprite.Position, new Facing(sprite.Facing));
            Destroy(sprite);
        }
    }

    private void RemoveSprite(MergedMapSprite sprite)
    {
        if (!_removed.Add(sprite))
            return;

        RemovePlacement(sprite, sprite.Position.AsVector2(), Kinds.Find(sprite.Id)?.Half ?? Vector2.Zero);
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

            if (kind.GetCollisionVolume(sprite.Position).Covers(point) && (found is null || kind.Object.Size.X < foundX || kind.Object.Size.Y < foundY))
            {
                found = sprite;
                foundX = kind.Object.Size.X;
                foundY = kind.Object.Size.Y;
            }
        }

        return found;
    }

    private bool IsPlayerWithin(TriggerArea area) => MeasureScriptDistance(Player.Position, new Vector2(area.Center.X, area.Center.Y)) < area.Radius;

    [EngineRoutine(0x0042dd60)]
    private static bool IsInChargeBuckets(Vector3 position, Vector2 center)
    {
        int x = GetBucket(position.X);
        int y = GetBucket(position.Y);

        return x >= GetBucket(center.X - ChargeBox - HashCell) && x <= GetBucket(center.X + ChargeBox + HashCell) &&
            y >= GetBucket(center.Y - ChargeBox - HashCell) && y <= GetBucket(center.Y + ChargeBox + HashCell);

        static int GetBucket(float value) => (int)MathF.Floor(value / HashCell);
    }

    [ScriptFunction("SizeTo")]
    private static int MeasureScriptDistance(Vector3 position, Vector2 point) =>
        (int)position.AsVector2().MeasureDistance(new Vector2((int)point.X, (int)point.Y));
}
