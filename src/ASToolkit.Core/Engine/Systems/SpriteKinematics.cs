using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class SpriteKinematics
{
    private const Command FirstTargetlessCommand = (Command)0x10;

    private readonly SpriteCatalog _definitions;
    private readonly Map _map;

    public SpriteKinematics(SpriteCatalog definitions, Map map)
    {
        _definitions = definitions;
        _map = map;
    }

    [EngineRoutine(0x004446c0)]
    public static bool SetCommand(Sprite sprite, Command command, Sprite? target = null, Vector3? point = null)
    {
        if (sprite.Command == Command.Pause && command != Command.Pause)
        {
            sprite.Timer = 0;
        }

        sprite.Goal = target;
        sprite.GoalPoint = target is null ? point : null;

        var link = sprite.ArmedLink;

        if (link is not null && link.Definition.FightChild != 0)
        {
            SetCommand(link, command, target, point);
        }

        if (command < FirstTargetlessCommand && !sprite.HasGoal)
        {
            sprite.Command = Command.None;

            return true;
        }

        sprite.Command = command;

        return false;
    }

    [EngineRoutine(0x00444750)]
    public static bool SetCommandWithoutLink(Sprite sprite, Command command, Sprite? target = null, Vector3? point = null)
    {
        if (sprite.Command == Command.Pause && command != Command.Pause)
        {
            sprite.Timer = 0;
        }

        sprite.Goal = target;
        sprite.GoalPoint = target is null ? point : null;

        if (command < FirstTargetlessCommand && !sprite.HasGoal)
        {
            sprite.Command = Command.None;

            return true;
        }

        sprite.Command = command;

        return false;
    }

    [EngineRoutine(0x0043f720)]
    public static bool ForgetObject(Sprite sprite, Sprite removed)
    {
        bool builtGoal = false;

        if (sprite.Goal == removed)
        {
            builtGoal = sprite.Animation == SpriteAnimation.Fight;

            if (builtGoal)
            {
                SetCommand(sprite, Command.OneAttack, point: removed.Position);
            }
            else
            {
                SetCommand(sprite, Command.None);
            }
        }

        var orders = sprite.ActionStack;
        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];

            if (order.Target == removed && Act.NamesSprite(order.Code))
            {
                orders.Replace(i, order with { Value = Act.ForgottenOrder, Var1 = 0, Target = null });
            }
        }

        return builtGoal;
    }

    [EngineRoutine(0x004489e0)]
    public static int ForgetCannonGoal(Sprite cannon, Sprite removed)
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
    public void SetMoveTarget(Sprite sprite, Sprite? target, Vector3? point)
    {
        if (!SetCommandWithoutLink(sprite, Command.Move, target, point) && !StartMove(sprite))
        {
            SetCommand(sprite, Command.None);

            return;
        }

        var link = sprite.Link;

        if (link is not null && link.Definition == sprite.Definition.LinkDefinition && link.Definition.FightChild != 0 && link.Definition.Object.WeaponIndex != 0 &&
            link.Command is not (Command.None or Command.OneAttack))
        {
            SetCommand(link, Command.None);
        }
    }

    [EngineRoutine(0x004443f0)]
    public bool StartMove(Sprite sprite)
    {
        if (sprite.MaxSpeed.IsZero())
            return false;

        ref readonly var obj = ref sprite.Definition.Object;

        if (sprite.HasGoal)
        {
            var offset = sprite.GoalPosition - sprite.Position;

            if (offset is { X: 0, Y: 0 })
                return false;

            int distance = 0;

            if (sprite.Definition.Layout.Directions == 1 || obj.Properties.HasFlag(ObjectProperties.RandomBirth))
            {
                sprite.ChangeDirection(Angle.Aim(offset.AsVector2(), out distance));
            }

            if (!obj.SpriteType.HasFlag(SpriteType.Cannon) && obj.SpriteClass != SpriteClass.Cannon)
            {
                sprite.VerticalSpeed = offset.Z > 0 ? sprite.Definition.ZSpeed : offset.Z < 0 ? -sprite.Definition.ZSpeed : 0;
            }
            else
            {
                if (distance == 0)
                {
                    Angle.Aim(offset.AsVector2(), out distance);
                }

                if (distance == 0)
                    return false;

                sprite.VerticalSpeed = sprite.Definition.CalculateZSpeed(_definitions, offset.Z, distance);
            }
        }

        sprite.IsMoving = true;
        sprite.HasReachedX = false;
        sprite.HasReachedY = false;

        if (sprite.Animation < SpriteAnimation.Death && sprite.Animation != SpriteAnimation.StartMove)
        {
            sprite.ChangeAnimation(sprite.Speed.IsZero() ? SpriteAnimation.StartMove : SpriteAnimation.Go);
        }

        if (obj.Acceleration.IsNear(SpriteDefinition.Unlimited))
        {
            sprite.Speed = sprite.MaxSpeed;
        }

        return true;
    }

    [EngineRoutine(0x0043fb10)]
    public Vector3 Move(Sprite sprite, int elapsed)
    {
        ref readonly var obj = ref sprite.Definition.Object;
        var next = sprite.Position;
        sprite.IsBlocked = false;

        if (sprite is { Command: Command.Move, IsMoving: false })
        {
            StartMove(sprite);
        }

        if (sprite is { Command: Command.Move, HasGoal: false })
        {
            Stop(sprite);
        }

        sprite.Speed = Accelerate(sprite.Definition, sprite.Speed, sprite.MaxSpeed, sprite.IsMoving, elapsed);

        if (sprite.Speed >= SpriteDefinition.Unlimited)
        {
            if (sprite.HasGoal && sprite is not { HasReachedX: true, HasReachedY: true })
            {
                var impact = _map.Projectiles.Trace(sprite.Definition, sprite.Position, sprite.GoalPosition, out bool hit);
                sprite.ChangeCoor(impact);
                sprite.IsBlocked = hit;
                sprite.HasReachedX = true;
                sprite.HasReachedY = true;

                return impact;
            }

            sprite.HasReachedX = true;
            sprite.HasReachedY = true;
        }
        else if (!sprite.Speed.IsZero())
        {
            var direction = obj.Properties.HasFlag(ObjectProperties.Homing) && sprite.HasGoal
                ? Angle.FromOffset((sprite.GoalPosition - sprite.Position).AsVector2()) : sprite.Direction;
            next = Displace(next, direction, sprite.Speed, elapsed);
        }

        if (obj.Properties.HasFlag(ObjectProperties.Gravity))
        {
            sprite.VerticalSpeed -= elapsed * _definitions.Gravitation;
        }
        else if (obj.Properties.HasFlag(ObjectProperties.Gravity2))
        {
            sprite.VerticalSpeed -= elapsed * _definitions.Gravitation2;
        }

        next.Z = elapsed * sprite.VerticalSpeed + next.Z;

        if (sprite.HasGoal)
        {
            sprite.UpdateReach(sprite.Position, next, sprite.GoalPosition.AsVector2());
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
    public static void Stop(Sprite sprite)
    {
        if (sprite.Command is Command.None or Command.Move)
        {
            SetCommandWithoutLink(sprite, Command.None);
        }

        sprite.ClearMove();
        sprite.VerticalSpeed = 0;

        if (sprite.Definition.Object.Deceleration.IsNear(SpriteDefinition.Unlimited))
        {
            sprite.Speed = 0;
        }
    }
}
