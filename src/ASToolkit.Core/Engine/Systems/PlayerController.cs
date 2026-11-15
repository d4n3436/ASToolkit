using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerController
{
    private const int FirstWeaponKey = '0';
    private const int LastWeaponKey = '9';
    private static readonly Angle HeadingUpRight = new(0x20 + 8);
    private static readonly Angle HeadingUpLeft = new(0xE0 - 8);
    private static readonly Angle HeadingDownRight = new(0x60 - 8);
    private static readonly Angle HeadingDownLeft = new(0xA0 + 8);
    private readonly GameWorld _world;
    private readonly UnitCollision _collision;
    private readonly ControlSettings _settings;
    private readonly bool _hasUnit;

    public PlayerController(GameWorld world, UnitCollision collision, ControlSettings settings, bool hasUnit)
    {
        _world = world;
        _collision = collision;
        _settings = settings;
        _hasUnit = hasUnit;
    }

    [EngineRoutine(0x0043ea10)]
    public void Control(PlayerInput input)
    {
        PropagateGroupCommands();

        if (!_hasUnit)
            return;

        var player = _world.Player;
        var weapons = _world.Weapons;
        int elapsed = _world.FrameTime;

        if (player.Kind.Object.SpriteClass != SpriteClass.Man)
            return;

        var flags = input.Flags;

        if (flags.HasFlag(InputFlags.Second))
        {
            var point = input.Cursor with { Y = input.Cursor.Y + _world.Movement.GetHeight(input.Cursor) };
            _world.Action(player, ActionCode.Move, (int)point.X, (int)point.Y);
        }

        if ((flags & InputFlags.Movement) != 0 && player.Command == UnitCommand.Move)
        {
            UnitKinematics.SetCommand(player, UnitCommand.None);
        }

        if (flags.HasFlag(InputFlags.First))
        {
            if (player.Rounds < player.Kind.Object.ChildCounts[(int)SpriteAnimation.Fight])
            {
                for (int slot = (int)player.Link!.Kind.Id - Unit.WeaponKindBase - 1; !weapons.SelectWeapon(slot); slot--)
                {
                }
            }

            _world.Action(player, ActionCode.CoorAttack, (int)input.Cursor.X, (int)input.Cursor.Y);
        }

        if (input.Key is >= FirstWeaponKey and <= LastWeaponKey)
        {
            weapons.SelectWeapon(input.Key - FirstWeaponKey);
        }

        StepWeapon(input);

        var cursorDirection = GetCursorDirection(player, input.Cursor);
        var origin = _settings.Relative ? cursorDirection : default;

        if (!Steer(player, flags, origin, elapsed) && player.Command != UnitCommand.Move)
        {
            UnitKinematics.StopMoving(player);
        }

        if ((flags & InputFlags.Movement) != 0)
        {
            _world.Motion.StartMove(player);
        }

        if (player.Link is not null && player.Link.Kind == player.Kind.LinkKind)
        {
            weapons.TrackCursor(cursorDirection, elapsed);
        }
    }

    private void PropagateGroupCommands()
    {
        var groups = _world.Groups;
        for (int index = groups.Count - 1; index >= 0; index--)
        {
            var members = groups[index].Members;
            Unit? target = null;
            var command = UnitCommand.None;
            bool hasIdle = false;
            foreach (var member in members)
            {
                if (member.Command == UnitCommand.None)
                {
                    hasIdle = true;
                }

                if (member.Command is UnitCommand.Attack or UnitCommand.OneAttack)
                {
                    target = member.Goal;
                    command = member.Command;
                }
            }

            if (target is null || command == UnitCommand.None || !hasIdle)
                continue;

            for (int member = 0; member < members.Count; member++)
            {
                if (members[member].Command == UnitCommand.None)
                {
                    UnitKinematics.SetCommand(members[member], command, target);
                }
            }
        }
    }

    private void StepWeapon(PlayerInput input)
    {
        var weapons = _world.Weapons;
        int slot = weapons.Slot;
        int notches = input.Wheel;

        if (notches == 0)
        {
            if (input.VirtualKey == _settings.Next)
            {
                notches = 1;
            }
            else if (input.VirtualKey == _settings.Prev)
            {
                notches = -1;
            }
        }

        for (; notches > 0; notches--)
        {
            while (slot < PlayerInventory.WeaponSlots)
            {
                slot++;

                if (weapons.SelectWeapon(slot))
                    break;
            }
        }

        if (notches < 0)
        {
            while (slot >= 1)
            {
                slot--;

                if (weapons.SelectWeapon(slot))
                    break;
            }
        }
    }

    private bool Steer(Unit player, InputFlags flags, Angle origin, int elapsed)
    {
        bool up = flags.HasFlag(InputFlags.Up);
        bool down = flags.HasFlag(InputFlags.Down);
        bool left = flags.HasFlag(InputFlags.Left);
        bool right = flags.HasFlag(InputFlags.Right);
        Angle heading;
        bool slides = true;

        if (up && right)
        {
            heading = HeadingUpRight;
            slides = false;
        }
        else if (up && left)
        {
            heading = HeadingUpLeft;
            slides = false;
        }
        else if (down && right)
        {
            heading = HeadingDownRight;
            slides = false;
        }
        else if (down && left)
        {
            heading = HeadingDownLeft;
            slides = false;
        }
        else if (left)
        {
            heading = Angle.West;
        }
        else if (right)
        {
            heading = Angle.East;
        }
        else if (down)
        {
            heading = Angle.South;
        }
        else if (up)
        {
            heading = Angle.North;
        }
        else
        {
            return false;
        }

        var goal = origin + heading.Value;

        if (slides)
        {
            goal = _collision.AvoidObstacles(goal, player);
        }

        player.SetDirection(player.Direction.TurnToward(goal, player.Kind.Object.RotationPeriod, elapsed, out _));

        return true;
    }

    private static Angle GetCursorDirection(Unit player, Vector2 cursor) =>
        Angle.Aim(new Vector2(cursor.X - player.Position.X, player.Position.Z + cursor.Y - player.Position.Y));
}
