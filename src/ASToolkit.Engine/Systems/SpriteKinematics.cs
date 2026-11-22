using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

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

    private static ReadOnlySpan<byte> SpriteLogTag => "SPRITE %i"u8;

    [EngineRoutine(0x004446c0)]
    public static bool SetCommand(Sprite sprite, Command command, Sprite? target = null)
    {
        if (sprite.Command == Command.Pause && command != Command.Pause)
        {
            sprite.Timer = 0;
        }

        sprite.Goal = target;
        var link = sprite.ArmedLink;

        if (link is not null && link.Definition.FightChild != 0)
        {
            SetCommand(link, command, target);
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
    public static bool SetCommandWithoutLink(Sprite sprite, Command command, Sprite? target = null)
    {
        if (sprite.Command == Command.Pause && command != Command.Pause)
        {
            sprite.Timer = 0;
        }

        sprite.Goal = target;

        if (command < FirstTargetlessCommand && !sprite.HasGoal)
        {
            sprite.Command = Command.None;

            return true;
        }

        sprite.Command = command;

        return false;
    }

    public static float Accelerate(SpriteDefinition definition, float speed, float maxSpeed, bool moving, int elapsed)
    {
        ref readonly var gameObject = ref definition.Object;

        if (!moving)
        {
            if (speed <= 0)
                return speed;

            if (gameObject.Deceleration.IsExactly(SpriteDefinition.Unlimited))
                return 0;

            float slowed = speed - elapsed * definition.Deceleration;

            return slowed < 0 ? 0 : slowed;
        }

        if (speed >= maxSpeed)
            return speed;

        if (gameObject.Acceleration.IsExactly(SpriteDefinition.Unlimited))
            return maxSpeed;

        float faster = elapsed * definition.Acceleration + speed;

        return faster < maxSpeed ? faster : maxSpeed;
    }

    public static Vector3 Displace(Vector3 position, Angle direction, float speed, int elapsed)
    {
        float distance = elapsed * speed;
        return position with
        {
            X = direction.Vector.X * distance + position.X,
            Y = direction.Vector.Y * distance + position.Y
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

        if (sprite.Definition.Object.Deceleration.IsExactly(SpriteDefinition.Unlimited))
        {
            sprite.Speed = 0;
        }
    }

    public void DeletePointerToSprite(Sprite sprite, Sprite removed)
    {
        if (sprite.Definition.Object.SpriteClass == SpriteClass.Cannon)
        {
            ForgetCannonGoal(sprite, removed);
        }
        else if (sprite.Definition.Object.SpriteClass == SpriteClass.Creature)
        {
            ForgetCreatureRegion(sprite, removed);
        }
        else
        {
            ForgetObject(sprite, removed);
        }
    }

    [EngineRoutine(0x0044d460)]
    public void ForgetCreatureRegion(Sprite creature, Sprite removed)
    {
        if (creature.Region == removed)
        {
            creature.Region = null;
        }

        ForgetObject(creature, removed);
    }

    [EngineRoutine(0x0043f720)]
    public void ForgetObject(Sprite sprite, Sprite removed)
    {
        if (sprite.Link is not null)
        {
            DeletePointerToSprite(sprite.Link, removed);
        }

        if (sprite.Goal == removed)
        {
            if (sprite.Animation == SpriteAnimation.Fight)
            {
                SetCommand(sprite, Command.OneAttack, _map.ConstructGoal(removed.Position));
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

            if (order.Target == removed && Act.HasSpriteTarget((ActionCode)order.Value))
            {
                orders.Replace(i, order with { Value = Act.ForgottenOrder, Var1 = 0, Target = null });
            }
        }
    }

    [EngineRoutine(0x004489e0)]
    public void ForgetCannonGoal(Sprite cannon, Sprite removed)
    {
        if (cannon.Goal == removed)
        {
            cannon.Goal = _map.ConstructGoal(removed.Position);
        }

        ForgetObject(cannon, removed);
    }

    [EngineRoutine(0x004447b0)]
    public void SetMoveTarget(Sprite sprite, Sprite? target)
    {
        if (!SetCommandWithoutLink(sprite, Command.Move, target) && !StartMove(sprite))
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
        if (sprite.MaxSpeed.IsExactlyZero())
            return false;

        ref readonly var gameObject = ref sprite.Definition.Object;

        if (sprite.HasGoal)
        {
            var offset = sprite.GoalPosition - sprite.Position;

            if (offset.X.IsExactlyZero() && offset.Y.IsExactlyZero())
                return false;

            int distance = 0;

            if (sprite.Definition.Layout.Directions == 1 || gameObject.Properties.HasFlag(ObjectProperties.RandomBirth))
            {
                sprite.ChangeDirection(Angle.Aim(offset.AsVector2(), out distance));
            }

            if (!gameObject.SpriteType.HasFlag(SpriteType.Cannon) && gameObject.SpriteClass != SpriteClass.Cannon)
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
            sprite.ChangeAnimation(sprite.Speed.IsExactlyZero() ? SpriteAnimation.StartMove : SpriteAnimation.Go);
        }

        if (gameObject.Acceleration.IsExactly(SpriteDefinition.Unlimited))
        {
            sprite.Speed = sprite.MaxSpeed;
        }

        return true;
    }

    [EngineRoutine(0x0043fb10)]
    public Vector3 CalculateNewCoordinates(Sprite sprite, int elapsed)
    {
        ref readonly var gameObject = ref sprite.Definition.Object;
        var next = sprite.Position;
        sprite.IsBlocked = false;

        if (sprite is { Command: Command.Move, IsMoving: false })
        {
            _map.Session.Logger.Error(SpriteLogTag, LogError.Text, 0, "Move without StartMove()"u8, (int)sprite.Definition.Id);
            StartMove(sprite);
        }

        if (sprite is { Command: Command.Move, HasGoal: false })
        {
            _map.Session.Logger.Error(SpriteLogTag, LogError.Text, 0, "Move without goal"u8, (int)sprite.Definition.Id);
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
        else if (!sprite.Speed.IsExactlyZero())
        {
            var direction = gameObject.Properties.HasFlag(ObjectProperties.Homing) && sprite.HasGoal
                ? Angle.FromOffset((sprite.GoalPosition - sprite.Position).AsVector2()) : sprite.Direction;
            next = Displace(next, direction, sprite.Speed, elapsed);
        }

        if (gameObject.Properties.HasFlag(ObjectProperties.Gravity))
        {
            sprite.VerticalSpeed -= elapsed * _definitions.Gravitation;
        }
        else if (gameObject.Properties.HasFlag(ObjectProperties.Gravity2))
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
}
