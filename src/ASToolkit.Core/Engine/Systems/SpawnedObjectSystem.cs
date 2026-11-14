using System.Numerics;
using Microsoft.Extensions.ObjectPool;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class SpawnedObjectSystem
{
    private const int MaxDepth = 12;
    private const float MinimumBounceSpeed = 0.022f;
    private const float MinimumLift = 0.005f;
    private const float BounceRetention = 0.5f;
    private const float SteeringReach = 100;
    private const int MaxSteeringTurn = 100;
    private const float RocketCloseReach = 10;
    private const float RocketFarReach = 30;
    private const int RocketTurnLimit = 0x47;
    private const float RocketClimbRate = 0.1f;
    private const float RocketHitHeight = 20;
    private const float RocketHitReach = 10;
    private const int FallTurn = 0x20;
    private const float ExitMarginX = 220;
    private const float ExitMarginY = 200;
    private const float DeepFall = -100;
    private const int MaxPooledUnits = 4096;

    private readonly List<Unit> _active = [];
    private readonly HashSet<Unit> _orphans = [];
    private readonly List<Unit> _leaving = [];
    private readonly DefaultObjectPool<Unit> _pool = new(new UnitPoolPolicy(), MaxPooledUnits);
    private readonly ObjectCatalog _kinds;
    private readonly MovementMap _map;
    private readonly Random _random;
    private readonly List<EffectState> _settled = [];
    private readonly GameWorld _world;
    private readonly UnitKinematics _motion;

    public SpawnedObjectSystem(ObjectCatalog kinds, MovementMap map, Random random, GameWorld world)
    {
        _kinds = kinds;
        _map = map;
        _random = random;
        _world = world;
        _motion = world.Motion;
    }

    public IReadOnlyList<Unit> Active => _active;

    public void AddStates(List<EffectState> states)
    {
        foreach (var unit in _active)
        {
            if (!IsLink(unit))
            {
                states.Add(GetState(unit));
            }
        }
    }

    public IReadOnlyList<EffectState> Settled => _settled;

    public EffectState[] GetStates() => _active.Where(unit => !IsLink(unit)).Select(GetState).ToArray();

    private static bool IsLink(Unit unit) => unit.Owner is not null && unit.Owner.Link == unit;

    [EngineRoutine(0x00445770)]
    public void Attach(Unit owner, ObjectDefinition kind, int time)
    {
        var position = owner.Position + owner.Kind.Object.GetLinkOffset(kind.Object, null, 0, owner.Facing.Value);
        owner.Link = Create(kind, position, owner.Facing, time, 0, owner, owner);
    }

    public void Detach(Unit owner)
    {
        var link = owner.Link;

        if (link is not null && link.Owner == owner && _active.Contains(link))
        {
            _orphans.Add(link);
        }

        owner.Link = null;
    }

    public Unit? Spawn(KnownObject id, Vector3 position, Facing facing, int time, Unit? owner = null, int? lifetime = null)
    {
        var kind = _kinds.Find(id);

        return kind is null ? null : Create(kind, position, facing, time, 0, null, owner, lifetime);
    }

    public void Play(ObjectDefinition kind, SpriteAnimation animation, Vector3 position, Facing facing, int time)
    {
        var unit = _pool.Get();
        unit.Initialize(-1, kind, position, 0, kind.MaxHp, facing);
        unit.CreatedAt = time;
        unit.LastTickTime = time;
        unit.SetState(animation);
        _active.Add(unit);
        _world.Register(UpdateRole.Effect, unit);
        _world.Units.Add(unit);
    }

    public void Emit(ObjectDefinition owner, SpriteAnimation animation, Vector3 position, Facing facing, int time, int depth = 0, bool secondPulse = false, Unit? parent = null)
    {
        int childId = owner.Object.Children[(int)animation];

        if (depth >= MaxDepth || childId == 0)
            return;

        var kind = _kinds.Find((KnownObject)Math.Abs(childId));

        if (kind is null)
            return;

        foreach (var spawn in ChildEmission.GetChildren(owner, kind, animation, position, facing, _random, secondPulse))
        {
            if (kind.Object.SpriteClass == SpriteClass.Unit && _world.Projectiles.IsOccupied(kind, spawn.Position))
                continue;

            var childPosition = spawn.Position;

            if (kind.Object.Flags.HasFlag(ObjectFlags.FollowsTerrain))
            {
                childPosition.Z = _map.GetSupportHeight(kind.Object, childPosition.AsVector2()) + spawn.Position.Z - position.Z;
            }
            var child = Create(kind, childPosition, spawn.Facing, time, depth + 1, parent);

            if (child is not null && IsSettled(child))
            {
                _pool.Return(child);
            }
        }
    }

    public Unit? Launch(ObjectDefinition kind, Vector3 position, Facing facing, Unit shooter, int time)
    {
        var unit = Create(kind, position, facing, time, 0, shooter);

        unit?.Shooter = shooter;

        return unit;
    }

    [EngineRoutine(0x00445fb0)]
    public void Aim(Unit unit, Unit? goal, Vector3 goalPoint)
    {
        unit.Goal = goal;
        unit.GoalPoint = goal is null ? goalPoint : null;
        _motion.StartTowardGoal(unit);
    }

    private Unit? Create(ObjectDefinition kind, Vector3 position, Facing facing, int time, int depth, Unit? parent, Unit? owner = null, int? lifetime = null)
    {
        ref readonly var obj = ref kind.Object;

        if (obj.SpriteClass is SpriteClass.Unit or SpriteClass.Avia && kind.MaxHp > 0)
        {
            _world.AddMonster(kind, position, facing);

            return null;
        }

        var unit = _pool.Get();
        unit.Initialize(-1, kind, position, parent?.Army ?? _kinds.GetArmy(kind), kind.MaxHp);
        unit.CreatedAt = time;
        unit.Depth = depth;
        unit.Owner = owner;
        unit.OwnerOffset = owner is null ? Vector3.Zero : position - owner.Position;
        unit.Shooter = parent?.Shooter ?? parent;
        var state = kind.InitialAnimation;
        unit.Start(state, facing, time, _random);
        unit.Lifetime = lifetime ?? obj.Lifetime;

        if (state != SpriteAnimation.Birth)
        {
            Emit(kind, SpriteAnimation.Birth, position, unit.Facing, time, depth, parent: unit);
        }

        if (obj.SpriteClass == SpriteClass.Cannon)
        {
            ConstructCannon(unit, parent);
        }

        CreateLink(unit, time, depth);
        _world.RunCreationHook(unit);

        if (IsSettled(unit))
        {
            Detach(unit);
            _settled.Add(GetState(unit));

            return unit;
        }

        _active.Add(unit);
        _world.Register(UpdateRole.Effect, unit);
        _world.Units.Add(unit);

        return unit;
    }

    [EngineRoutine(0x00445770)]
    private void CreateLink(Unit unit, int time, int depth)
    {
        ref readonly var obj = ref unit.Kind.Object;
        var link = obj.Link == 0 || depth >= ObjectCatalog.MaxLaunchDepth ? null : _kinds.Find(obj.Link);

        if (link is not null)
        {
            unit.Link = Create(link, unit.Position + obj.GetLinkOffset(link.Object, null, 0, unit.Facing.Value), unit.Facing, time, depth + 1, unit, unit);
        }
    }

    private static bool IsSettled(Unit unit)
    {
        ref readonly var obj = ref unit.Kind.Object;

        return unit is { Moving: false, VerticalSpeed: 0 } && (obj.Flags & (ObjectFlags.Flying | ObjectFlags.Heavy)) == 0 &&
               unit is { Animation: SpriteAnimation.Stand, Lifetime: >= ObjectDefinition.Unlimited } && obj.SpriteClass == SpriteClass.Scenery;
    }

    [EngineRoutine(0x00447fe0)]
    private void ConstructCannon(Unit unit, Unit? parent)
    {
        ref readonly var obj = ref unit.Kind.Object;

        if (obj.Flags.HasFlag(ObjectFlags.RandomLift) && !unit.Kind.ZSpeed.IsZero())
        {
            bool downward = (_random.Next() & 1) == 0;
            float lift = _random.NextSingle() * unit.Kind.ZSpeed;
            unit.VerticalSpeed = downward ? -lift : lift;
        }
        else if (parent is not null && parent.Animation >= SpriteAnimation.Death && obj.RestHeight < 0)
        {
            unit.VerticalSpeed = unit.Kind.ZSpeed;
            unit.Speed = unit.MaxSpeed + parent.Speed;
        }
        else
        {
            unit.VerticalSpeed = unit.Kind.ZSpeed;
        }

        if (obj.Flags.HasFlag(ObjectFlags.RandomSpeed) && !unit.Kind.Speed.IsZero())
        {
            unit.MaxSpeed = _random.NextSingle() * unit.Kind.Speed;
        }

        unit.Climbing = true;
        _motion.StartTowardGoal(unit);
    }

    public void Forget(Unit removed)
    {
        foreach (var unit in _active)
        {
            if (unit.Kind.Object.SpriteClass == SpriteClass.Cannon)
            {
                UnitKinematics.ForgetCannonGoal(unit, removed);
            }
            else
            {
                UnitKinematics.ForgetObject(unit, removed);
            }
        }
    }

    public void Compact()
    {
        if (_leaving.Count == 0)
            return;

        _active.RemoveAll(unit => unit.LeavesEffects);
        foreach (var unit in _leaving)
        {
            unit.LeavesEffects = false;
            _pool.Return(unit);
        }

        _leaving.Clear();
    }

    public bool Tick(Unit unit, int elapsed)
    {
        bool stays = Update(unit, elapsed);
        _world.Units.Move(unit);

        if (stays)
            return true;

        _world.Units.Remove(unit);
        unit.LeavesEffects = true;
        _leaving.Add(unit);

        if (IsLink(unit))
        {
            unit.Owner!.Link = null;
        }

        if (unit.Kind.Object.Link != 0)
        {
            foreach (var other in _active)
            {
                if (other.Owner == unit && other.Kind.Id == unit.Kind.Object.Link && !other.LeavesEffects)
                {
                    _orphans.Add(other);
                }
            }
        }

        return false;
    }

    private bool Update(Unit unit, int elapsed)
    {
        if (unit.Owner is not null)
        {
            if (_orphans.Remove(unit))
                return false;

            var owner = unit.Owner;

            if (owner.Link == unit)
            {
                unit.Position = owner.Position + owner.Kind.Object.GetLinkOffset(unit.Kind.Object, null, 0, owner.Facing.Value);
            }
            else
            {
                unit.Position = owner.Position + unit.OwnerOffset;
                unit.SetFacing(owner.Facing);
            }
        }
        else if (NeedsMotion(unit.Kind))
        {
            if (unit.Kind.Object.SpriteClass == SpriteClass.Cannon)
            {
                RunCannon(unit, elapsed);
            }
            else
            {
                RunGeneric(unit, elapsed);
            }
        }

        return _world.TickUnit(unit);
    }

    private static float KeepHover(Unit unit, float height, float ground)
    {
        ref readonly var obj = ref unit.Kind.Object;
        float hover = ground + obj.RestHeight;
        float z = unit.Position.Z;

        if (z.IsNear(height) || obj.Flags.HasFlag(ObjectFlags.Flying) || hover.IsZero())
            return height;

        if (hover > z ? hover <= height : hover < z ? height < hover : !obj.Flags.HasFlag(ObjectFlags.Rocket))
        {
            unit.VerticalSpeed = 0;

            return hover > z || hover < z ? hover : height;
        }

        return height;
    }

    private static Vector3 KeepGround(Vector3 next, Unit unit) => next with { X = unit.Position.X, Y = unit.Position.Y };

    private static bool NeedsMotion(ObjectDefinition kind)
    {
        ref readonly var obj = ref kind.Object;

        return !kind.Speed.IsZero() || !kind.ZSpeed.IsZero() || (obj.Flags & (ObjectFlags.Flying | ObjectFlags.Heavy | ObjectFlags.Windblown)) != 0;
    }

    [EngineRoutine(0x0043f820)]
    private void RunGeneric(Unit unit, int elapsed)
    {
        ref readonly var obj = ref unit.Kind.Object;
        var next = _motion.Integrate(unit, elapsed);
        float current = _map.GetHeight(unit.Position.AsVector2());
        float ground = _map.GetHeight(next.AsVector2());

        if (obj.SpriteType.HasFlag(SpriteType.Cannon) && obj.Flags.HasFlag(ObjectFlags.Flying) && ground >= next.Z && unit.Position.Z >= current)
        {
            next.Z = unit.Position.Z;
        }
        else
        {
            next.Z = KeepHover(unit, next.Z, ground);
        }

        if (unit.HasGoal && !unit.Speed.IsZero() && unit is { ReachedX: true, ReachedY: true })
        {
            UnitKinematics.StopMoving(unit);
        }

        if (!next.X.IsNear(unit.Position.X) || !next.Y.IsNear(unit.Position.Y))
        {
            if (_world.Projectiles.IsBlockedAt(unit.Kind, next, unit))
            {
                unit.VerticalSpeed = 0;
                unit.Speed = 0;
                unit.Blocked = true;
                next = KeepGround(next, unit);
            }
        }

        unit.Position = next;
    }

    [EngineRoutine(0x00448130)]
    private void RunCannon(Unit unit, int elapsed)
    {
        ref readonly var obj = ref unit.Kind.Object;
        var next = _motion.Integrate(unit, elapsed);

        if (unit is { Blocked: true, Animation: < SpriteAnimation.Death })
        {
            unit.SetState(SpriteAnimation.Death);
        }

        float current = _map.GetHeight(unit.Position.AsVector2());
        float ground = _map.GetHeight(next.AsVector2());

        if (ground < next.Z || unit.Position.Z < current)
        {
            next.Z = KeepHover(unit, next.Z, ground);
        }
        else
        {
            next.Z = unit.Position.Z;
            Land(unit, ref next, current, ground);
        }

        if (!next.X.IsNear(unit.Position.X) || !next.Y.IsNear(unit.Position.Y))
        {
            if (_world.Projectiles.IsBlockedAt(unit.Kind, next, unit))
            {
                if (unit.Animation < SpriteAnimation.Death)
                {
                    unit.SetState(SpriteAnimation.Death);
                }

                next = KeepGround(next, unit);
            }
            else if (unit.Animation < SpriteAnimation.Death && !obj.Flags.HasFlag(ObjectFlags.Flying) && IsFarOutside(next.AsVector2()))
            {
                unit.SetState(SpriteAnimation.Death);
            }
        }

        float previousHeight = unit.Position.Z;
        unit.Position = next;
        Steer(unit, elapsed, previousHeight);
    }

    private void Land(Unit unit, ref Vector3 next, float current, float ground)
    {
        ref readonly var obj = ref unit.Kind.Object;
        bool climbs = ground > current;

        if (unit.Animation >= SpriteAnimation.Death)
        {
            UnitKinematics.StopMoving(unit);

            if (climbs)
            {
                _world.EmitChildrenAs(unit, SpriteAnimation.Load);
            }

            next = KeepGround(next, unit);

            return;
        }

        if (!obj.Flags.HasFlag(ObjectFlags.Bouncing))
        {
            UnitKinematics.StopMoving(unit);
            next.Z = current;
            _world.EmitChildrenAs(unit, climbs ? SpriteAnimation.Load : SpriteAnimation.Unload);
            unit.SetState(SpriteAnimation.Death);
            next = KeepGround(next, unit);

            return;
        }

        if (climbs)
        {
            unit.SetState(SpriteAnimation.Load);
            unit.SetFacing(unit.Facing.Opposite);
        }
        else if (unit.VerticalSpeed < -MinimumBounceSpeed)
        {
            unit.SetState(SpriteAnimation.Unload);
            unit.VerticalSpeed *= -BounceRetention;
        }
        else if (unit.VerticalSpeed <= MinimumLift)
        {
            unit.VerticalSpeed = 0;
            unit.SetState(SpriteAnimation.Death);
        }
        else
        {
            unit.VerticalSpeed *= BounceRetention;
            unit.Speed *= BounceRetention;
            unit.SetFacing(unit.Facing.Opposite);
        }

        next = KeepGround(next, unit);
    }

    private void Steer(Unit unit, int elapsed, float previousHeight)
    {
        ref readonly var obj = ref unit.Kind.Object;

        if (!unit.HasGoal)
        {
            if (obj.RestHeight < 0 && unit.VerticalSpeed < 0)
            {
                unit.SetFacing(unit.Facing.TurnToward(unit.Facing + FallTurn, obj.TurnPeriod, elapsed, out _));
            }

            return;
        }

        var goal = unit.GoalPosition;
        var target = unit.Goal;
        float distance = unit.Position.AsVector2().MeasureDistance(goal.AsVector2());
        var aim = Facing.FromOffset((goal - unit.Position).AsVector2()) + (unit.Speed < 0 ? Facing.HalfTurn : 0);

        if (!obj.Flags.HasFlag(ObjectFlags.Rocket))
        {
            if (distance > SteeringReach)
            {
                var before = unit.Facing;
                unit.SetFacing(unit.Facing.TurnToward(aim, obj.TurnPeriod, elapsed, out _));

                if (before.MeasureTurnTo(unit.Facing) > MaxSteeringTurn)
                {
                    UnitKinematics.StopMoving(unit);
                    unit.SetState(SpriteAnimation.Death);
                }
            }
        }
        else if (!unit.Climbing)
        {
            unit.SetFacing(unit.Facing.TurnToward(aim, obj.TurnPeriod, elapsed, out int remaining));

            if (distance <= RocketCloseReach || distance >= RocketFarReach || (byte)remaining < RocketTurnLimit)
            {
                unit.VerticalSpeed = unit.Position.Z <= goal.Z || distance.IsZero() ? 0
                    : Math.Max((goal.Z - unit.Position.Z) / distance * RocketClimbRate, -unit.Kind.ZSpeed);
            }
            else
            {
                unit.Climbing = true;
                _motion.StartTowardGoal(unit);
            }
        }
        else if (_map.GetHeight(unit.Position.AsVector2()) + obj.RestHeight <= unit.Position.Z && goal.Z <= unit.Position.Z || unit.VerticalSpeed <= 0)
        {
            unit.Climbing = false;
        }
        else
        {
            unit.VerticalSpeed = unit.VerticalSpeed - elapsed * _kinds.Gravity;
        }

        if (HitsGoal(unit, goal, target, previousHeight))
        {
            UnitKinematics.StopMoving(unit);
            unit.SetState(SpriteAnimation.Death);
        }
    }

    private static bool HitsGoal(Unit unit, Vector3 goal, Unit? target, float previousHeight)
    {
        var half = unit.Kind.Half;
        var goalHalf = target?.Kind.Half ?? PlayerMovement.TargetHalf;
        var offset = Vector2.Abs(unit.Position.AsVector2() - goal.AsVector2());

        if (unit is { ReachedX: true, ReachedY: true } || offset.X < half.X + goalHalf.X && offset.Y < half.Y + goalHalf.Y)
        {
            float goalHeight = target?.Kind.Object.Size.Z ?? PlayerMovement.TargetHeight;
            float z = unit.Position.Z;

            if (goal.Z <= unit.Kind.Object.Size.Z + z && z <= goal.Z + goalHeight ||
                Math.Min(previousHeight, z) <= goal.Z && goal.Z <= Math.Max(previousHeight, z))
                return true;
        }

        return unit.Kind.Object.Flags.HasFlag(ObjectFlags.Rocket) && MathF.Abs(goal.Z - unit.Position.Z) < RocketHitHeight &&
               offset is { X: < RocketHitReach, Y: < RocketHitReach };
    }

    private bool IsFarOutside(Vector2 point) =>
        point.X < -ExitMarginX || point.Y < -ExitMarginY ||
        point.X > _map.MapSize.Width + ExitMarginX || point.Y > _map.MapSize.Height + ExitMarginY;

    [EngineRoutine(0x00448950)]
    public static void DecideCannon(Unit unit)
    {
        var state = unit.Animation;

        if (state == SpriteAnimation.Fight)
        {
            unit.SetState(SpriteAnimation.Death);
        }
        else if (state < SpriteAnimation.Death)
        {
            if (unit.Position.Z < DeepFall)
            {
                unit.SetState(SpriteAnimation.Death2);
            }
            else if (!unit.Speed.IsZero())
            {
                unit.SetState(SpriteAnimation.Go);
            }
            else if (state > SpriteAnimation.Open && state != SpriteAnimation.StandOpen)
            {
                unit.SetState(SpriteAnimation.Stand);
            }
        }
    }

    [EngineRoutine(0x00440830)]
    public static void DecideObject(Unit unit)
    {
        var state = unit.Animation;

        if (state >= SpriteAnimation.Death)
            return;

        if (unit.Speed.IsZero())
        {
            if (state > SpriteAnimation.RRotate && state != SpriteAnimation.StandOpen)
            {
                unit.SetState(SpriteAnimation.Stand);
            }
        }
        else if (state != SpriteAnimation.Go)
        {
            unit.SetState(SpriteAnimation.Go);
        }
    }

    public static EffectState GetState(Unit unit) =>
        new(unit.Kind.Id, unit.Position, unit.Facing.Value, unit.Animation, unit.CreatedAt, unit.Frame, unit.KeyframePosition);
}
