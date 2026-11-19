using System.Numerics;
using Microsoft.Extensions.ObjectPool;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

internal sealed class SpawnedObjectSystem
{
    private const int MaxDepth = 12;
    private const int MaxSteeringTurn = 100;
    private const int RocketTurnLimit = 0x47;
    private const int FallTurn = 0x20;
    private const int MaxPooledUnits = 4096;
    private const float ExitMarginX = 220;
    private const float ExitMarginY = 200;
    private const float DeepFall = -100;
    private const float RocketClimbRate = 0.1f;
    private const float RocketHitHeight = 20;
    private const float RocketHitReach = 10;
    private const float RocketCloseReach = 10;
    private const float RocketFarReach = 30;
    private const float MinimumBounceSpeed = 0.022f;
    private const float MinimumLift = 0.005f;
    private const float BounceRetention = 0.5f;
    private const float SteeringReach = 100;

    private readonly List<Unit> _active = [];
    private readonly HashSet<Unit> _orphans = [];
    private readonly List<Unit> _leaving = [];
    private readonly DefaultObjectPool<Unit> _pool = new(new DefaultPooledObjectPolicy<Unit>(), MaxPooledUnits);
    private readonly SpriteCatalog _definitions;
    private readonly MovementMap _map;
    private readonly Random _random;
    private readonly List<EffectState> _settled = [];
    private readonly GameWorld _world;
    private readonly UnitKinematics _motion;

    public SpawnedObjectSystem(SpriteCatalog definitions, MovementMap map, Random random, GameWorld world)
    {
        _definitions = definitions;
        _map = map;
        _random = random;
        _world = world;
        _motion = world.Motion;
    }

    public IReadOnlyList<Unit> Active => _active;

    public IReadOnlyList<EffectState> Settled => _settled;

    private static bool IsLink(Unit unit) => unit.Owner is not null && unit.Owner.Link == unit;

    public void Detach(Unit owner)
    {
        var link = owner.Link;

        if (link is not null && link.Owner == owner && _active.Contains(link))
        {
            _orphans.Add(link);
        }

        owner.Link = null;
    }

    public Unit? Spawn(KnownObject id, Vector3 position, Angle direction, int time, Unit? owner = null, int? lifetime = null)
    {
        var definition = _definitions.Find(id);

        return definition is null ? null : Create(definition, position, direction, time, 0, null, owner, lifetime);
    }

    public void Play(SpriteDefinition definition, SpriteAnimation animation, Vector3 position, Angle direction, int time)
    {
        var unit = _pool.Get();
        unit.Initialize(-1, definition, position, 0, definition.MaxHp, direction);
        unit.CreatedAt = time;
        unit.LastTickTime = time;
        unit.ChangeAnimation(animation);
        _active.Add(unit);
        _world.Register(UpdateRole.Effect, unit);
        _world.Units.Add(unit);
    }

    public void Emit(SpriteDefinition owner, SpriteAnimation animation, Vector3 position, Angle direction, int time, int depth = 0, bool secondPulse = false, Unit? parent = null)
    {
        int childId = owner.Children[(int)animation];

        if (depth >= MaxDepth || childId == 0)
            return;

        var definition = _definitions.Find((KnownObject)Math.Abs(childId));

        if (definition is null)
            return;

        foreach (var spawn in ChildEmission.GetChildren(owner, definition, animation, position, direction, _random, secondPulse))
        {
            if (definition.Object.SpriteClass == SpriteClass.Unit && _world.Projectiles.IsOccupied(definition, spawn.Position))
                continue;

            var childPosition = spawn.Position;

            if (definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ))
            {
                childPosition.Z = _map.GetSupportHeight(definition.Object, childPosition.AsVector2()) + spawn.Position.Z - position.Z;
            }
            var child = Create(definition, childPosition, spawn.Direction, time, depth + 1, parent);

            if (child is not null && definition.Object.SpriteClass == SpriteClass.Linker)
            {
                ConstructLinker(child, parent, spawn.Position, spawn.Direction);
            }

            if (child is not null && IsSettled(child))
            {
                _world.ReleaseSprite(child);
                _pool.Return(child);
            }
        }
    }

    public Unit? Launch(SpriteDefinition definition, Vector3 position, Angle direction, Unit shooter, int time)
    {
        var unit = Create(definition, position, direction, time, 0, shooter);

        unit?.Shooter = shooter;

        return unit;
    }

    [EngineRoutine(0x00445fb0)]
    public void Aim(Unit unit, Unit? goal, Vector3 goalPoint)
    {
        unit.Goal = goal;
        unit.GoalPoint = goal is null ? goalPoint : null;
        _motion.StartMove(unit);
    }

    private Unit? Create(SpriteDefinition definition, Vector3 position, Angle direction, int time, int depth, Unit? parent, Unit? owner = null, int? lifetime = null)
    {
        ref readonly var obj = ref definition.Object;

        if (obj.SpriteClass is SpriteClass.Unit or SpriteClass.Avia && definition.MaxHp > 0)
        {
            _world.AddMonster(definition, position, direction);

            return null;
        }

        var unit = _pool.Get();
        unit.Initialize(-1, definition, position, parent?.Army ?? _definitions.GetArmy(definition), definition.MaxHp);
        unit.CreatedAt = time;
        unit.Depth = depth;
        unit.Owner = owner;
        unit.Shooter = parent?.Shooter ?? parent;
        var state = _world.GetInitialAnimation(definition);
        unit.Start(state, direction, time, _random);
        unit.Lifetime = lifetime ?? obj.Lifetime;

        if (state != SpriteAnimation.Birth && !_world.IsLoading)
        {
            Emit(definition, SpriteAnimation.Birth, position, unit.Direction, time, depth, parent: unit);
        }

        if (obj.SpriteClass == SpriteClass.Cannon)
        {
            ConstructCannon(unit, parent);
        }

        CreateLink(unit, time, depth + 1);

        if (!_world.IsLoading)
        {
            _world.RunCreationHook(unit);
        }

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
    [EngineRoutine(0x00447130)]
    public void CreateLink(Unit owner, int time, int depth)
    {
        ref readonly var obj = ref owner.Definition.Object;
        var definition = obj.Link == 0 || depth > SpriteCatalog.MaxLaunchDepth ? null : _definitions.Find(obj.Link);

        if (definition is null || owner.Link?.Definition == definition)
            return;

        if (definition.Id.IsScripted || definition.Object.WeaponIndex != 0 || definition.Object.SpriteClass == SpriteClass.Creature)
        {
            _world.AttachObjectLink(owner, definition);
        }
        else
        {
            var previous = owner.Link;
            var link = Create(definition, owner.Position + owner.Definition.GetLinkOffset(definition, owner.Direction), owner.Direction, time, depth, owner, owner);
            owner.Link = link;

            if (link is not null && previous is not null && previous != link)
            {
                previous.Owner = null;
                AppendLink(link, previous);
            }
        }
    }

    [EngineRoutine(0x0040ec60)]
    public static void ConstructLinker(Unit linker, Unit? parent, Vector3 position, Angle direction)
    {
        linker.LinkerBase = parent;
        linker.LinkerDirection = direction;

        if (parent is null)
            return;

        AppendLink(parent, linker);
        linker.LinkerOffset = position - parent.Position;
    }

    [EngineRoutine(0x004449c0)]
    public static void AppendLink(Unit parent, Unit link)
    {
        if (link.Owner is not null)
            return;

        var last = parent;
        while (last.Link is not null)
        {
            last = last.Link;
        }

        last.Link = link;
        link.Owner = last;
    }

    private static bool IsSettled(Unit unit)
    {
        ref readonly var obj = ref unit.Definition.Object;

        return unit is { IsMoving: false, VerticalSpeed: 0 } && (obj.Properties & (ObjectProperties.Gravity | ObjectProperties.Gravity2)) == 0 &&
               unit is { Animation: SpriteAnimation.Stand, Lifetime: >= SpriteDefinition.Unlimited } && obj.SpriteClass == SpriteClass.BuiltTerrain;
    }

    [EngineRoutine(0x00447fe0)]
    private void ConstructCannon(Unit unit, Unit? parent)
    {
        ref readonly var obj = ref unit.Definition.Object;

        if (obj.Properties.HasFlag(ObjectProperties.RandomZSpeed) && !unit.Definition.ZSpeed.IsZero())
        {
            bool downward = (_random.Next() & 1) == 0;
            float lift = _random.NextSingle() * unit.Definition.ZSpeed;
            unit.VerticalSpeed = downward ? -lift : lift;
        }
        else if (parent is not null && parent.Animation >= SpriteAnimation.Death && obj.TopZ < 0)
        {
            unit.VerticalSpeed = unit.Definition.ZSpeed;
            unit.Speed = unit.MaxSpeed + parent.Speed;
        }
        else
        {
            unit.VerticalSpeed = unit.Definition.ZSpeed;
        }

        if (obj.Properties.HasFlag(ObjectProperties.RandomSpeed) && !unit.Definition.Speed.IsZero())
        {
            unit.MaxSpeed = _random.NextSingle() * unit.Definition.Speed;
        }

        unit.IsClimbing = true;
        _motion.StartMove(unit);
    }

    public void Forget(Unit removed)
    {
        foreach (var unit in _active)
        {
            if (unit.Definition.Object.SpriteClass == SpriteClass.Cannon)
            {
                for (int built = UnitKinematics.ForgetCannonGoal(unit, removed); built > 0; built--)
                {
                    _world.DrawGoalSpriteRandom();
                }
            }
            else
            {
                if (UnitKinematics.ForgetObject(unit, removed))
                {
                    _world.DrawGoalSpriteRandom();
                }
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

    public bool Tick(Unit unit)
    {
        bool stays = Update(unit);
        _world.Units.ChangeCoor(unit);

        if (stays)
            return true;

        Leave(unit);

        return false;
    }

    public void Leave(Unit unit)
    {
        _world.Units.Remove(unit);
        unit.LeavesEffects = true;
        _leaving.Add(unit);

        if (IsLink(unit))
        {
            unit.Owner!.Link = null;
        }

        foreach (var tail in unit.EnumerateLinks())
        {
            if (tail is { Role: UpdateRole.Effect, LeavesEffects: false })
            {
                _orphans.Add(tail);
            }
        }

        unit.Link = null;

        _world.ReleaseSprite(unit);
    }

    private bool Update(Unit unit)
    {
        if (unit.Owner is not null && _orphans.Remove(unit))
            return false;

        return _world.Tact(unit);
    }

    private static float KeepHover(Unit unit, float height, float ground)
    {
        ref readonly var obj = ref unit.Definition.Object;
        float hover = ground + obj.TopZ;
        float z = unit.Position.Z;

        if (z.IsNear(height) || obj.Properties.HasFlag(ObjectProperties.Gravity) || hover.IsZero())
            return height;

        if (hover > z ? hover <= height : hover < z ? height < hover : !obj.Properties.HasFlag(ObjectProperties.SelfMoving))
        {
            unit.VerticalSpeed = 0;

            return hover > z || hover < z ? hover : height;
        }

        return height;
    }

    private static Vector3 KeepGround(Vector3 next, Unit unit) => next with { X = unit.Position.X, Y = unit.Position.Y };

    [EngineRoutine(0x0043f820)]
    [EngineRoutine(0x00448130)]
    public void Move(Unit unit, int elapsed)
    {
        if (!NeedsMotion(unit.Definition))
            return;

        if (unit.Definition.Object.SpriteClass == SpriteClass.Cannon)
        {
            RunCannon(unit, elapsed);
        }
        else
        {
            RunGeneric(unit, elapsed);
        }
    }

    private static bool NeedsMotion(SpriteDefinition definition)
    {
        ref readonly var obj = ref definition.Object;

        return !definition.Speed.IsZero() || !definition.ZSpeed.IsZero() || (obj.Properties & (ObjectProperties.Gravity | ObjectProperties.Gravity2 | ObjectProperties.Wind)) != 0;
    }

    [EngineRoutine(0x0043f820)]
    private void RunGeneric(Unit unit, int elapsed)
    {
        ref readonly var obj = ref unit.Definition.Object;
        var next = _motion.Move(unit, elapsed);
        float current = _map.GetHeight(unit.Position.AsVector2());
        float ground = _map.GetHeight(next.AsVector2());

        if (obj.SpriteType.HasFlag(SpriteType.Cannon) && obj.Properties.HasFlag(ObjectProperties.Gravity) && ground >= next.Z && unit.Position.Z >= current)
        {
            next.Z = unit.Position.Z;
        }
        else
        {
            next.Z = KeepHover(unit, next.Z, ground);
        }

        if (unit.HasGoal && !unit.Speed.IsZero() && unit is { HasReachedX: true, HasReachedY: true })
        {
            UnitKinematics.StopMoving(unit);
        }

        if (!next.X.IsNear(unit.Position.X) || !next.Y.IsNear(unit.Position.Y))
        {
            if (_world.Projectiles.IsBlockedAt(unit.Definition, next, unit))
            {
                unit.VerticalSpeed = 0;
                unit.Speed = 0;
                unit.IsBlocked = true;
                next = KeepGround(next, unit);
            }
        }

        unit.ChangeCoor(next);
    }

    [EngineRoutine(0x00448130)]
    private void RunCannon(Unit unit, int elapsed)
    {
        ref readonly var obj = ref unit.Definition.Object;
        var next = _motion.Move(unit, elapsed);

        if (unit is { IsBlocked: true, Animation: < SpriteAnimation.Death })
        {
            unit.ChangeAnimation(SpriteAnimation.Death);
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
            if (_world.Projectiles.IsBlockedAt(unit.Definition, next, unit))
            {
                if (unit.Animation < SpriteAnimation.Death)
                {
                    unit.ChangeAnimation(SpriteAnimation.Death);
                }

                next = KeepGround(next, unit);
            }
            else if (unit.Animation < SpriteAnimation.Death && !obj.Properties.HasFlag(ObjectProperties.Gravity) && IsFarOutside(next.AsVector2()))
            {
                unit.ChangeAnimation(SpriteAnimation.Death);
            }
        }

        float previousHeight = unit.Position.Z;
        unit.ChangeCoor(next);
        Steer(unit, elapsed, previousHeight);
    }

    private void Land(Unit unit, ref Vector3 next, float current, float ground)
    {
        ref readonly var obj = ref unit.Definition.Object;
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

        if (!obj.Properties.HasFlag(ObjectProperties.Bouncing))
        {
            UnitKinematics.StopMoving(unit);
            next.Z = current;
            _world.EmitChildrenAs(unit, climbs ? SpriteAnimation.Load : SpriteAnimation.Unload);
            unit.ChangeAnimation(SpriteAnimation.Death);
            next = KeepGround(next, unit);

            return;
        }

        if (climbs)
        {
            unit.ChangeAnimation(SpriteAnimation.Load);
            unit.SetDirection(unit.Direction.Opposite);
        }
        else if (unit.VerticalSpeed < -MinimumBounceSpeed)
        {
            unit.ChangeAnimation(SpriteAnimation.Unload);
            unit.VerticalSpeed *= -BounceRetention;
        }
        else if (unit.VerticalSpeed <= MinimumLift)
        {
            unit.VerticalSpeed = 0;
            unit.ChangeAnimation(SpriteAnimation.Death);
        }
        else
        {
            unit.VerticalSpeed *= BounceRetention;
            unit.Speed *= BounceRetention;
            unit.SetDirection(unit.Direction.Opposite);
        }

        next = KeepGround(next, unit);
    }

    private void Steer(Unit unit, int elapsed, float previousHeight)
    {
        ref readonly var obj = ref unit.Definition.Object;

        if (!unit.HasGoal)
        {
            if (obj.TopZ < 0 && unit.VerticalSpeed < 0)
            {
                unit.SetDirection(unit.Direction.TurnToward(unit.Direction + FallTurn, obj.RotationPeriod, elapsed, out _));
            }

            return;
        }

        var goal = unit.GoalPosition;
        var target = unit.Goal;
        float distance = unit.Position.AsVector2().MeasureOctagonalDistance(goal.AsVector2());
        var aim = Angle.FromOffset((goal - unit.Position).AsVector2()) + (unit.Speed < 0 ? Angle.HalfTurn : 0);

        if (!obj.Properties.HasFlag(ObjectProperties.SelfMoving))
        {
            if (distance > SteeringReach)
            {
                var before = unit.Direction;
                unit.SetDirection(unit.Direction.TurnToward(aim, obj.RotationPeriod, elapsed, out _));

                if (before.MeasureTurnTo(unit.Direction) > MaxSteeringTurn)
                {
                    UnitKinematics.StopMoving(unit);
                    unit.ChangeAnimation(SpriteAnimation.Death);
                }
            }
        }
        else if (!unit.IsClimbing)
        {
            unit.SetDirection(unit.Direction.TurnToward(aim, obj.RotationPeriod, elapsed, out int remaining));

            if (distance <= RocketCloseReach || distance >= RocketFarReach || (byte)remaining < RocketTurnLimit)
            {
                unit.VerticalSpeed = unit.Position.Z <= goal.Z || distance.IsZero() ? 0
                    : Math.Max((goal.Z - unit.Position.Z) / distance * RocketClimbRate, -unit.Definition.ZSpeed);
            }
            else
            {
                unit.IsClimbing = true;
                _motion.StartMove(unit);
            }
        }
        else if (_map.GetHeight(unit.Position.AsVector2()) + obj.TopZ <= unit.Position.Z && goal.Z <= unit.Position.Z || unit.VerticalSpeed <= 0)
        {
            unit.IsClimbing = false;
        }
        else
        {
            unit.VerticalSpeed -= elapsed * _definitions.Gravitation;
        }

        if (HitsGoal(unit, goal, target, previousHeight))
        {
            UnitKinematics.StopMoving(unit);
            unit.ChangeAnimation(SpriteAnimation.Death);
        }
    }

    private static bool HitsGoal(Unit unit, Vector3 goal, Unit? target, float previousHeight)
    {
        var half = unit.Definition.Half;
        var goalHalf = target?.Definition.Half ?? PlayerMovement.MoveTargetHalfSize;
        var offset = Vector2.Abs(unit.Position.AsVector2() - goal.AsVector2());

        if (unit is { HasReachedX: true, HasReachedY: true } || offset.X < half.X + goalHalf.X && offset.Y < half.Y + goalHalf.Y)
        {
            float goalHeight = target?.Definition.Object.Size.Z ?? PlayerMovement.TargetHeight;
            float z = unit.Position.Z;

            if (goal.Z <= unit.Definition.Object.Size.Z + z && z <= goal.Z + goalHeight ||
                Math.Min(previousHeight, z) <= goal.Z && goal.Z <= Math.Max(previousHeight, z))
                return true;
        }

        return unit.Definition.Object.Properties.HasFlag(ObjectProperties.SelfMoving) && MathF.Abs(goal.Z - unit.Position.Z) < RocketHitHeight &&
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
            unit.ChangeAnimation(SpriteAnimation.Death);
        }
        else if (state < SpriteAnimation.Death)
        {
            if (unit.Position.Z < DeepFall)
            {
                unit.ChangeAnimation(SpriteAnimation.Death2);
            }
            else if (!unit.Speed.IsZero())
            {
                unit.ChangeAnimation(SpriteAnimation.Go);
            }
            else if (state > SpriteAnimation.Open && state != SpriteAnimation.StandOpen)
            {
                unit.ChangeAnimation(SpriteAnimation.Stand);
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
                unit.ChangeAnimation(SpriteAnimation.Stand);
            }
        }
        else if (state != SpriteAnimation.Go)
        {
            unit.ChangeAnimation(SpriteAnimation.Go);
        }
    }

    public static EffectState GetState(Unit unit) =>
        new(unit.Definition.Id, unit.Position, unit.Direction, unit.Animation, unit.CreatedAt, unit.Frame, unit.KeyframePosition);
}
