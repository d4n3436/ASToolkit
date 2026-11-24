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
    /// Initializes a new instance of the <see cref="Cannon"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Cannon(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, definition, position, army, hitPoints, map, direction)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the cannon climbs onto higher ground.
    /// </summary>
    public bool IsClimbing { get; set; }

    [EngineRoutine(0x00448950)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        if ((ActionCode)act != ActionCode.NextCommand)
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
        if (!Definition.CanMove)
            return;

        ref readonly var gameObject = ref Definition.Object;
        int elapsed = Map.FrameTime;
        var next = Map.Motion.CalculateNewCoordinates(this, elapsed);

        if (this is { IsBlocked: true, Animation: < SpriteAnimation.Death })
        {
            ChangeAnimation(SpriteAnimation.Death);
        }

        float current = Map.Movement.GetHeight(Position.AsVector2());
        float ground = Map.Movement.GetHeight(next.AsVector2());
        float hover = ground + gameObject.TopZ;
        float previousHeight = Position.Z;

        if (ground < next.Z || Position.Z < current)
        {
            KeepHoverHeight(this, gameObject, hover, ref next);
        }
        else
        {
            HitGround(gameObject, ground, current, ref next);
        }

        MoveCannon(gameObject, next);
        if (!Position.Z.IsExactly(next.Z))
        {
            ChangeCoor(Position with { Z = next.Z });
        }

        if (!HasGoal)
        {
            if (gameObject.TopZ < 0 && VerticalSpeed < 0)
            {
                RotateTact(Direction + FallTurn, elapsed);
            }

            return;
        }

        SteerToGoal(gameObject, previousHeight, elapsed);
    }

    private void HitGround(in GameObject gameObject, float ground, float current, ref Vector3 next)
    {
        next.Z = Position.Z;

        if (Animation < SpriteAnimation.Death)
        {
            if (!gameObject.Properties.HasFlag(ObjectProperties.Bouncing))
            {
                SpriteKinematics.Stop(this);
                next.Z = current;
                Map.CreateChildFor(this, ground <= current ? SpriteAnimation.Unload : SpriteAnimation.Load);
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
            SpriteKinematics.Stop(this);

            if (ground > current)
            {
                Map.CreateChildFor(this, SpriteAnimation.Load);
            }
        }

        next = next with { X = Position.X, Y = Position.Y };
    }

    private void MoveCannon(in GameObject gameObject, Vector3 next)
    {
        if (next.AsVector2() == Position.AsVector2())
            return;

        if (Map.Projectiles.FindBlocker(this, next) is not null)
        {
            if (Animation < SpriteAnimation.Death)
            {
                ChangeAnimation(SpriteAnimation.Death);
            }
        }
        else
        {
            ChangeCoor(next);

            if (Animation < SpriteAnimation.Death && !gameObject.Properties.HasFlag(ObjectProperties.Gravity) &&
                (Position.X < -ExitMargin.X || Position.Y < -ExitMargin.Y ||
                 Position.X > Map.Movement.MapSize.Width + ExitMargin.X || Position.Y > Map.Movement.MapSize.Height + ExitMargin.Y))
            {
                ChangeAnimation(SpriteAnimation.Death);
            }
        }
    }

    private void SteerToGoal(in GameObject gameObject, float previousHeight, int elapsed)
    {
        var goal = GoalPosition;
        var target = Goal!;
        float distance = Position.AsVector2().MeasureOctagonalDistance(goal.AsVector2());
        var aim = DirectionTo(target) + (Speed < 0 ? Angle.HalfTurn : 0);

        if (!gameObject.Properties.HasFlag(ObjectProperties.SelfMoving))
        {
            if (distance > SteeringReach)
            {
                var before = Direction;
                RotateTact(aim, elapsed);

                if (before.MeasureTurnTo(Direction) > MaxSteeringTurn)
                {
                    SpriteKinematics.Stop(this);
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
                    : Math.Max((goal.Z - Position.Z) / distance * RocketClimbRate, -Definition.ZSpeed);
            }
            else
            {
                IsClimbing = true;
                Map.Motion.StartMove(this);
            }
        }
        else if (Map.Movement.GetHeight(Position.AsVector2()) + gameObject.TopZ <= Position.Z && goal.Z <= Position.Z || VerticalSpeed <= 0)
        {
            IsClimbing = false;
        }
        else
        {
            VerticalSpeed -= elapsed * Map.Definitions.Gravitation;
        }

        var offset = Vector2.Abs(Position.AsVector2() - goal.AsVector2());
        bool isHit = false;

        if (this is { HasReachedX: true, HasReachedY: true } || offset.X < Definition.Half.X + target.Definition.Half.X && offset.Y < Definition.Half.Y + target.Definition.Half.Y)
        {
            isHit = goal.Z <= Definition.Object.Size.Z + Position.Z && Position.Z <= goal.Z + target.Definition.Object.Size.Z ||
                Math.Min(previousHeight, Position.Z) <= goal.Z && goal.Z <= Math.Max(previousHeight, Position.Z);
        }

        if (isHit || gameObject.Properties.HasFlag(ObjectProperties.SelfMoving) && MathF.Abs(goal.Z - Position.Z) < RocketHitHeight && offset is { X: < RocketHitReach, Y: < RocketHitReach })
        {
            SpriteKinematics.Stop(this);
            ChangeAnimation(SpriteAnimation.Death);
        }
    }
}
