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
    private const int MaxPooledSprites = 4096;
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

    private readonly List<Sprite> _active = [];
    private readonly HashSet<Sprite> _orphans = [];
    private readonly List<Sprite> _leaving = [];
    private readonly DefaultObjectPool<Sprite> _pool = new(new DefaultPooledObjectPolicy<Sprite>(), MaxPooledSprites);
    private readonly SpriteCatalog _definitions;
    private readonly MovementMap _movement;
    private readonly Random _random;
    private readonly List<EffectState> _settled = [];
    private readonly Map _map;
    private readonly SpriteKinematics _motion;

    public SpawnedObjectSystem(SpriteCatalog definitions, MovementMap movement, Random random, Map map)
    {
        _definitions = definitions;
        _movement = movement;
        _random = random;
        _map = map;
        _motion = map.Motion;
    }

    public IReadOnlyList<Sprite> Active => _active;

    public IReadOnlyList<EffectState> Settled => _settled;

    private static bool IsLink(Sprite sprite) => sprite.Owner is not null && sprite.Owner.Link == sprite;

    public void Detach(Sprite owner)
    {
        var link = owner.Link;

        if (link is not null && link.Owner == owner && _active.Contains(link))
        {
            _orphans.Add(link);
        }

        owner.Link = null;
    }

    public Sprite? Spawn(KnownObject id, Vector3 position, Angle direction, int time, Sprite? owner = null, int? lifetime = null)
    {
        var definition = _definitions.Find(id);

        return definition is null ? null : Create(definition, position, direction, time, 0, null, owner, lifetime);
    }

    public void Play(SpriteDefinition definition, SpriteAnimation animation, Vector3 position, Angle direction, int time)
    {
        var sprite = _pool.Get();
        sprite.Initialize(-1, definition, position, 0, definition.MaxHp, direction);
        sprite.CreatedAt = time;
        sprite.LastTickTime = time;
        sprite.ChangeAnimation(animation);
        _active.Add(sprite);
        _map.Register(UpdateRole.Effect, sprite);
        _map.Collector.InsertToHash(sprite);
    }

    public void Emit(SpriteDefinition owner, SpriteAnimation animation, Vector3 position, Angle direction, int time, int depth = 0, bool secondPulse = false, Sprite? parent = null)
    {
        int childId = owner.Children[(int)animation];

        if (depth >= MaxDepth || childId == 0)
            return;

        var definition = _definitions.Find((KnownObject)Math.Abs(childId));

        if (definition is null)
            return;

        foreach (var spawn in ChildEmission.GetChildren(owner, definition, animation, position, direction, _random, secondPulse))
        {
            if (definition.Object.SpriteClass == SpriteClass.Unit && _map.Projectiles.IsOccupied(definition, spawn.Position))
                continue;

            var childPosition = spawn.Position;

            if (definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ))
            {
                childPosition.Z = _movement.GetSupportHeight(definition.Object, childPosition.AsVector2()) + spawn.Position.Z - position.Z;
            }
            var child = Create(definition, childPosition, spawn.Direction, time, depth + 1, parent);

            if (child is not null && definition.Object.SpriteClass == SpriteClass.Linker)
            {
                ConstructLinker(child, parent, spawn.Position, spawn.Direction);
            }

            if (child is not null && IsSettled(child))
            {
                _map.ReleaseSprite(child);
                _pool.Return(child);
            }
        }
    }

    public Sprite? Launch(SpriteDefinition definition, Vector3 position, Angle direction, Sprite shooter, int time)
    {
        var sprite = Create(definition, position, direction, time, 0, shooter);

        sprite?.Shooter = shooter;

        return sprite;
    }

    [EngineRoutine(0x00445fb0)]
    public void Aim(Sprite sprite, Sprite? goal, Vector3 goalPoint)
    {
        sprite.Goal = goal;
        sprite.GoalPoint = goal is null ? goalPoint : null;
        _motion.StartMove(sprite);
    }

    private Sprite? Create(SpriteDefinition definition, Vector3 position, Angle direction, int time, int depth, Sprite? parent, Sprite? owner = null, int? lifetime = null)
    {
        ref readonly var obj = ref definition.Object;

        if (obj.SpriteClass is SpriteClass.Unit or SpriteClass.Avia && definition.MaxHp > 0)
        {
            _map.AddMonster(definition, position, direction);

            return null;
        }

        var sprite = _pool.Get();
        sprite.Initialize(-1, definition, position, parent?.Army ?? _definitions.GetArmy(definition), definition.MaxHp);
        sprite.CreatedAt = time;
        sprite.Depth = depth;
        sprite.Owner = owner;
        sprite.Shooter = parent?.Shooter ?? parent;
        var state = _map.GetInitialAnimation(definition);
        sprite.Start(state, direction, time, _random);
        sprite.Lifetime = lifetime ?? obj.Lifetime;

        if (state != SpriteAnimation.Birth && !_map.IsLoading)
        {
            Emit(definition, SpriteAnimation.Birth, position, sprite.Direction, time, depth, parent: sprite);
        }

        if (obj.SpriteClass == SpriteClass.Cannon)
        {
            ConstructCannon(sprite, parent);
        }

        CreateLink(sprite, time, depth + 1);

        if (!_map.IsLoading)
        {
            _map.RunCreationHook(sprite);
        }

        if (IsSettled(sprite))
        {
            Detach(sprite);
            _settled.Add(GetState(sprite));

            return sprite;
        }

        _active.Add(sprite);
        _map.Register(UpdateRole.Effect, sprite);
        _map.Collector.InsertToHash(sprite);

        return sprite;
    }

    [EngineRoutine(0x00445770)]
    public void CreateLink(Sprite owner, int time, int depth)
    {
        ref readonly var obj = ref owner.Definition.Object;
        var definition = obj.Link == 0 || depth > SpriteCatalog.MaxLaunchDepth ? null : _definitions.Find(obj.Link);

        if (definition is null || owner.Link?.Definition == definition)
            return;

        if (definition.Id.IsScripted || definition.Object.WeaponIndex != 0 || definition.Object.SpriteClass == SpriteClass.Creature)
        {
            _map.AttachObjectLink(owner, definition);
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
    public static void ConstructLinker(Sprite linker, Sprite? parent, Vector3 position, Angle direction)
    {
        linker.LinkerBase = parent;
        linker.LinkerDirection = direction;

        if (parent is null)
            return;

        AppendLink(parent, linker);
        linker.LinkerOffset = position - parent.Position;
    }

    [EngineRoutine(0x004449c0)]
    public static void AppendLink(Sprite parent, Sprite link)
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

    private static bool IsSettled(Sprite sprite)
    {
        ref readonly var obj = ref sprite.Definition.Object;

        return sprite is { IsMoving: false, VerticalSpeed: 0 } && (obj.Properties & (ObjectProperties.Gravity | ObjectProperties.Gravity2)) == 0 &&
               sprite is { Animation: SpriteAnimation.Stand, Lifetime: >= SpriteDefinition.Unlimited } && obj.SpriteClass == SpriteClass.BuiltTerrain;
    }

    [EngineRoutine(0x00447fe0)]
    private void ConstructCannon(Sprite sprite, Sprite? parent)
    {
        ref readonly var obj = ref sprite.Definition.Object;

        if (obj.Properties.HasFlag(ObjectProperties.RandomZSpeed) && !sprite.Definition.ZSpeed.IsZero())
        {
            bool downward = (_random.Next() & 1) == 0;
            float lift = _random.NextSingle() * sprite.Definition.ZSpeed;
            sprite.VerticalSpeed = downward ? -lift : lift;
        }
        else if (parent is not null && parent.Animation >= SpriteAnimation.Death && obj.TopZ < 0)
        {
            sprite.VerticalSpeed = sprite.Definition.ZSpeed;
            sprite.Speed = sprite.MaxSpeed + parent.Speed;
        }
        else
        {
            sprite.VerticalSpeed = sprite.Definition.ZSpeed;
        }

        if (obj.Properties.HasFlag(ObjectProperties.RandomSpeed) && !sprite.Definition.Speed.IsZero())
        {
            sprite.MaxSpeed = _random.NextSingle() * sprite.Definition.Speed;
        }

        sprite.IsClimbing = true;
        _motion.StartMove(sprite);
    }

    public void Forget(Sprite removed)
    {
        foreach (var sprite in _active)
        {
            if (sprite.Definition.Object.SpriteClass == SpriteClass.Cannon)
            {
                for (int built = SpriteKinematics.ForgetCannonGoal(sprite, removed); built > 0; built--)
                {
                    _map.DrawGoalSpriteRandom();
                }
            }
            else
            {
                if (SpriteKinematics.ForgetObject(sprite, removed))
                {
                    _map.DrawGoalSpriteRandom();
                }
            }
        }
    }

    public void Compact()
    {
        if (_leaving.Count == 0)
            return;

        _active.RemoveAll(sprite => sprite.LeavesEffects);
        foreach (var sprite in _leaving)
        {
            sprite.LeavesEffects = false;
            _pool.Return(sprite);
        }

        _leaving.Clear();
    }

    public bool Tick(Sprite sprite)
    {
        bool stays = Update(sprite);
        _map.Collector.ChangeCoor(sprite);

        if (stays)
            return true;

        Leave(sprite);

        return false;
    }

    public void Leave(Sprite sprite)
    {
        _map.Collector.DeleteFromHash(sprite);
        sprite.LeavesEffects = true;
        _leaving.Add(sprite);

        if (IsLink(sprite))
        {
            sprite.Owner!.Link = null;
        }

        foreach (var tail in sprite.EnumerateLinks())
        {
            if (tail is { Role: UpdateRole.Effect, LeavesEffects: false })
            {
                _orphans.Add(tail);
            }
        }

        sprite.Link = null;

        _map.ReleaseSprite(sprite);
    }

    private bool Update(Sprite sprite)
    {
        if (sprite.Owner is not null && _orphans.Remove(sprite))
            return false;

        return _map.Tact(sprite);
    }

    private static float KeepHover(Sprite sprite, float height, float ground)
    {
        ref readonly var obj = ref sprite.Definition.Object;
        float hover = ground + obj.TopZ;
        float z = sprite.Position.Z;

        if (z.IsNear(height) || obj.Properties.HasFlag(ObjectProperties.Gravity) || hover.IsZero())
            return height;

        if (hover > z ? hover <= height : hover < z ? height < hover : !obj.Properties.HasFlag(ObjectProperties.SelfMoving))
        {
            sprite.VerticalSpeed = 0;

            return hover > z || hover < z ? hover : height;
        }

        return height;
    }

    private static Vector3 KeepGround(Vector3 next, Sprite sprite) => next with { X = sprite.Position.X, Y = sprite.Position.Y };

    public void Move(Sprite sprite, int elapsed)
    {
        if (sprite.Definition.Object.SpriteClass == SpriteClass.Cannon)
        {
            RunCannon(sprite, elapsed);
        }
        else
        {
            RunGeneric(sprite, elapsed);
        }
    }

    private static bool NeedsMotion(SpriteDefinition definition)
    {
        ref readonly var obj = ref definition.Object;

        return !definition.Speed.IsZero() || !definition.ZSpeed.IsZero() || (obj.Properties & (ObjectProperties.Gravity | ObjectProperties.Gravity2 | ObjectProperties.Wind)) != 0;
    }

    [EngineRoutine(0x0043f820)]
    private void RunGeneric(Sprite sprite, int elapsed)
    {
        if (!NeedsMotion(sprite.Definition))
            return;

        ref readonly var obj = ref sprite.Definition.Object;
        var next = _motion.Move(sprite, elapsed);
        float current = _movement.GetHeight(sprite.Position.AsVector2());
        float ground = _movement.GetHeight(next.AsVector2());

        if (obj.SpriteType.HasFlag(SpriteType.Cannon) && obj.Properties.HasFlag(ObjectProperties.Gravity) && ground >= next.Z && sprite.Position.Z >= current)
        {
            next.Z = sprite.Position.Z;
        }
        else
        {
            next.Z = KeepHover(sprite, next.Z, ground);
        }

        if (sprite.HasGoal && !sprite.Speed.IsZero() && sprite is { HasReachedX: true, HasReachedY: true })
        {
            SpriteKinematics.Stop(sprite);
        }

        if (!next.X.IsNear(sprite.Position.X) || !next.Y.IsNear(sprite.Position.Y))
        {
            if (_map.Projectiles.IsBlockedAt(sprite.Definition, next, sprite))
            {
                sprite.VerticalSpeed = 0;
                sprite.Speed = 0;
                sprite.IsBlocked = true;
                next = KeepGround(next, sprite);
            }
        }

        sprite.ChangeCoor(next);
    }

    [EngineRoutine(0x00448130)]
    private void RunCannon(Sprite sprite, int elapsed)
    {
        if (!NeedsMotion(sprite.Definition))
            return;

        ref readonly var obj = ref sprite.Definition.Object;
        var next = _motion.Move(sprite, elapsed);

        if (sprite is { IsBlocked: true, Animation: < SpriteAnimation.Death })
        {
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }

        float current = _movement.GetHeight(sprite.Position.AsVector2());
        float ground = _movement.GetHeight(next.AsVector2());

        if (ground < next.Z || sprite.Position.Z < current)
        {
            next.Z = KeepHover(sprite, next.Z, ground);
        }
        else
        {
            next.Z = sprite.Position.Z;
            Land(sprite, ref next, current, ground);
        }

        if (!next.X.IsNear(sprite.Position.X) || !next.Y.IsNear(sprite.Position.Y))
        {
            if (_map.Projectiles.IsBlockedAt(sprite.Definition, next, sprite))
            {
                if (sprite.Animation < SpriteAnimation.Death)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Death);
                }

                next = KeepGround(next, sprite);
            }
            else if (sprite.Animation < SpriteAnimation.Death && !obj.Properties.HasFlag(ObjectProperties.Gravity) && IsFarOutside(next.AsVector2()))
            {
                sprite.ChangeAnimation(SpriteAnimation.Death);
            }
        }

        float previousHeight = sprite.Position.Z;
        sprite.ChangeCoor(next);
        Steer(sprite, elapsed, previousHeight);
    }

    private void Land(Sprite sprite, ref Vector3 next, float current, float ground)
    {
        ref readonly var obj = ref sprite.Definition.Object;
        bool climbs = ground > current;

        if (sprite.Animation >= SpriteAnimation.Death)
        {
            SpriteKinematics.Stop(sprite);

            if (climbs)
            {
                _map.CreateChildFor(sprite, SpriteAnimation.Load);
            }

            next = KeepGround(next, sprite);

            return;
        }

        if (!obj.Properties.HasFlag(ObjectProperties.Bouncing))
        {
            SpriteKinematics.Stop(sprite);
            next.Z = current;
            _map.CreateChildFor(sprite, climbs ? SpriteAnimation.Load : SpriteAnimation.Unload);
            sprite.ChangeAnimation(SpriteAnimation.Death);
            next = KeepGround(next, sprite);

            return;
        }

        if (climbs)
        {
            sprite.ChangeAnimation(SpriteAnimation.Load);
            sprite.ChangeDirection(sprite.Direction.Opposite);
        }
        else if (sprite.VerticalSpeed < -MinimumBounceSpeed)
        {
            sprite.ChangeAnimation(SpriteAnimation.Unload);
            sprite.VerticalSpeed *= -BounceRetention;
        }
        else if (sprite.VerticalSpeed <= MinimumLift)
        {
            sprite.VerticalSpeed = 0;
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }
        else
        {
            sprite.VerticalSpeed *= BounceRetention;
            sprite.Speed *= BounceRetention;
            sprite.ChangeDirection(sprite.Direction.Opposite);
        }

        next = KeepGround(next, sprite);
    }

    private void Steer(Sprite sprite, int elapsed, float previousHeight)
    {
        ref readonly var obj = ref sprite.Definition.Object;

        if (!sprite.HasGoal)
        {
            if (obj.TopZ < 0 && sprite.VerticalSpeed < 0)
            {
                sprite.RotateTact(sprite.Direction + FallTurn, elapsed);
            }

            return;
        }

        var goal = sprite.GoalPosition;
        var target = sprite.Goal;
        float distance = sprite.Position.AsVector2().MeasureOctagonalDistance(goal.AsVector2());
        var aim = Angle.FromOffset((goal - sprite.Position).AsVector2()) + (sprite.Speed < 0 ? Angle.HalfTurn : 0);

        if (!obj.Properties.HasFlag(ObjectProperties.SelfMoving))
        {
            if (distance > SteeringReach)
            {
                var before = sprite.Direction;
                sprite.RotateTact(aim, elapsed);

                if (before.MeasureTurnTo(sprite.Direction) > MaxSteeringTurn)
                {
                    SpriteKinematics.Stop(sprite);
                    sprite.ChangeAnimation(SpriteAnimation.Death);
                }
            }
        }
        else if (!sprite.IsClimbing)
        {
            var remaining = sprite.RotateTact(aim, elapsed);

            if (distance <= RocketCloseReach || distance >= RocketFarReach || remaining.Value < RocketTurnLimit)
            {
                sprite.VerticalSpeed = sprite.Position.Z <= goal.Z || distance.IsZero() ? 0
                    : Math.Max((goal.Z - sprite.Position.Z) / distance * RocketClimbRate, -sprite.Definition.ZSpeed);
            }
            else
            {
                sprite.IsClimbing = true;
                _motion.StartMove(sprite);
            }
        }
        else if (_movement.GetHeight(sprite.Position.AsVector2()) + obj.TopZ <= sprite.Position.Z && goal.Z <= sprite.Position.Z || sprite.VerticalSpeed <= 0)
        {
            sprite.IsClimbing = false;
        }
        else
        {
            sprite.VerticalSpeed -= elapsed * _definitions.Gravitation;
        }

        if (HitsGoal(sprite, goal, target, previousHeight))
        {
            SpriteKinematics.Stop(sprite);
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }
    }

    private static bool HitsGoal(Sprite sprite, Vector3 goal, Sprite? target, float previousHeight)
    {
        var half = sprite.Definition.Half;
        var goalHalf = target?.Definition.Half ?? PlayerMovement.MoveTargetHalfSize;
        var offset = Vector2.Abs(sprite.Position.AsVector2() - goal.AsVector2());

        if (sprite is { HasReachedX: true, HasReachedY: true } || offset.X < half.X + goalHalf.X && offset.Y < half.Y + goalHalf.Y)
        {
            float goalHeight = target?.Definition.Object.Size.Z ?? PlayerMovement.TargetHeight;
            float z = sprite.Position.Z;

            if (goal.Z <= sprite.Definition.Object.Size.Z + z && z <= goal.Z + goalHeight ||
                Math.Min(previousHeight, z) <= goal.Z && goal.Z <= Math.Max(previousHeight, z))
                return true;
        }

        return sprite.Definition.Object.Properties.HasFlag(ObjectProperties.SelfMoving) && MathF.Abs(goal.Z - sprite.Position.Z) < RocketHitHeight &&
               offset is { X: < RocketHitReach, Y: < RocketHitReach };
    }

    private bool IsFarOutside(Vector2 point) =>
        point.X < -ExitMarginX || point.Y < -ExitMarginY ||
        point.X > _movement.MapSize.Width + ExitMarginX || point.Y > _movement.MapSize.Height + ExitMarginY;

    [EngineRoutine(0x00448950)]
    public static void DecideCannon(Sprite sprite)
    {
        var state = sprite.Animation;

        if (state == SpriteAnimation.Fight)
        {
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }
        else if (state < SpriteAnimation.Death)
        {
            if (sprite.Position.Z < DeepFall)
            {
                sprite.ChangeAnimation(SpriteAnimation.Death2);
            }
            else if (!sprite.Speed.IsZero())
            {
                sprite.ChangeAnimation(SpriteAnimation.Go);
            }
            else if (state > SpriteAnimation.Open && state != SpriteAnimation.StandOpen)
            {
                sprite.ChangeAnimation(SpriteAnimation.Stand);
            }
        }
    }

    [EngineRoutine(0x00440830)]
    public static void DecideObject(Sprite sprite)
    {
        var state = sprite.Animation;

        if (state >= SpriteAnimation.Death)
            return;

        if (sprite.Speed.IsZero())
        {
            if (state > SpriteAnimation.RRotate && state != SpriteAnimation.StandOpen)
            {
                sprite.ChangeAnimation(SpriteAnimation.Stand);
            }
        }
        else if (state != SpriteAnimation.Go)
        {
            sprite.ChangeAnimation(SpriteAnimation.Go);
        }
    }

    public static EffectState GetState(Sprite sprite) =>
        new(sprite.Definition.Id, sprite.Position, sprite.Direction, sprite.Animation, sprite.CreatedAt, sprite.Frame, sprite.KeyframePosition);
}
