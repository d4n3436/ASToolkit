using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Sprite
{
    [EngineRoutine(0x004446c0)]
    internal bool SetCommand(Command command, Sprite? target = null)
    {
        if (Command == Command.Pause && command != Command.Pause)
        {
            Timer = 0;
        }

        Goal = target;
        var link = ArmedLink;

        if (link is not null && link.Vid.FightChild != 0)
        {
            link.SetCommand(command, target);
        }

        if (command < Command.Build && !HasGoal)
        {
            Command = Command.None;

            return true;
        }

        Command = command;

        return false;
    }

    [EngineRoutine(0x00444750)]
    internal bool SetCommandWithoutLink(Command command, Sprite? target = null)
    {
        if (Command == Command.Pause && command != Command.Pause)
        {
            Timer = 0;
        }

        Goal = target;

        if (command < Command.Build && !HasGoal)
        {
            Command = Command.None;

            return true;
        }

        Command = command;

        return false;
    }

    [EngineRoutine(0x004445d0)]
    internal void Stop()
    {
        if (Command is Command.None or Command.Move)
        {
            SetCommandWithoutLink(Command.None);
        }

        ClearMove();
        VerticalSpeed = 0;

        if (Vid.Deceleration.IsExactly(Vid.Unlimited))
        {
            Speed = 0;
        }
    }

    [EngineRoutine(0x004447b0)]
    internal void Move(Sprite? target)
    {
        if (!SetCommandWithoutLink(Command.Move, target) && !StartMove())
        {
            SetCommand(Command.None);

            return;
        }

        var link = Link;

        if (link is not null && link.Vid == Vid.LinkVid && link.Vid.FightChild != 0 && link.Vid.WeaponIndex != 0 &&
            link.Command is not (Command.None or Command.OneAttack))
        {
            link.SetCommand(Command.None);
        }
    }

    [EngineRoutine(0x004443f0)]
    internal bool StartMove()
    {
        if (MaxSpeed.IsExactlyZero())
            return false;

        var vid = Vid;

        if (HasGoal)
        {
            var offset = GoalPosition - Position;

            if (offset.X.IsExactlyZero() && offset.Y.IsExactlyZero())
                return false;

            int distance = 0;

            if (Vid.Directions == 1 || vid.Properties.HasFlag(ObjectProperties.RandomBirth))
            {
                ChangeDirection(Angle.Aim(new Point((int)offset.X, (int)offset.Y), out distance));
            }

            if (!vid.SpriteType.HasFlag(OldSpriteType.Cannon) && Vid.Class != SpriteClass.Cannon)
            {
                VerticalSpeed = offset.Z > 0 ? Vid.ZSpeed : offset.Z < 0 ? -Vid.ZSpeed : 0;
            }
            else
            {
                if (distance == 0)
                {
                    Angle.Aim(new Point((int)offset.X, (int)offset.Y), out distance);
                }

                if (distance == 0)
                    return false;

                VerticalSpeed = Vid.CalculateZSpeed(Map, offset.Z, distance);
            }
        }

        IsMoving = true;
        HasReachedX = false;
        HasReachedY = false;

        if (Animation < SpriteAnimation.Death && Animation != SpriteAnimation.StartMove)
        {
            ChangeAnimation(Speed.IsExactlyZero() ? SpriteAnimation.StartMove : SpriteAnimation.Go);
        }

        if (vid.Acceleration.IsExactly(Vid.Unlimited))
        {
            Speed = MaxSpeed;
        }

        return true;
    }

    [EngineRoutine(0x0043fb10)]
    internal Vector3 MoveTactCalculateCoordinates(int elapsed)
    {
        var vid = Vid;
        var next = Position;
        IsBlocked = false;

        if (this is { Command: Command.Move, IsMoving: false })
        {
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Text, 0, "Move without ().StartMove()"u8);
            StartMove();
        }

        if (this is { Command: Command.Move, HasGoal: false })
        {
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Text, 0, "Move without goal"u8);
            Stop();
        }

        Speed = Accelerate(Vid, Speed, MaxSpeed, IsMoving, elapsed);

        if (Speed >= Vid.Unlimited)
        {
            if (HasGoal && this is not { HasReachedX: true, HasReachedY: true })
            {
                var impact = GoalPosition;
                bool hit = DirectRouteIntersection(ref impact);
                ChangeCoor(impact);
                IsBlocked = hit;
                HasReachedX = true;
                HasReachedY = true;

                return impact;
            }

            HasReachedX = true;
            HasReachedY = true;
        }
        else if (!Speed.IsExactlyZero() || !Map.Session.Graphics.WindSpeed.IsExactlyZero() && vid.Properties.HasFlag(ObjectProperties.Wind))
        {
            var direction = vid.Properties.HasFlag(ObjectProperties.Homing) && HasGoal
                ? Angle.FromOffset((GoalPosition - Position).AsVector2()) : Direction;
            next = Displace(next, direction, Speed, elapsed);

            if (vid.Properties.HasFlag(ObjectProperties.Wind))
            {
                next = Displace(next, Map.Session.Graphics.WindDirection, Map.Session.Graphics.WindSpeed, elapsed);
            }
        }

        if (vid.Properties.HasFlag(ObjectProperties.Gravity))
        {
            VerticalSpeed -= elapsed * Map.Constant.Gravitation;
        }
        else if (vid.Properties.HasFlag(ObjectProperties.Gravity2))
        {
            VerticalSpeed -= elapsed * Map.Constant.Gravitation2;
        }

        next.Z = elapsed * VerticalSpeed + next.Z;

        if (HasGoal)
        {
            UpdateReach(Position, next, GoalPosition.AsVector2());
        }

        return next;
    }

    private static float Accelerate(Vid vid, float speed, float maxSpeed, bool moving, int elapsed)
    {

        if (!moving)
        {
            if (speed <= 0)
                return speed;

            if (vid.Deceleration.IsExactly(Vid.Unlimited))
                return 0;

            float slowed = speed - elapsed * vid.Deceleration;

            return slowed < 0 ? 0 : slowed;
        }

        if (speed >= maxSpeed)
            return speed;

        if (vid.Acceleration.IsExactly(Vid.Unlimited))
            return maxSpeed;

        float faster = elapsed * vid.Acceleration + speed;

        return faster < maxSpeed ? faster : maxSpeed;
    }

    private static Vector3 Displace(Vector3 position, Angle direction, float speed, int elapsed)
    {
        float distance = elapsed * speed;
        return position with
        {
            X = direction.Vector.X * distance + position.X,
            Y = direction.Vector.Y * distance + position.Y
        };
    }
}
