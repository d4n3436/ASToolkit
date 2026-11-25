using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a cannon sprite, a fired projectile.
/// </summary>
public sealed class Cannon : Sprite
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
    private const float DeepFall = -100;

    private static readonly Vector2 ExitMargin = new(220, 200);

    /// <summary>
    /// Initializes a new instance of the <see cref="Cannon"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x00447fe0)]
    public Cannon(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        if (vid.Properties.HasFlag(ObjectProperties.RandomZSpeed) && !Vid.ZSpeed.IsExactlyZero())
        {
            bool downward = (Map.Random.Next() & 1) == 0;
            float lift = Map.Random.Next() * Vid.ZSpeed;
            VerticalSpeed = downward ? lift * -GameRandom.UnitScale : lift * GameRandom.UnitScale;
        }
        else if (parent is not null && parent.Animation >= SpriteAnimation.Death && vid.TopZ < 0)
        {
            VerticalSpeed = Vid.ZSpeed;
            Speed = MaxSpeed + parent.Speed;
        }
        else
        {
            VerticalSpeed = Vid.ZSpeed;
        }

        if (vid.Properties.HasFlag(ObjectProperties.RandomSpeed) && !Vid.Speed.IsExactlyZero())
        {
            MaxSpeed = Map.Random.Next() * Vid.Speed * GameRandom.UnitScale;
        }

        IsClimbing = true;
        StartMove();
    }

    /// <summary>
    /// Gets or sets a value indicating whether the cannon climbs onto higher ground.
    /// </summary>
    public bool IsClimbing { get; set; }

    [EngineRoutine(0x00448950)]
    internal override int Action(ActionCode act, int var1, int var2, int var3, Sprite? target)
    {
        if (act != ActionCode.NextCommand)
            return base.Action(act, var1, var2, var3, target);

        var state = Animation;

        if (state == SpriteAnimation.Fight)
        {
            ChangeAnimation(SpriteAnimation.Death);
        }
        else if (state < SpriteAnimation.Death)
        {
            if (Position.Z < DeepFall)
            {
                ChangeAnimation(SpriteAnimation.Death2);
            }
            else if (!Speed.IsExactlyZero())
            {
                ChangeAnimation(SpriteAnimation.Go);
            }
            else if (state > SpriteAnimation.Open && state != SpriteAnimation.StandOpen)
            {
                ChangeAnimation(SpriteAnimation.Stand);
            }
        }

        return 0;
    }

    [EngineRoutine(0x004489e0)]
    internal override void DeletePointerToSprite(Sprite removed)
    {
        if (Goal == removed)
        {
            Goal = Map.ConstructGoal(removed.Position);
        }

        base.DeletePointerToSprite(removed);
    }

    [EngineRoutine(0x00448130)]
    internal override void MoveTact()
    {
        if (!Vid.CanMove)
            return;

        var vid = Vid;
        int elapsed = Map.FrameTime;
        var next = MoveTactCalculateCoordinates(elapsed);

        if (this is { IsBlocked: true, Animation: < SpriteAnimation.Death })
        {
            ChangeAnimation(SpriteAnimation.Death);
        }

        float current = Map.GetGroundZ(Position.AsVector2());
        float ground = Map.GetGroundZ(next.AsVector2());
        float hover = ground + vid.TopZ;
        float previousHeight = Position.Z;

        if (ground < next.Z || Position.Z < current)
        {
            KeepHoverHeight(this, vid, hover, ref next);
        }
        else
        {
            HitGround(vid, ground, current, ref next);
        }

        MoveCannon(vid, next);
        if (!Position.Z.IsExactly(next.Z))
        {
            ChangeCoor(Position with { Z = next.Z });
        }

        if (!HasGoal)
        {
            if (vid.TopZ < 0 && VerticalSpeed < 0)
            {
                RotateTact(Direction + FallTurn, elapsed);
            }

            return;
        }

        SteerToGoal(vid, previousHeight, elapsed);
    }

    private void HitGround(Vid vid, float ground, float current, ref Vector3 next)
    {
        next.Z = Position.Z;

        if (Animation < SpriteAnimation.Death)
        {
            if (!vid.Properties.HasFlag(ObjectProperties.Bouncing))
            {
                Stop();
                next.Z = current;
                CreateChildFor(ground <= current ? SpriteAnimation.Unload : SpriteAnimation.Load);
                ChangeAnimation(SpriteAnimation.Death);
            }
            else if (ground <= current)
            {
                if (-MinimumBounceSpeed <= VerticalSpeed)
                {
                    if (VerticalSpeed <= MinimumLift)
                    {
                        VerticalSpeed = 0;
                        ChangeAnimation(SpriteAnimation.Death);
                    }
                    else
                    {
                        VerticalSpeed *= BounceRetention;
                        Speed *= BounceRetention;
                        ChangeDirection(Direction.Opposite);
                    }
                }
                else
                {
                    ChangeAnimation(SpriteAnimation.Unload);
                    VerticalSpeed *= -BounceRetention;
                }
            }
            else
            {
                ChangeAnimation(SpriteAnimation.Load);
                ChangeDirection(Direction.Opposite);
            }
        }
        else
        {
            Stop();

            if (ground > current)
            {
                CreateChildFor(SpriteAnimation.Load);
            }
        }

        next = next with { X = Position.X, Y = Position.Y };
    }

    private void MoveCannon(Vid vid, Vector3 next)
    {
        if (next.AsVector2() == Position.AsVector2())
            return;

        if (CanPlaceWithCrush(next) is not null)
        {
            if (Animation < SpriteAnimation.Death)
            {
                ChangeAnimation(SpriteAnimation.Death);
            }
        }
        else
        {
            ChangeCoor(next);

            if (Animation < SpriteAnimation.Death && !vid.Properties.HasFlag(ObjectProperties.Gravity) &&
                (Position.X < -ExitMargin.X || Position.Y < -ExitMargin.Y ||
                 Position.X > Map.Size.Width + ExitMargin.X || Position.Y > Map.Size.Height + ExitMargin.Y))
            {
                ChangeAnimation(SpriteAnimation.Death);
            }
        }
    }

    private void SteerToGoal(Vid vid, float previousHeight, int elapsed)
    {
        var goal = GoalPosition;
        var target = Goal!;
        float distance = Position.AsVector2().NearLength(goal.AsVector2());
        var aim = DirectionTo(target) + (Speed < 0 ? Angle.HalfTurn : 0);

        if (!vid.Properties.HasFlag(ObjectProperties.SelfMoving))
        {
            if (distance > SteeringReach)
            {
                var before = Direction;
                RotateTact(aim, elapsed);

                if (before.MeasureTurnTo(Direction) > MaxSteeringTurn)
                {
                    Stop();
                    ChangeAnimation(SpriteAnimation.Death);
                }
            }
        }
        else if (!IsClimbing)
        {
            var remaining = RotateTact(aim, elapsed);

            if (distance <= RocketCloseReach || distance >= RocketFarReach || remaining.Value < RocketTurnLimit)
            {
                VerticalSpeed = Position.Z <= goal.Z || distance.IsExactlyZero() ? 0
                    : Math.Max((goal.Z - Position.Z) / distance * RocketClimbRate, -Vid.ZSpeed);
            }
            else
            {
                IsClimbing = true;
                StartMove();
            }
        }
        else if (Map.GetGroundZ(Position.AsVector2()) + vid.TopZ <= Position.Z && goal.Z <= Position.Z || VerticalSpeed <= 0)
        {
            IsClimbing = false;
        }
        else
        {
            VerticalSpeed -= elapsed * Map.Constant.Gravitation;
        }

        var offset = Vector2.Abs(Position.AsVector2() - goal.AsVector2());
        bool isHit = false;

        if (this is { HasReachedX: true, HasReachedY: true } || offset.X < Vid.Half.X + target.Vid.Half.X && offset.Y < Vid.Half.Y + target.Vid.Half.Y)
        {
            isHit = goal.Z <= Vid.Size.Z + Position.Z && Position.Z <= goal.Z + target.Vid.Size.Z ||
                Math.Min(previousHeight, Position.Z) <= goal.Z && goal.Z <= Math.Max(previousHeight, Position.Z);
        }

        if (isHit || vid.Properties.HasFlag(ObjectProperties.SelfMoving) && MathF.Abs(goal.Z - Position.Z) < RocketHitHeight && offset is { X: < RocketHitReach, Y: < RocketHitReach })
        {
            Stop();
            ChangeAnimation(SpriteAnimation.Death);
        }
    }
}
