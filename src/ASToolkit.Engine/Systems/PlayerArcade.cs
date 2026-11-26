using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class PlayerArcade : Player
{
    private const int FirstWeaponKey = '0';
    private const int LastWeaponKey = '9';
    private const int MessageLineId = 4;
    private const int NoMarkerId = -1;
    private const int MessageLines = 5;
    private const int MessageLifetime = 5000;
    private const float MessageRightMargin = 242;
    private const float MessageBottomMargin = 92;
    private const int MessageLineStep = 1;
    private const int LegSlack = 63;

    private static readonly Vector2 MessageDefaultOrigin = new(398, 388);
    private static readonly Angle HeadingUpRight = new(0x20 + 8);
    private static readonly Angle HeadingUpLeft = new(0xE0 - 8);
    private static readonly Angle HeadingDownRight = new(0x60 - 8);
    private static readonly Angle HeadingDownLeft = new(0xA0 + 8);

    [EngineRoutine(0x0043e8c0)]
    public PlayerArcade(GameSession session, int index)
        : base(session, index)
    {
        var screen = session.Graphics.ScreenSize;
        Messages = new Message(Map, MessageLineId, NoMarkerId, MessageDefaultOrigin, MessageLines, MessageLifetime)
        {
            Origin = new Vector2(screen.Width - MessageRightMargin, screen.Height - MessageBottomMargin),
            LineStep = MessageLineStep
        };
    }

    public Message Messages { get; }

    [EngineRoutine(0x0043e930)]
    public override void PutMessage(ReadOnlyMemory<byte> text, Vector2 target) => Messages.Put(text, target);

    [EngineRoutine(0x0043e950)]
    public override void Destruct()
    {
        Messages.Clear();
        base.Destruct();
    }

    [EngineRoutine(0x0043e9f0)]
    public override void Forget(Sprite removed)
    {
        Messages.DeletePointerToSprite(removed);
        base.Forget(removed);
    }

    [EngineRoutine(0x0043ea10)]
    public override void Control(Input input)
    {
        foreach (var group in Map.Groups)
        {
            Sprite? target = null;
            Sprite? lastTarget = null;
            var command = Command.None;
            bool hasIdle = false;

            foreach (var member in group)
            {
                if (member.Command == Command.None)
                {
                    hasIdle = true;
                    target = lastTarget;
                }

                if (member.Command is Command.Attack or Command.OneAttack)
                {
                    target = member.Goal;
                    command = member.Command;
                    lastTarget = target;
                }
            }

            if (target is null || command == Command.None || !hasIdle)
                continue;

            for (int j = 0; j < group.Count; j++)
            {
                if (group[j].Command == Command.None)
                {
                    group[j].SetCommand(command, target);
                }
            }
        }
        Messages.Tact();

        var player = Flagman;

        if (player is null)
            return;

        if (player.Vid.Class != SpriteClass.Man)
            return;

        var flags = input.Flags;
        if (flags.HasFlag(InputFlags.Second))
        {
            var point = input.Cursor with { Y = input.Cursor.Y + Map.GetGroundZScr(input.Cursor) };
            Map.Action(player, ActionCode.Move, (int)point.X, (int)point.Y);
        }

        if ((flags.HasFlag(InputFlags.Left) || flags.HasFlag(InputFlags.Right) || flags.HasFlag(InputFlags.Down) || flags.HasFlag(InputFlags.Up)) && Flagman!.Command == Command.Move)
        {
            Flagman.SetCommandWithoutLink(Command.None);
        }

        if (flags.HasFlag(InputFlags.First))
        {
            if (Flagman!.Vid.Class == SpriteClass.Man)
            {
                var sprite = (Man)Flagman;
                if (sprite.Rounds < sprite.Vid.ChildCounts[(int)SpriteAnimation.Fight])
                {
                    for (int slot = (int)sprite.Link!.Vid.Id - Sprite.WeaponIdBase - 1; !sprite.ChangeWeapon(slot); slot--)
                    {
                    }
                }
            }

            Map.Action(Flagman!, ActionCode.CoorAttack, (int)input.Cursor.X, (int)input.Cursor.Y);
        }

        if (input.Key is >= FirstWeaponKey and <= LastWeaponKey && Flagman!.Vid.Class == SpriteClass.Man)
        {
            ((Man)Flagman!).ChangeWeapon(input.Key - FirstWeaponKey);
        }
        int weaponSlot = Flagman!.WeaponSlot;
        int notches = input.Wheel;

        if (notches == 0)
        {
            if (input.VirtualKey == Session.Controls!.Next)
            {
                notches = 1;
            }
            else if (input.VirtualKey == Session.Controls!.Prev)
            {
                notches = -1;
            }
        }

        if (notches > 0)
        {
            do
            {
                while (weaponSlot < Sprite.WeaponSlots)
                {
                    weaponSlot++;

                    if (((Man)Flagman!).ChangeWeapon(weaponSlot))
                        break;
                }

                notches--;
            }
            while (notches != 0);
        }
        else if (notches < 0)
        {
            while (weaponSlot > 0)
            {
                weaponSlot--;

                if (((Man)Flagman!).ChangeWeapon(weaponSlot))
                    break;
            }
        }
        player = Flagman!;
        var cursorDirection = Angle.FromOffset(new Vector2(input.Cursor.X - player.Position.X, player.Position.Z + input.Cursor.Y - player.Position.Y));
        var origin = Session.Controls!.IsMovementRelativeToCursor ? cursorDirection : default;
        bool up = flags.HasFlag(InputFlags.Up);
        bool down = flags.HasFlag(InputFlags.Down);
        bool left = flags.HasFlag(InputFlags.Left);
        bool right = flags.HasFlag(InputFlags.Right);

        if (up && right)
        {
            player.RotateTact(origin + HeadingUpRight.Value, Map.FrameTime);
        }
        else if (up && left)
        {
            player.RotateTact(origin + HeadingUpLeft.Value, Map.FrameTime);
        }
        else if (down && right)
        {
            player.RotateTact(origin + HeadingDownRight.Value, Map.FrameTime);
        }
        else if (down && left)
        {
            player.RotateTact(origin + HeadingDownLeft.Value, Map.FrameTime);
        }
        else if (left)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.West.Value), Map.FrameTime);
        }
        else if (right)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.East.Value), Map.FrameTime);
        }
        else if (down)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.South.Value), Map.FrameTime);
        }
        else if (up)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.North.Value), Map.FrameTime);
        }
        else if (player.Command != Command.Move)
        {
            player.Stop();
        }
        if (flags.HasFlag(InputFlags.Left) || flags.HasFlag(InputFlags.Right) || flags.HasFlag(InputFlags.Down) || flags.HasFlag(InputFlags.Up))
        {
            player.StartMove();
        }

        var torso = player.Link;

        if (torso is not null && torso.Vid == player.Vid.LinkVid)
        {
            if (player is { Speed: 0, Vid.Id: KnownObject.PlayerStart })
            {
                if (!Session.IsTurningLegs)
                {
                    Session.IsTurningLegs = player.Direction.MeasureTurnTo(torso.Direction) > LegSlack;
                }
                else
                {
                    Session.IsTurningLegs = player.RotateTact(torso.Direction, Map.FrameTime) != default;
                }
            }

            torso.RotateTact(cursorDirection, Map.FrameTime);
        }
        if (Hovered is not null && Hovered.Vid.SpriteType.HasFlag(OldSpriteType.Terrain))
        {
            Map.Release(Hovered);
            Hovered = null;
        }
    }
}
