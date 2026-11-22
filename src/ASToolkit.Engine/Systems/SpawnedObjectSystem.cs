using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

internal sealed class SpawnedObjectSystem
{
    private const int MaxSteeringTurn = 100;
    private const int RocketTurnLimit = 0x47;
    private const int FallTurn = 0x20;
    private static readonly Vector2 ExitMargin = new(220, 200);
    private const float RocketClimbRate = 0.1f;
    private const float RocketHitHeight = 20;
    private const float RocketHitReach = 10;
    private const float RocketCloseReach = 10;
    private const float RocketFarReach = 30;
    private const float MinimumBounceSpeed = 0.022f;
    private const float MinimumLift = 0.005f;
    private const float BounceRetention = 0.5f;
    private const float SteeringReach = 100;

    private readonly SpriteCatalog _definitions;
    private readonly MovementMap _movement;
    private readonly Random _random;
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

    public static EffectState GetState(Sprite sprite) =>
        new(sprite.Definition.Id, sprite.Position, sprite.Direction, sprite.Animation, sprite.CreatedAt, sprite.Frame, sprite.KeyframePosition);

    public Sprite? Spawn(KnownObject id, Vector3 position, Angle direction, Sprite? owner = null, int? lifetime = null)
    {
        var definition = _definitions.Find(id);

        return definition is null ? null : Create(definition, position, direction, null, owner, lifetime);
    }

    public Sprite Create(SpriteDefinition definition, Vector3 position, Angle direction, Sprite? parent, Sprite? owner = null, int? lifetime = null)
    {
        ref readonly var gameObject = ref definition.Object;

        if (gameObject.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Balloon or SpriteClass.Creature)
        {
            var unit = _map.AddMonster(definition, position, direction, parent?.Army ?? _definitions.GetArmy(definition), parent);

            if (!_map.IsLoading)
            {
                _map.RunCreationHandler(unit);
            }

            return unit;
        }

        var sprite = _map.ConstructSprite(definition, position, direction, parent?.Army ?? _definitions.GetArmy(definition), definition.MaxHp, -1, parent, owner, parent?.Shooter ?? parent, lifetime);

        if (gameObject.SpriteClass == SpriteClass.Cannon)
        {
            ConstructCannon(sprite, parent);
        }
        else if (gameObject.SpriteClass == SpriteClass.BuiltTerrain)
        {
            _map.ConstructBuiltTerrain(sprite);
        }

        if (!_map.IsLoading)
        {
            _map.RunCreationHandler(sprite);
        }

        return sprite;
    }

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

    private static float KeepHover(Sprite sprite, float height, float ground)
    {
        ref readonly var gameObject = ref sprite.Definition.Object;
        float hover = ground + gameObject.TopZ;

        if (sprite.Position.Z.IsExactly(height) || gameObject.Properties.HasFlag(ObjectProperties.Gravity) || hover.IsExactlyZero())
            return height;

        if (hover > sprite.Position.Z ? hover <= height : hover < sprite.Position.Z ? height < hover : !gameObject.Properties.HasFlag(ObjectProperties.SelfMoving))
        {
            sprite.VerticalSpeed = 0;

            return hover > sprite.Position.Z || hover < sprite.Position.Z ? hover : height;
        }

        return height;
    }

    private static Vector3 KeepGround(Vector3 next, Sprite sprite) => next with { X = sprite.Position.X, Y = sprite.Position.Y };

    private static bool CanMove(SpriteDefinition definition)
    {
        ref readonly var gameObject = ref definition.Object;

        return !definition.Speed.IsExactlyZero() || !definition.ZSpeed.IsExactlyZero() || gameObject.Properties.HasFlag(ObjectProperties.Gravity) || gameObject.Properties.HasFlag(ObjectProperties.Gravity2) || gameObject.Properties.HasFlag(ObjectProperties.Wind);
    }

    private static bool HitsGoal(Sprite sprite, Vector3 goal, Sprite target, float previousHeight)
    {
        var offset = Vector2.Abs(sprite.Position.AsVector2() - goal.AsVector2());

        if (sprite is { HasReachedX: true, HasReachedY: true } || offset.X < sprite.Definition.Half.X + target.Definition.Half.X && offset.Y < sprite.Definition.Half.Y + target.Definition.Half.Y)
        {
            if (goal.Z <= sprite.Definition.Object.Size.Z + sprite.Position.Z && sprite.Position.Z <= goal.Z + target.Definition.Object.Size.Z ||
                Math.Min(previousHeight, sprite.Position.Z) <= goal.Z && goal.Z <= Math.Max(previousHeight, sprite.Position.Z))
                return true;
        }

        return sprite.Definition.Object.Properties.HasFlag(ObjectProperties.SelfMoving) && MathF.Abs(goal.Z - sprite.Position.Z) < RocketHitHeight &&
               offset is { X: < RocketHitReach, Y: < RocketHitReach };
    }

    [EngineRoutine(0x00447fe0)]
    private void ConstructCannon(Sprite sprite, Sprite? parent)
    {
        ref readonly var gameObject = ref sprite.Definition.Object;

        if (gameObject.Properties.HasFlag(ObjectProperties.RandomZSpeed) && !sprite.Definition.ZSpeed.IsExactlyZero())
        {
            bool downward = (_random.Next() & 1) == 0;
            float lift = _random.Next() * sprite.Definition.ZSpeed;
            sprite.VerticalSpeed = downward ? lift * -GameRandom.UnitScale : lift * GameRandom.UnitScale;
        }
        else if (parent is not null && parent.Animation >= SpriteAnimation.Death && gameObject.TopZ < 0)
        {
            sprite.VerticalSpeed = sprite.Definition.ZSpeed;
            sprite.Speed = sprite.MaxSpeed + parent.Speed;
        }
        else
        {
            sprite.VerticalSpeed = sprite.Definition.ZSpeed;
        }

        if (gameObject.Properties.HasFlag(ObjectProperties.RandomSpeed) && !sprite.Definition.Speed.IsExactlyZero())
        {
            sprite.MaxSpeed = _random.Next() * sprite.Definition.Speed * GameRandom.UnitScale;
        }

        sprite.IsClimbing = true;
        _motion.StartMove(sprite);
    }

    [EngineRoutine(0x0043f820)]
    private void RunGeneric(Sprite sprite, int elapsed)
    {
        if (!CanMove(sprite.Definition))
            return;

        ref readonly var gameObject = ref sprite.Definition.Object;
        var next = _motion.CalculateNewCoordinates(sprite, elapsed);
        float current = _movement.GetHeight(sprite.Position.AsVector2());
        float ground = _movement.GetHeight(next.AsVector2());

        if (gameObject.SpriteType.HasFlag(SpriteType.Cannon) && gameObject.Properties.HasFlag(ObjectProperties.Gravity) && ground >= next.Z && sprite.Position.Z >= current)
        {
            next.Z = sprite.Position.Z;
        }
        else
        {
            next.Z = KeepHover(sprite, next.Z, ground);
        }

        if (sprite.HasGoal && !sprite.Speed.IsExactlyZero() && sprite is { HasReachedX: true, HasReachedY: true })
        {
            SpriteKinematics.Stop(sprite);
        }

        if (next.AsVector2() != sprite.Position.AsVector2())
        {
            if (_map.Projectiles.IsBlockedAt(sprite.Definition, next, sprite))
            {
                sprite.VerticalSpeed = 0;
                sprite.Speed = 0;
                sprite.IsBlocked = true;
            }
            else
            {
                sprite.ChangeCoor(next);
            }
        }

        if (!sprite.Position.Z.IsExactly(next.Z))
        {
            sprite.ChangeCoor(sprite.Position with { Z = next.Z });
        }
    }

    [EngineRoutine(0x00448130)]
    private void RunCannon(Sprite sprite, int elapsed)
    {
        if (!CanMove(sprite.Definition))
            return;

        ref readonly var gameObject = ref sprite.Definition.Object;
        var next = _motion.CalculateNewCoordinates(sprite, elapsed);

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

        float previousHeight = sprite.Position.Z;

        if (next.AsVector2() != sprite.Position.AsVector2())
        {
            if (_map.Projectiles.IsBlockedAt(sprite.Definition, next, sprite))
            {
                if (sprite.Animation < SpriteAnimation.Death)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Death);
                }
            }
            else
            {
                sprite.ChangeCoor(next);

                if (sprite.Animation < SpriteAnimation.Death && !gameObject.Properties.HasFlag(ObjectProperties.Gravity) && IsFarOutside(sprite.Position.AsVector2()))
                {
                    sprite.ChangeAnimation(SpriteAnimation.Death);
                }
            }
        }

        if (!sprite.Position.Z.IsExactly(next.Z))
        {
            sprite.ChangeCoor(sprite.Position with { Z = next.Z });
        }

        Steer(sprite, elapsed, previousHeight);
    }

    private void Land(Sprite sprite, ref Vector3 next, float current, float ground)
    {
        ref readonly var gameObject = ref sprite.Definition.Object;
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

        if (!gameObject.Properties.HasFlag(ObjectProperties.Bouncing))
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
        ref readonly var gameObject = ref sprite.Definition.Object;

        if (!sprite.HasGoal)
        {
            if (gameObject.TopZ < 0 && sprite.VerticalSpeed < 0)
            {
                sprite.RotateTact(sprite.Direction + FallTurn, elapsed);
            }

            return;
        }

        var goal = sprite.GoalPosition;
        var target = sprite.Goal!;
        float distance = sprite.Position.AsVector2().MeasureOctagonalDistance(goal.AsVector2());
        var aim = sprite.MeasureDirectionTo(target) + (sprite.Speed < 0 ? Angle.HalfTurn : 0);

        if (!gameObject.Properties.HasFlag(ObjectProperties.SelfMoving))
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
                sprite.VerticalSpeed = sprite.Position.Z <= goal.Z || distance.IsExactlyZero() ? 0
                    : Math.Max((goal.Z - sprite.Position.Z) / distance * RocketClimbRate, -sprite.Definition.ZSpeed);
            }
            else
            {
                sprite.IsClimbing = true;
                _motion.StartMove(sprite);
            }
        }
        else if (_movement.GetHeight(sprite.Position.AsVector2()) + gameObject.TopZ <= sprite.Position.Z && goal.Z <= sprite.Position.Z || sprite.VerticalSpeed <= 0)
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

    private bool IsFarOutside(Vector2 point) =>
        point.X < -ExitMargin.X || point.Y < -ExitMargin.Y ||
        point.X > _movement.MapSize.Width + ExitMargin.X || point.Y > _movement.MapSize.Height + ExitMargin.Y;
}
