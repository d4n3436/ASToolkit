using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class UnitKinematics
{
    private const float BallisticRise = 1.1f;
    private const float BallisticDrop = 0.9f;
    private const UnitCommand FirstTargetlessCommand = (UnitCommand)0x10;

    private readonly ObjectCatalog _kinds;
    private readonly GameWorld _world;

    public UnitKinematics(ObjectCatalog kinds, GameWorld world)
    {
        _kinds = kinds;
        _world = world;
    }

    [EngineRoutine(0x004446c0)]
    [EngineRoutine(0x00444750)]
    public static bool SetCommand(Unit unit, UnitCommand command, Unit? target = null, Vector3? point = null)
    {
        if (unit.Command == UnitCommand.Pause && command != UnitCommand.Pause)
        {
            unit.Timer = 0;
        }

        unit.Goal = target;
        unit.GoalPoint = target is null ? point : null;

        if (command < FirstTargetlessCommand && !unit.HasGoal)
        {
            unit.Command = UnitCommand.None;

            return true;
        }

        unit.Command = command;

        return false;
    }

    [EngineRoutine(0x0043f720)]
    public static void ForgetObject(Unit unit, Unit removed)
    {
        if (unit.Goal == removed)
        {
            if (unit.Animation == SpriteAnimation.Fight)
            {
                SetCommand(unit, UnitCommand.OneAttack, point: removed.Position);
            }
            else
            {
                SetCommand(unit, UnitCommand.None);
            }
        }

        var orders = unit.Orders;
        for (int index = 0; index < orders.Length; index++)
        {
            if (orders[index].Target == removed)
            {
                orders[index] = default;
            }
        }
    }

    [EngineRoutine(0x004489e0)]
    public static void ForgetCannonGoal(Unit cannon, Unit removed)
    {
        if (cannon.Goal == removed)
        {
            cannon.Goal = null;
            cannon.GoalPoint = removed.Position;
        }

        ForgetObject(cannon, removed);
    }

    [EngineRoutine(0x004443f0)]
    public bool StartMove(Unit unit)
    {
        if (unit.MaxSpeed.IsZero())
            return false;

        ref readonly var obj = ref unit.Kind.Object;

        if (unit.HasGoal)
        {
            var offset = unit.GoalPosition - unit.Position;

            if (offset is { X: 0, Y: 0 })
                return false;

            int distance = 0;

            if (unit.Kind.Layout.Directions == 1 || obj.Properties.HasFlag(ObjectProperties.RandomBirth))
            {
                unit.SetFacing(Facing.Aim(offset.AsVector2(), out distance));
            }

            if (!obj.SpriteType.HasFlag(SpriteType.Cannon) && obj.SpriteClass != SpriteClass.Cannon)
            {
                unit.VerticalSpeed = offset.Z > 0 ? unit.Kind.ZSpeed : offset.Z < 0 ? -unit.Kind.ZSpeed : 0;
            }
            else
            {
                if (distance == 0)
                {
                    Facing.Aim(offset.AsVector2(), out distance);
                }

                if (distance == 0)
                    return false;

                unit.VerticalSpeed = GetLaunchSpeed(unit.Kind, offset.Z, distance);
            }
        }

        unit.StartMoving(unit.MaxSpeed);

        return true;
    }

    [EngineRoutine(0x00414a80)]
    private float GetLaunchSpeed(ObjectDefinition kind, float rise, float distance)
    {
        ref readonly var obj = ref kind.Object;
        float gravity = obj.Properties.HasFlag(ObjectProperties.Gravity) ? _kinds.Gravitation : obj.Properties.HasFlag(ObjectProperties.Gravity2) ? _kinds.Gravitation2 : 0;
        float speed;

        if (!gravity.IsZero())
        {
            speed = rise * kind.Speed / distance + distance * gravity / kind.Speed * 0.5f;
            speed *= speed > 0 ? BallisticRise : BallisticDrop;
        }
        else
        {
            speed = obj.Properties.HasFlag(ObjectProperties.SelfMoving) ? kind.ZSpeed : obj.TopZ.IsZero() ? rise * kind.Speed / distance : 0;
        }

        return Math.Clamp(speed, -kind.ZSpeed, kind.ZSpeed);
    }

    [EngineRoutine(0x0043fb10)]
    public Vector3 Move(Unit unit, int elapsed)
    {
        ref readonly var obj = ref unit.Kind.Object;
        var next = unit.Position;
        unit.Blocked = false;

        if (unit is { Command: UnitCommand.Move, Moving: false })
        {
            StartMove(unit);
        }

        if (unit is { Command: UnitCommand.Move, HasGoal: false })
        {
            StopMoving(unit);
        }

        unit.Speed = Accelerate(unit.Kind, unit.Speed, unit.MaxSpeed, unit.Moving, elapsed);

        if (unit.Speed >= ObjectDefinition.Unlimited)
        {
            if (unit.HasGoal && unit is not { ReachedX: true, ReachedY: true })
            {
                var impact = _world.Projectiles.Trace(unit.Kind, unit.Position, unit.GoalPosition, out bool hit);
                unit.Position = impact;
                unit.Blocked = hit;
                unit.ReachedX = true;
                unit.ReachedY = true;

                return impact;
            }

            unit.ReachedX = true;
            unit.ReachedY = true;
        }
        else if (!unit.Speed.IsZero())
        {
            var facing = obj.Properties.HasFlag(ObjectProperties.Homing) && unit.HasGoal
                ? Facing.FromOffset((unit.GoalPosition - unit.Position).AsVector2()) : unit.Facing;
            next = Displace(next, facing, unit.Speed, elapsed);
        }

        if (obj.Properties.HasFlag(ObjectProperties.Gravity))
        {
            unit.VerticalSpeed -= elapsed * _kinds.Gravitation;
        }
        else if (obj.Properties.HasFlag(ObjectProperties.Gravity2))
        {
            unit.VerticalSpeed -= elapsed * _kinds.Gravitation2;
        }

        next.Z = elapsed * unit.VerticalSpeed + next.Z;

        if (unit.HasGoal)
        {
            unit.UpdateReach(unit.Position, next, unit.GoalPosition.AsVector2());
        }

        return next;
    }

    public static float Accelerate(ObjectDefinition kind, float speed, float maxSpeed, bool moving, int elapsed)
    {
        ref readonly var obj = ref kind.Object;

        if (!moving)
        {
            if (speed > 0)
            {
                speed = obj.Deceleration.IsNear(ObjectDefinition.Unlimited) ? 0 : MathF.Max(0, speed - elapsed * kind.Deceleration);
            }

            return speed;
        }

        if (speed >= maxSpeed)
            return speed;

        if (!obj.Acceleration.IsNear(ObjectDefinition.Unlimited))
        {
            speed = elapsed * kind.Acceleration + speed;
        }

        return obj.Acceleration.IsNear(ObjectDefinition.Unlimited) || speed >= maxSpeed ? maxSpeed : speed;
    }

    public static Vector3 Displace(Vector3 position, Facing facing, float speed, int elapsed)
    {
        float distance = elapsed * speed;
        var direction = facing.Direction;

        return position with
        {
            X = direction.X * distance + position.X,
            Y = direction.Y * distance + position.Y
        };
    }

    [EngineRoutine(0x004445d0)]
    public static void StopMoving(Unit unit)
    {
        if (unit.Command is UnitCommand.None or UnitCommand.Move)
        {
            SetCommand(unit, UnitCommand.None);
        }

        unit.ClearMove();
        unit.VerticalSpeed = 0;

        if (unit.Kind.Object.Deceleration.IsNear(ObjectDefinition.Unlimited))
        {
            unit.Speed = 0;
        }
    }
}
