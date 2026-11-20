using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class Player
{
    private const int FirstWeaponKey = '0';
    private const int LastWeaponKey = '9';
    private const int StartMoney = 1000;
    private const int MessageLineId = 4;
    private const int NoMarkerId = -1;
    private const int MessageLines = 5;
    private const int MessageLifetime = 5000;
    private static readonly Vector2 MessageOrigin = new(398, 388);
    private static readonly Angle HeadingUpRight = new(0x20 + 8);
    private static readonly Angle HeadingUpLeft = new(0xE0 - 8);
    private static readonly Angle HeadingDownRight = new(0x60 - 8);
    private static readonly Angle HeadingDownLeft = new(0xA0 + 8);
    private readonly Map _map;
    private readonly SpriteCollision _collision;
    private readonly ControlSettings _settings;

    [EngineRoutine(0x00413060)]
    public Player(Map map, SpriteCollision collision, ControlSettings settings)
    {
        _map = map;
        _collision = collision;
        _settings = settings;
        Money = StartMoney;
        Messages = new MessageWindow(map, MessageLineId, NoMarkerId, MessageOrigin, MessageLines, MessageLifetime);
    }

    public Sprite? Sprite { get; private set; }

    public MessageWindow Messages { get; }

    public int Money { get; set; }

    [EngineRoutine(0x00413140)]
    public void SetSprite(Sprite? sprite) => Sprite = sprite;

    [EngineRoutine(0x0043e9f0)]
    public void Forget(Sprite removed) => ForgetSprite(removed);

    [EngineRoutine(0x00413280)]
    private void ForgetSprite(Sprite removed)
    {
        if (Sprite == removed)
        {
            Sprite = null;
        }
    }

    [EngineRoutine(0x0043ea10)]
    public void Control(PlayerInput input)
    {
        PropagateGroupCommands();
        Messages.Tact();

        var player = Sprite;

        if (player is null)
            return;

        var weapons = _map.Weapons;
        int elapsed = _map.FrameTime;

        if (player.Definition.Object.SpriteClass != SpriteClass.Man)
            return;

        var flags = input.Flags;

        if (flags.HasFlag(InputFlags.Second))
        {
            var point = input.Cursor with { Y = input.Cursor.Y + _map.Movement.GetHeight(input.Cursor) };
            _map.Action(player, ActionCode.Move, (int)point.X, (int)point.Y);
        }

        if ((flags & InputFlags.Movement) != 0 && player.Command == Command.Move)
        {
            SpriteKinematics.SetCommandWithoutLink(player, Command.None);
        }

        if (flags.HasFlag(InputFlags.First))
        {
            if (player.Rounds < player.Definition.ChildCounts[(int)SpriteAnimation.Fight])
            {
                for (int slot = (int)player.Link!.Definition.Id - Sprite.WeaponIdBase - 1; !weapons.SelectWeapon(slot); slot--)
                {
                }
            }

            _map.Action(player, ActionCode.CoorAttack, (int)input.Cursor.X, (int)input.Cursor.Y);
        }

        if (input.Key is >= FirstWeaponKey and <= LastWeaponKey)
        {
            weapons.SelectWeapon(input.Key - FirstWeaponKey);
        }

        StepWeapon(input);

        var cursorDirection = GetCursorDirection(player, input.Cursor);
        var origin = _settings.IsMovementRelativeToCursor ? cursorDirection : default;

        if (!Steer(player, flags, origin, elapsed) && player.Command != Command.Move)
        {
            SpriteKinematics.Stop(player);
        }

        if ((flags & InputFlags.Movement) != 0)
        {
            _map.Motion.StartMove(player);
        }

        if (player.Link is not null && player.Link.Definition == player.Definition.LinkDefinition)
        {
            weapons.TrackCursor(cursorDirection, elapsed);
        }
    }

    private void PropagateGroupCommands()
    {
        var groups = _map.Groups;
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var members = groups[i].Members;
            Sprite? target = null;
            var command = Command.None;
            bool hasIdle = false;
            foreach (var member in members)
            {
                if (member.Command == Command.None)
                {
                    hasIdle = true;
                }

                if (member.Command is Command.Attack or Command.OneAttack)
                {
                    target = member.Goal;
                    command = member.Command;
                }
            }

            if (target is null || command == Command.None || !hasIdle)
                continue;

            foreach (var member in members)
            {
                if (member.Command == Command.None)
                {
                    SpriteKinematics.SetCommand(member, command, target);
                }
            }
        }
    }

    private void StepWeapon(PlayerInput input)
    {
        var weapons = _map.Weapons;
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
            while (slot < Sprite.WeaponSlots)
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

    private bool Steer(Sprite player, InputFlags flags, Angle origin, int elapsed)
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
            goal = _collision.RotateForGlide(goal, player);
        }

        player.RotateTact(goal, elapsed);

        return true;
    }

    private static Angle GetCursorDirection(Sprite player, Vector2 cursor) =>
        Angle.Aim(new Vector2(cursor.X - player.Position.X, player.Position.Z + cursor.Y - player.Position.Y));
}
