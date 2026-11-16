using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class UnitKinematics
{
    private const float BallisticRise = 1.1f;
    private const float BallisticDrop = 0.9f;
    private const UnitCommand FirstTargetlessCommand = (UnitCommand)0x10;

    private readonly SpriteCatalog _definitions;
    private readonly GameWorld _world;

    public UnitKinematics(SpriteCatalog definitions, GameWorld world)
    {
        _definitions = definitions;
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
    public static bool ForgetObject(Unit unit, Unit removed)
    {
        bool builtGoal = false;

        if (unit.Goal == removed)
        {
            builtGoal = unit.Animation == SpriteAnimation.Fight;

            if (builtGoal)
            {
                SetCommand(unit, UnitCommand.OneAttack, point: removed.Position);
            }
            else
            {
                SetCommand(unit, UnitCommand.None);
            }
        }

        var orders = unit.Orders;
        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];

            if (order.Target == removed && UnitOrder.NamesSprite(order.Code))
            {
                orders.Replace(i, order with { Act = UnitOrder.ForgottenOrder, Var1 = 0, Target = null });
            }
        }

        return builtGoal;
    }

    [EngineRoutine(0x004489e0)]
    public static int ForgetCannonGoal(Unit cannon, Unit removed)
    {
        int built = 0;

        if (cannon.Goal == removed)
        {
            cannon.Goal = null;
            cannon.GoalPoint = removed.Position;
            built++;
        }

        return ForgetObject(cannon, removed) ? built + 1 : built;
    }

    [EngineRoutine(0x004447b0)]
    public void SetMoveTarget(Unit unit, Unit? target, Vector3? point)
    {
        if (!SetCommand(unit, UnitCommand.Move, target, point) && !StartMove(unit))
        {
            SetCommand(unit, UnitCommand.None);

            return;
        }

        var link = unit.Link;

        if (link is not null && link.Definition == unit.Definition.LinkDefinition && link.Definition.FightChild != 0 && link.Definition.Object.WeaponIndex != 0 &&
            link.Command is not (UnitCommand.None or UnitCommand.OneAttack))
        {
            SetCommand(link, UnitCommand.None);
        }
    }

    [EngineRoutine(0x004443f0)]
    public bool StartMove(Unit unit)
    {
        if (unit.MaxSpeed.IsZero())
            return false;

        ref readonly var obj = ref unit.Definition.Object;

        if (unit.HasGoal)
        {
            var offset = unit.GoalPosition - unit.Position;

            if (offset is { X: 0, Y: 0 })
                return false;

            int distance = 0;

            if (unit.Definition.Layout.Directions == 1 || obj.Properties.HasFlag(ObjectProperties.RandomBirth))
            {
                unit.SetDirection(Angle.Aim(offset.AsVector2(), out distance));
            }

            if (!obj.SpriteType.HasFlag(SpriteType.Cannon) && obj.SpriteClass != SpriteClass.Cannon)
            {
                unit.VerticalSpeed = offset.Z > 0 ? unit.Definition.ZSpeed : offset.Z < 0 ? -unit.Definition.ZSpeed : 0;
            }
            else
            {
                if (distance == 0)
                {
                    Angle.Aim(offset.AsVector2(), out distance);
                }

                if (distance == 0)
                    return false;

                unit.VerticalSpeed = GetLaunchSpeed(unit.Definition, offset.Z, distance);
            }
        }

        unit.StartMoving(unit.MaxSpeed);

        return true;
    }

    [EngineRoutine(0x00414a80)]
    private float GetLaunchSpeed(SpriteDefinition definition, float rise, float distance)
    {
        ref readonly var obj = ref definition.Object;
        float gravity = obj.Properties.HasFlag(ObjectProperties.Gravity) ? _definitions.Gravitation : obj.Properties.HasFlag(ObjectProperties.Gravity2) ? _definitions.Gravitation2 : 0;
        float speed;

        if (!gravity.IsZero())
        {
            speed = rise * definition.Speed / distance + distance * gravity / definition.Speed * 0.5f;
            speed *= speed > 0 ? BallisticRise : BallisticDrop;
        }
        else
        {
            speed = obj.Properties.HasFlag(ObjectProperties.SelfMoving) ? definition.ZSpeed : obj.TopZ.IsZero() ? rise * definition.Speed / distance : 0;
        }

        return Math.Clamp(speed, -definition.ZSpeed, definition.ZSpeed);
    }

    [EngineRoutine(0x0043fb10)]
    public Vector3 Move(Unit unit, int elapsed)
    {
        ref readonly var obj = ref unit.Definition.Object;
        var next = unit.Position;
        unit.IsBlocked = false;

        if (unit is { Command: UnitCommand.Move, IsMoving: false })
        {
            StartMove(unit);
        }

        if (unit is { Command: UnitCommand.Move, HasGoal: false })
        {
            StopMoving(unit);
        }

        unit.Speed = Accelerate(unit.Definition, unit.Speed, unit.MaxSpeed, unit.IsMoving, elapsed);

        if (unit.Speed >= SpriteDefinition.Unlimited)
        {
            if (unit.HasGoal && unit is not { HasReachedX: true, HasReachedY: true })
            {
                var impact = _world.Projectiles.Trace(unit.Definition, unit.Position, unit.GoalPosition, out bool hit);
                unit.Position = impact;
                unit.IsBlocked = hit;
                unit.HasReachedX = true;
                unit.HasReachedY = true;

                return impact;
            }

            unit.HasReachedX = true;
            unit.HasReachedY = true;
        }
        else if (!unit.Speed.IsZero())
        {
            var direction = obj.Properties.HasFlag(ObjectProperties.Homing) && unit.HasGoal
                ? Angle.FromOffset((unit.GoalPosition - unit.Position).AsVector2()) : unit.Direction;
            next = Displace(next, direction, unit.Speed, elapsed);
        }

        if (obj.Properties.HasFlag(ObjectProperties.Gravity))
        {
            unit.VerticalSpeed -= elapsed * _definitions.Gravitation;
        }
        else if (obj.Properties.HasFlag(ObjectProperties.Gravity2))
        {
            unit.VerticalSpeed -= elapsed * _definitions.Gravitation2;
        }

        next.Z = elapsed * unit.VerticalSpeed + next.Z;

        if (unit.HasGoal)
        {
            unit.UpdateReach(unit.Position, next, unit.GoalPosition.AsVector2());
        }

        return next;
    }

    public static float Accelerate(SpriteDefinition definition, float speed, float maxSpeed, bool moving, int elapsed)
    {
        ref readonly var obj = ref definition.Object;

        if (!moving)
        {
            if (speed > 0)
            {
                speed = obj.Deceleration.IsNear(SpriteDefinition.Unlimited) ? 0 : MathF.Max(0, speed - elapsed * definition.Deceleration);
            }

            return speed;
        }

        if (speed >= maxSpeed)
            return speed;

        if (!obj.Acceleration.IsNear(SpriteDefinition.Unlimited))
        {
            speed = elapsed * definition.Acceleration + speed;
        }

        return obj.Acceleration.IsNear(SpriteDefinition.Unlimited) || speed >= maxSpeed ? maxSpeed : speed;
    }

    public static Vector3 Displace(Vector3 position, Angle direction, float speed, int elapsed)
    {
        float distance = elapsed * speed;
        var vector = direction.Vector;

        return position with
        {
            X = vector.X * distance + position.X,
            Y = vector.Y * distance + position.Y
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

        if (unit.Definition.Object.Deceleration.IsNear(SpriteDefinition.Unlimited))
        {
            unit.Speed = 0;
        }
    }
}
