using System.Globalization;
using System.Numerics;
using ASToolkit.Engine.IO;
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
    private const int LegSlack = 63;
    private const int UnitKeyLength = 16;

    private static readonly Vector2 MessageDefaultOrigin = new(398, 388);
    private static readonly Angle HeadingUpRight = new(0x20 + 8);
    private static readonly Angle HeadingUpLeft = new(0xE0 - 8);
    private static readonly Angle HeadingDownRight = new(0x60 - 8);
    private static readonly Angle HeadingDownLeft = new(0xA0 + 8);
    private readonly GameSession _session;
    private readonly Map _map;
    private readonly SpriteList _sprites = new();
    private bool _isTurningLegs;

    [EngineRoutine(0x00413060)]
    public Player(GameSession session, int index)
    {
        _session = session;
        _map = session.Map!;
        Index = index;
        Money = StartMoney;
        var screen = session.Graphics.ScreenSize;
        Messages = new Message(_map, MessageLineId, NoMarkerId, MessageDefaultOrigin, MessageLines, MessageLifetime)
        {
            Origin = new Vector2(screen.Width - MessageRightMargin, screen.Height - MessageBottomMargin),
            LineStep = MessageLineStep
        };
    }

    public int Index { get; }

    public Sprite? Flagman { get; private set; }

    public Sprite? Hovered { get; set; }

    public Message Messages { get; }

    [EngineRoutine(0x0043a8b0)]
    public int Money { get; set; }

    [EngineRoutine(0x0043a8c0)]
    public bool IsCleverEnemyAttack { get; set; }

    [EngineRoutine(0x00413140)]
    public void SetFlagman(Sprite? sprite)
    {
        sprite?.AddRef();

        if (Flagman is not null)
        {
            _map.Release(Flagman);
        }

        Flagman = sprite;
    }

    [EngineRoutine(0x00413360)]
    public void Reset()
    {
        if (Flagman is not null)
        {
            _map.Release(Flagman);
        }

        Flagman = null;

        if (Hovered is not null)
        {
            _map.Release(Hovered);
        }

        Hovered = null;
        Money = StartMoney;
    }

    [EngineRoutine(0x00413440)]
    public void Load(ref Resource reader)
    {
        _map.TryReadPointer(ref reader, out var flagman);
        SetFlagman(flagman);
    }

    [EngineRoutine(0x004134c0)]
    public ReadOnlyMemory<byte> GetMouseTipsString()
    {
        if (Hovered is null)
            return default;

        Span<byte> key = stackalloc byte[UnitKeyLength];
        ((int)Hovered.Vid.Id).TryFormat(key, out int written, provider: CultureInfo.InvariantCulture);

        return _map.Session.Strings?.GetString("Units"u8, key[..written]) ?? default;
    }

    [EngineRoutine(0x0043e9f0)]
    public void Forget(Sprite removed) => ForgetSprite(removed);

    [EngineRoutine(0x0043ea10)]
    public void Control(Input input)
    {
        foreach (var group in _map.Groups)
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
            var point = input.Cursor with { Y = input.Cursor.Y + _map.GetGroundZScr(input.Cursor) };
            Map.Action(player, ActionCode.Move, (int)point.X, (int)point.Y);
        }

        if ((flags.HasFlag(InputFlags.Left) || flags.HasFlag(InputFlags.Right) || flags.HasFlag(InputFlags.Down) || flags.HasFlag(InputFlags.Up)) && Flagman!.Command == Command.Move)
        {
            Flagman.SetCommandWithoutLink(Command.None);
        }

        if (flags.HasFlag(InputFlags.First))
        {
            var sprite = (Man)Flagman!;

            if (sprite.Vid.Class == SpriteClass.Man && sprite.Rounds < sprite.Vid.ChildCounts[(int)SpriteAnimation.Fight])
            {
                for (int slot = (int)sprite.Link!.Vid.Id - Sprite.WeaponIdBase - 1; !sprite.ChangeWeapon(slot); slot--)
                {
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
            if (input.VirtualKey == _session.Controls!.Next)
            {
                notches = 1;
            }
            else if (input.VirtualKey == _session.Controls!.Prev)
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
        var origin = _session.Controls!.IsMovementRelativeToCursor ? cursorDirection : default;
        bool up = flags.HasFlag(InputFlags.Up);
        bool down = flags.HasFlag(InputFlags.Down);
        bool left = flags.HasFlag(InputFlags.Left);
        bool right = flags.HasFlag(InputFlags.Right);

        if (up && right)
        {
            player.RotateTact(origin + HeadingUpRight.Value, _map.FrameTime);
        }
        else if (up && left)
        {
            player.RotateTact(origin + HeadingUpLeft.Value, _map.FrameTime);
        }
        else if (down && right)
        {
            player.RotateTact(origin + HeadingDownRight.Value, _map.FrameTime);
        }
        else if (down && left)
        {
            player.RotateTact(origin + HeadingDownLeft.Value, _map.FrameTime);
        }
        else if (left)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.West.Value), _map.FrameTime);
        }
        else if (right)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.East.Value), _map.FrameTime);
        }
        else if (down)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.South.Value), _map.FrameTime);
        }
        else if (up)
        {
            player.RotateTact(player.RotateForGlide(origin + Angle.North.Value), _map.FrameTime);
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
                if (!_isTurningLegs)
                {
                    _isTurningLegs = player.Direction.MeasureTurnTo(torso.Direction) > LegSlack;
                }
                else
                {
                    _isTurningLegs = player.RotateTact(torso.Direction, _map.FrameTime) != default;
                }
            }

            torso.RotateTact(cursorDirection, _map.FrameTime);
        }
        if (Hovered is not null && Hovered.Vid.SpriteType.HasFlag(OldSpriteType.Terrain))
        {
            _map.Release(Hovered);
            Hovered = null;
        }
    }

    [EngineRoutine(0x00413280)]
    private void ForgetSprite(Sprite removed)
    {
        _sprites.Delete(removed);

        if (Flagman == removed)
        {
            _map.Release(removed);
            Flagman = null;
        }

        if (Hovered == removed)
        {
            _map.Release(removed);
            Hovered = null;
        }
    }
}
