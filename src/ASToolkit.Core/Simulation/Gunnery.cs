using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Content;

namespace ASToolkit.Core.Simulation;

internal sealed class Gunnery
{
    private const float MaxUnitHalf = 65;
    public static readonly Vector2 ScreenHalf = FightPlanner.Screen.ToVector2() / 2;
    private const float GroundAimHeight = 19;
    private const float WallSlack = 16;
    private const float FlightStep = 8;
    private const int PistolKillTime = 1000;
    private const float CalmReach = 250;

    private readonly GameWorld _world;
    private readonly PlayerState _state;
    private readonly WalkObstacles _obstacles;

    public Gunnery(GameWorld world, WalkObstacles obstacles)
    {
        _obstacles = obstacles;
        _world = world;
        _state = new PlayerState(world);
        Attacks = new AttackTable(world.Definitions);
    }

    public AttackTable Attacks { get; }

    public bool HoldsShortReach()
    {
        for (int slot = 0; slot < Unit.WeaponSlots; slot++)
        {
            var weapon = _state.GetWeapon(slot);

            if (weapon is not null && _state.HasAmmo(slot) && Attacks.Get(weapon).Reach < float.MaxValue)
                return true;
        }

        return false;
    }

    private float GetDistance(Unit target) => _world.Player.Position.AsVector2().MeasureOctagonalDistance(target.Position.AsVector2());

    public int SelectWeapon(Unit target)
    {
        int best = Unit.Pistols;
        float bestRate = 0;
        float pistolRate = 0;
        float distance = GetDistance(target);
        for (int slot = 0; slot < Unit.WeaponSlots; slot++)
        {
            var weapon = _state.GetWeapon(slot);

            if (!_state.HasAmmo(slot) || weapon is null || !weapon.IsArmed ||
                distance > weapon.BattleRange ||
                !HasClearAim(target, _world.Player.Position.AsVector2(), slot))
                continue;

            var profile = Attacks.Get(weapon);
            float radius = profile.BlastRadius;

            if (distance > profile.Reach + radius)
                continue;

            if (profile.HurtsOwnArmy && radius > 0 && distance <= radius + _world.Player.Definition.Half.Length())
                continue;

            if (radius > 0 && !IsInBlastHeight(target, profile))
                continue;

            float chance = MeasureHitChance(target, weapon, profile);
            float harm = profile.BlastRadius > _world.Player.Definition.Half.Length() ? MeasureHarm(target, profile) * chance
                : profile.Rounds > 1 ? MeasureSpreadHarm(target, weapon, profile)
                : Math.Min(target.Hp, profile.Damage * chance);
            float rate = harm * GameClock.MillisecondsPerSecond / Math.Max(_world.FrameTime, weapon.AttackTime);

            if (slot == Unit.Pistols)
            {
                pistolRate = profile.Damage * chance * GameClock.MillisecondsPerSecond / Math.Max(_world.FrameTime, weapon.AttackTime);
            }

            if (rate > bestRate)
            {
                best = slot;
                bestRate = rate;
            }
        }

        return best != Unit.Pistols && pistolRate > 0 && target.Hp * GameClock.MillisecondsPerSecond <= PistolKillTime * pistolRate &&
            IsCalm(target) ? Unit.Pistols : best;
    }

    private bool IsCalm(Unit target)
    {
        var center = _world.Player.Position.AsVector2();
        var reach = new Vector2(CalmReach);
        foreach (var unit in _world.Units.GetNear(center - reach, center + reach))
        {
            if (unit != target && unit is { Army: Army.Monsters, IsAlive: true } && center.MeasureOctagonalDistance(unit.Position.AsVector2()) <= CalmReach)
                return false;
        }

        return true;
    }

    private bool IsInBlastHeight(Unit unit, AttackProfile profile) =>
        unit.Position.Z - _world.Movement.GetHeight(unit.Position.AsVector2()) < profile.BlastHeight + unit.Definition.Object.Size.Z;

    private float MeasureHarm(Unit target, AttackProfile profile)
    {
        var center = target.Position.AsVector2();
        var reach = new Vector2(profile.BlastRadius + MaxUnitHalf);
        float harm = 0;
        foreach (var unit in _world.Units.GetNear(center - reach, center + reach))
        {
            if (unit.Army == target.Army && unit.IsAlive && center.MeasureOctagonalDistance(unit.Position.AsVector2()) <= profile.BlastRadius && IsInBlastHeight(unit, profile))
            {
                harm += Math.Min(profile.Damage, unit.Hp);
            }
        }

        return Math.Max(harm, Math.Min(profile.Damage, target.Hp));
    }

    private float MeasureSpreadHarm(Unit target, SpriteDefinition torso, AttackProfile profile)
    {
        float scatter = Math.Max(1, MeasureScatter(target, torso));
        float roundDamage = (float)profile.Damage / profile.Rounds;
        var center = target.Position.AsVector2();
        var reach = new Vector2(scatter + MaxUnitHalf);
        float harm = 0;
        foreach (var unit in _world.Units.GetNear(center - reach, center + reach))
        {
            float unitReach = (unit.Definition.Half.X + unit.Definition.Half.Y) / 2;

            if (unit.Army != target.Army || !unit.IsAlive || center.MeasureOctagonalDistance(unit.Position.AsVector2()) > scatter + unitReach)
                continue;

            float share = Math.Min(1, unitReach * unitReach / (scatter * scatter));
            harm += Math.Min(unit.Hp, profile.Rounds * share * roundDamage);
        }

        return Math.Max(harm, Math.Min(target.Hp, profile.Damage * MeasureHitChance(target, torso, profile)));
    }

    private float MeasureScatter(Unit target, SpriteDefinition torso)
    {
        float spread = GetSpread(torso, out _);
        float distance = GetDistance(target);
        float overshoot = MathF.Max(0, MathF.Min(spread * MathF.Sqrt(2), torso.BattleRange - distance - 1));

        return spread * distance / Math.Max(1, distance + overshoot);
    }

    // Measures the share of rounds whose scatter across the line of fire stays within the target, the scatter shrunk by the overshoot past it
    private float MeasureHitChance(Unit target, SpriteDefinition torso, AttackProfile profile)
    {
        float spread = GetSpread(torso, out _);

        if (spread <= 0)
            return 1;

        float scatter = MeasureScatter(target, torso);
        float reach = (target.Definition.Half.X + target.Definition.Half.Y) / 2 + profile.BlastRadius;

        return Math.Min(1, reach / Math.Max(1, scatter));
    }

    public Vector3 GetAimPoint(Unit target, int slot)
    {
        var point = target.Position with { Z = target.Position.Z + Math.Min(GroundAimHeight, target.Definition.Object.Size.Z / 2) };
        var torso = _state.GetWeapon(slot);

        if (torso is null || target.Placement is not null)
            return point;

        float spread = GetSpread(torso, out var payload);
        var offset = target.Position.AsVector2() - _world.Player.Position.AsVector2();

        if (payload is null || payload.Damage <= 0 || payload.IsExplosive || spread <= 0 || offset.LengthSquared() < 1)
            return point;

        float room = torso.BattleRange - GetDistance(target);
        float overshoot = MathF.Min(spread * MathF.Sqrt(2), room - 1);

        if (overshoot <= 0)
            return point;

        var ground = target.Position.AsVector2() + Vector2.Normalize(offset) * overshoot;

        return new Vector3(ground, _world.Movement.GetHeight(ground));
    }

    private float GetSpread(SpriteDefinition torso, out SpriteDefinition? payload)
    {
        float spread = torso.Aim;
        payload = _world.Definitions.Find((KnownObject)Math.Abs((int)torso.FightChild));
        while (payload is not null && payload.Damage <= 0 && payload.FightChild != 0)
        {
            spread += payload.Aim;
            payload = _world.Definitions.Find((KnownObject)Math.Abs((int)payload.FightChild));
        }

        return spread;
    }

    public bool HasClearAim(Unit target) => HasClearAim(target, _world.Player.Position.AsVector2());

    public bool HasClearAim(Unit target, Vector2 from, int slot = Unit.Pistols)
    {
        var torso = _state.GetWeapon(slot);
        var launch = Vector3.Zero;
        var bullet = torso is null ? null : FindBullet(torso, out launch);

        if (bullet is null)
        {
            return true;
        }

        var toward = target.Position.AsVector2() - from;
        var forward = toward.LengthSquared() < 1 ? Vector2.Zero : Vector2.Normalize(toward);

        if (launch.Y * launch.Y >= toward.LengthSquared())
            return true;

        var muzzle = from + new Vector2(forward.X * launch.Y, forward.Y * launch.Y * ChildEmission.VerticalProjection);
        float height = _world.Player.Position.Z + _world.Player.Definition.Object.LinkOffset.Z + launch.Z;

        var aim = target.Position with { Z = target.Position.Z + Math.Min(GroundAimHeight, target.Definition.Object.Size.Z / 2) };

        var half = target.Definition.Id is KnownObject.BreakableWall or KnownObject.BreakableWall2 ? target.Definition.Half + new Vector2(WallSlack) : target.Definition.Half;

        var start = new Vector3(muzzle, height);

        return bullet.Speed >= SpriteDefinition.Unlimited ? _world.Projectiles.HasClearTerrain(bullet, start, aim, half) : HasClearFlight(bullet, start, aim, half);
    }

    // Steps a moving round along its line a tick at a time, stopping only where it could not stand
    private bool HasClearFlight(SpriteDefinition bullet, Vector3 start, Vector3 aim, Vector2 targetHalf)
    {
        float length = start.AsVector2().MeasureOctagonalDistance(aim.AsVector2());
        for (float distance = FlightStep; distance < length; distance += FlightStep)
        {
            var point = Vector3.Lerp(start, aim, distance / length);

            if (_obstacles.CanStand(bullet.Object, point))
                continue;
            
            var offset = Vector2.Abs(point.AsVector2() - aim.AsVector2());

            return offset.X < targetHalf.X + bullet.Half.X && offset.Y < targetHalf.Y + bullet.Half.Y;
        }

        return true;
    }

    private SpriteDefinition? FindBullet(SpriteDefinition torso, out Vector3 launch)
    {
        launch = Vector3.Zero;
        var bullet = torso;
        while (bullet is not null && bullet.Object.SpriteClass != SpriteClass.Cannon)
        {
            launch += new Vector3(0, bullet.Object.ChildY[(int)SpriteAnimation.Fight], bullet.Object.ChildZ[(int)SpriteAnimation.Fight]);
            bullet = bullet.FightChild == 0 ? null : _world.Definitions.Find((KnownObject)Math.Abs((int)bullet.FightChild));
        }

        return bullet;
    }

    public bool IsOnScreen(Vector2 point)
    {
        var offset = Vector2.Abs(point - _world.Player.Position.AsVector2());

        return offset.X <= ScreenHalf.X && offset.Y <= ScreenHalf.Y;
    }
}
