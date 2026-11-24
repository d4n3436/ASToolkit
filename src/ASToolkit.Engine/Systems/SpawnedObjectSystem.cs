using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class SpawnedObjectSystem
{
    private const int MaxSteeringTurn = 100;
    private const int RocketTurnLimit = 0x47;
    private const int FallTurn = 0x20;
    private const float RocketClimbRate = 0.1f;
    private const float RocketHitHeight = 20;
    private const float RocketHitReach = 10;
    private const float RocketCloseReach = 10;
    private const float RocketFarReach = 30;
    private const float MinimumBounceSpeed = 0.022f;
    private const float MinimumLift = 0.005f;
    private const float BounceRetention = 0.5f;
    private const float SteeringReach = 100;

    private static readonly Vector2 ExitMargin = new(220, 200);

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

        AddLinkToLast(parent, linker);
        linker.LinkerOffset = position - parent.Position;
    }

    [EngineRoutine(0x004449c0)]
    public static void AddLinkToLast(Sprite parent, Sprite link)
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

    public void Move(Sprite sprite, int elapsed)
    {
        if (sprite.Definition.Class == SpriteClass.Cannon)
        {
            RunCannon(sprite, elapsed);
        }
        else
        {
            RunGeneric(sprite, elapsed);
        }
    }

    [EngineRoutine(0x00447fe0)]
    public void ConstructCannon(Sprite sprite, Sprite? parent)
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

    private static void KeepHoverHeight(Sprite sprite, in GameObject gameObject, float hover, ref Vector3 next)
    {
        if (sprite.Position.Z.IsExactly(next.Z) || gameObject.Properties.HasFlag(ObjectProperties.Gravity) || hover.IsExactlyZero())
            return;

        float height = hover;

        if (hover <= sprite.Position.Z)
        {
            if (sprite.Position.Z <= hover)
            {
                height = next.Z;

                if (!gameObject.Properties.HasFlag(ObjectProperties.SelfMoving))
                {
                    next.Z = height;
                    sprite.VerticalSpeed = 0;
                }
            }
            else if (next.Z < hover)
            {
                next.Z = height;
                sprite.VerticalSpeed = 0;
            }
        }
        else if (hover <= next.Z)
        {
            next.Z = height;
            sprite.VerticalSpeed = 0;
        }
    }

    [EngineRoutine(0x0043f820)]
    private void RunGeneric(Sprite sprite, int elapsed)
    {
        if (!sprite.Definition.CanMove)
            return;

        ref readonly var gameObject = ref sprite.Definition.Object;
        var next = _motion.CalculateNewCoordinates(sprite, elapsed);
        float current = _movement.GetHeight(sprite.Position.AsVector2());
        float ground = _movement.GetHeight(next.AsVector2());
        float hover = ground + gameObject.TopZ;

        if (gameObject.SpriteType.HasFlag(SpriteType.Cannon) && gameObject.Properties.HasFlag(ObjectProperties.Gravity) && ground >= next.Z && sprite.Position.Z >= current)
        {
            next.Z = sprite.Position.Z;
        }
        else
        {
            KeepHoverHeight(sprite, gameObject, hover, ref next);
        }

        if (sprite.HasGoal && !sprite.Speed.IsExactlyZero() && sprite is { HasReachedX: true, HasReachedY: true })
        {
            SpriteKinematics.Stop(sprite);
        }

        if (next.AsVector2() != sprite.Position.AsVector2())
        {
            if (_map.Projectiles.FindBlocker(sprite, next) is not null)
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
        if (!sprite.Definition.CanMove)
            return;

        ref readonly var gameObject = ref sprite.Definition.Object;
        var next = _motion.CalculateNewCoordinates(sprite, elapsed);

        if (sprite is { IsBlocked: true, Animation: < SpriteAnimation.Death })
        {
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }

        float current = _movement.GetHeight(sprite.Position.AsVector2());
        float ground = _movement.GetHeight(next.AsVector2());
        float hover = ground + gameObject.TopZ;
        float previousHeight = sprite.Position.Z;

        if (ground < next.Z || sprite.Position.Z < current)
        {
            KeepHoverHeight(sprite, gameObject, hover, ref next);
        }
        else
        {
            HitGround(sprite, gameObject, ground, current, ref next);
        }

        MoveCannon(sprite, gameObject, next);
        if (!sprite.Position.Z.IsExactly(next.Z))
        {
            sprite.ChangeCoor(sprite.Position with { Z = next.Z });
        }

        if (!sprite.HasGoal)
        {
            if (gameObject.TopZ < 0 && sprite.VerticalSpeed < 0)
            {
                sprite.RotateTact(sprite.Direction + FallTurn, elapsed);
            }

            return;
        }

        SteerToGoal(sprite, gameObject, previousHeight, elapsed);
    }

    private void HitGround(Sprite sprite, in GameObject gameObject, float ground, float current, ref Vector3 next)
    {
        next.Z = sprite.Position.Z;

        if (sprite.Animation < SpriteAnimation.Death)
        {
            if (!gameObject.Properties.HasFlag(ObjectProperties.Bouncing))
            {
                SpriteKinematics.Stop(sprite);
                next.Z = current;
                _map.CreateChildFor(sprite, ground <= current ? SpriteAnimation.Unload : SpriteAnimation.Load);
                sprite.ChangeAnimation(SpriteAnimation.Death);
            }
            else if (ground <= current)
            {
                if (-MinimumBounceSpeed <= sprite.VerticalSpeed)
                {
                    if (sprite.VerticalSpeed <= MinimumLift)
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
                }
                else
                {
                    sprite.ChangeAnimation(SpriteAnimation.Unload);
                    sprite.VerticalSpeed *= -BounceRetention;
                }
            }
            else
            {
                sprite.ChangeAnimation(SpriteAnimation.Load);
                sprite.ChangeDirection(sprite.Direction.Opposite);
            }
        }
        else
        {
            SpriteKinematics.Stop(sprite);

            if (ground > current)
            {
                _map.CreateChildFor(sprite, SpriteAnimation.Load);
            }
        }

        next = next with { X = sprite.Position.X, Y = sprite.Position.Y };
    }

    private void MoveCannon(Sprite sprite, in GameObject gameObject, Vector3 next)
    {
        if (next.AsVector2() == sprite.Position.AsVector2())
            return;

        if (_map.Projectiles.FindBlocker(sprite, next) is not null)
        {
            if (sprite.Animation < SpriteAnimation.Death)
            {
                sprite.ChangeAnimation(SpriteAnimation.Death);
            }
        }
        else
        {
            sprite.ChangeCoor(next);

            if (sprite.Animation < SpriteAnimation.Death && !gameObject.Properties.HasFlag(ObjectProperties.Gravity) &&
                (sprite.Position.X < -ExitMargin.X || sprite.Position.Y < -ExitMargin.Y ||
                 sprite.Position.X > _movement.MapSize.Width + ExitMargin.X || sprite.Position.Y > _movement.MapSize.Height + ExitMargin.Y))
            {
                sprite.ChangeAnimation(SpriteAnimation.Death);
            }
        }
    }

    private void SteerToGoal(Sprite sprite, in GameObject gameObject, float previousHeight, int elapsed)
    {
        var goal = sprite.GoalPosition;
        var target = sprite.Goal!;
        float distance = sprite.Position.AsVector2().MeasureOctagonalDistance(goal.AsVector2());
        var aim = sprite.DirectionTo(target) + (sprite.Speed < 0 ? Angle.HalfTurn : 0);

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

        var offset = Vector2.Abs(sprite.Position.AsVector2() - goal.AsVector2());
        bool isHit = false;

        if (sprite is { HasReachedX: true, HasReachedY: true } || offset.X < sprite.Definition.Half.X + target.Definition.Half.X && offset.Y < sprite.Definition.Half.Y + target.Definition.Half.Y)
        {
            isHit = goal.Z <= sprite.Definition.Object.Size.Z + sprite.Position.Z && sprite.Position.Z <= goal.Z + target.Definition.Object.Size.Z ||
                Math.Min(previousHeight, sprite.Position.Z) <= goal.Z && goal.Z <= Math.Max(previousHeight, sprite.Position.Z);
        }

        if (isHit || gameObject.Properties.HasFlag(ObjectProperties.SelfMoving) && MathF.Abs(goal.Z - sprite.Position.Z) < RocketHitHeight && offset is { X: < RocketHitReach, Y: < RocketHitReach })
        {
            SpriteKinematics.Stop(sprite);
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }
    }
}
