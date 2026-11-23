using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class Player
{
    private const int FirstWeaponKey = '0';
    private const int LastWeaponKey = '9';
    private const int StartMoney = 1000;
    private const int MessageLineId = 4;
    private const int NoMarkerId = -1;
    private const int MessageLines = 5;
    private const int MessageLifetime = 5000;
    private const float MessageRightMargin = 242;
    private const float MessageBottomMargin = 92;
    private const int MessageLineStep = 1;

    private static readonly Vector2 MessageDefaultOrigin = new(398, 388);
    private static readonly Angle HeadingUpRight = new(0x20 + 8);
    private static readonly Angle HeadingUpLeft = new(0xE0 - 8);
    private static readonly Angle HeadingDownRight = new(0x60 - 8);
    private static readonly Angle HeadingDownLeft = new(0xA0 + 8);
    private readonly Map _map;
    private readonly ControlSettings _settings;

    [EngineRoutine(0x00413060)]
    public Player(Map map, ControlSettings settings)
    {
        _map = map;
        _settings = settings;
        Money = StartMoney;
        var screen = map.Session.ScreenSize;
        Messages = new MessageWindow(map, MessageLineId, NoMarkerId, MessageDefaultOrigin, MessageLines, MessageLifetime)
        {
            Origin = new Vector2(screen.Width - MessageRightMargin, screen.Height - MessageBottomMargin),
            LineStep = MessageLineStep
        };
    }

    public Sprite? Sprite { get; private set; }

    public Sprite? Hovered { get; set; }

    public MessageWindow Messages { get; }

    [EngineRoutine(0x0043a8b0)]
    public int Money { get; set; }

    [EngineRoutine(0x00413360)]
    public void Reset()
    {
        if (Sprite is not null)
        {
            _map.Release(Sprite);
        }

        Sprite = null;

        if (Hovered is not null)
        {
            _map.Release(Hovered);
        }

        Hovered = null;
        Money = StartMoney;
    }

    [EngineRoutine(0x00413140)]
    public void SetFlagman(Sprite? sprite)
    {
        sprite?.AddRef();

        if (Sprite is not null)
        {
            _map.Release(Sprite);
        }

        Sprite = sprite;
    }

    [EngineRoutine(0x00413440)]
    public void Load(Sprite? flagman) => SetFlagman(flagman);

    [EngineRoutine(0x0043e9f0)]
    public void Forget(Sprite removed) => ForgetSprite(removed);

    [EngineRoutine(0x0043ea10)]
    public void Control(PlayerInput input)
    {
        PropagateGroupCommands();
        Messages.Tact();

        var player = Sprite;

        if (player is null)
            return;

        var weapons = _map.Weapons;

        if (player.Definition.Object.SpriteClass != SpriteClass.Man)
            return;

        var flags = input.Flags;

        if (flags.HasFlag(InputFlags.Second))
        {
            var point = input.Cursor with { Y = input.Cursor.Y + _map.Movement.GetHeight(input.Cursor) };
            _map.Action(player, ActionCode.Move, (int)point.X, (int)point.Y);
        }

        bool isSteering = flags.HasFlag(InputFlags.Left) || flags.HasFlag(InputFlags.Right) || flags.HasFlag(InputFlags.Down) || flags.HasFlag(InputFlags.Up);

        if (isSteering && player.Command == Command.Move)
        {
            SpriteKinematics.SetCommandWithoutLink(player, Command.None);
        }

        if (flags.HasFlag(InputFlags.First))
        {
            if (player.Rounds < player.Definition.ChildCounts[(int)SpriteAnimation.Fight])
            {
                for (int slot = (int)player.Link!.Definition.Id - Sprite.WeaponIdBase - 1; !weapons.ChangeWeapon(player, slot); slot--)
                {
                }
            }

            _map.Action(player, ActionCode.CoorAttack, (int)input.Cursor.X, (int)input.Cursor.Y);
        }

        if (input.Key is >= FirstWeaponKey and <= LastWeaponKey)
        {
            weapons.ChangeWeapon(player, input.Key - FirstWeaponKey);
        }

        StepWeapon(player, input);

        var cursorDirection = GetCursorDirection(player, input.Cursor);
        var origin = _settings.IsMovementRelativeToCursor ? cursorDirection : default;

        if (!Steer(player, flags, origin, _map.FrameTime) && player.Command != Command.Move)
        {
            SpriteKinematics.Stop(player);
        }

        if (isSteering)
        {
            _map.Motion.StartMove(player);
        }

        if (player.Link is not null && player.Link.Definition == player.Definition.LinkDefinition)
        {
            weapons.TrackCursor(player, cursorDirection, _map.FrameTime);
        }

        if (Hovered is not null && Hovered.Definition.Object.SpriteType.HasFlag(SpriteType.Terrain))
        {
            _map.Release(Hovered);
            Hovered = null;
        }
    }

    private static Angle GetCursorDirection(Sprite player, Vector2 cursor) =>
        Angle.FromOffset(new Vector2(cursor.X - player.Position.X, player.Position.Z + cursor.Y - player.Position.Y));

    [EngineRoutine(0x00413280)]
    private void ForgetSprite(Sprite removed)
    {
        if (Sprite == removed)
        {
            _map.Release(removed);
            Sprite = null;
        }

        if (Hovered == removed)
        {
            _map.Release(removed);
            Hovered = null;
        }
    }

    private void PropagateGroupCommands()
    {
        for (int i = _map.Groups.Count - 1; i >= 0; i--)
        {
            var members = _map.Groups[i].Members;
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

    private void StepWeapon(Sprite player, PlayerInput input)
    {
        int slot = player.WeaponSlot;
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

                if (_map.Weapons.ChangeWeapon(player, slot))
                    break;
            }
        }

        if (notches < 0)
        {
            while (slot >= 1)
            {
                slot--;

                if (_map.Weapons.ChangeWeapon(player, slot))
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
            goal = _map.Collision.RotateForGlide(goal, player);
        }

        player.RotateTact(goal, elapsed);

        return true;
    }
}
