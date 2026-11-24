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

    private static readonly Vector2 MessageDefaultOrigin = new(398, 388);
    private static readonly Angle HeadingUpRight = new(0x20 + 8);
    private static readonly Angle HeadingUpLeft = new(0xE0 - 8);
    private static readonly Angle HeadingDownRight = new(0x60 - 8);
    private static readonly Angle HeadingDownLeft = new(0xA0 + 8);
    private readonly Map _map;
    private readonly ControlSettings _settings;
    private readonly List<Sprite> _sprites = [];
    private bool _isTurningLegs;

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

    [EngineRoutine(0x0043a8c0)]
    public bool IsCleverEnemyAttack { get; set; }

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
    public void Load(ref Resource reader)
    {
        _map.TryReadRelatedSprite(ref reader, out var flagman);
        SetFlagman(flagman);
    }

    [EngineRoutine(0x0043e9f0)]
    public void Forget(Sprite removed) => ForgetSprite(removed);

    [EngineRoutine(0x0043ea10)]
    public void Control(PlayerInput input)
    {
        ShareGroupAttacks();
        Messages.Tact();

        var player = Sprite;

        if (player is null)
            return;

        if (player.Definition.Class != SpriteClass.Man)
            return;

        var flags = input.Flags;
        var weapons = _map.Weapons;
        ApplyButtons(input, player, weapons);
        ScrollWeapons(input, weapons);
        player = Sprite!;
        var cursorDirection = Angle.FromOffset(new Vector2(input.Cursor.X - player.Position.X, player.Position.Z + input.Cursor.Y - player.Position.Y));
        Steer(player, flags, _settings.IsMovementRelativeToCursor ? cursorDirection : default);
        if (flags.HasFlag(InputFlags.Left) || flags.HasFlag(InputFlags.Right) || flags.HasFlag(InputFlags.Down) || flags.HasFlag(InputFlags.Up))
        {
            _map.Motion.StartMove(player);
        }

        TurnTorso(player, cursorDirection);
        if (Hovered is not null && Hovered.Definition.Object.SpriteType.HasFlag(SpriteType.Terrain))
        {
            _map.Release(Hovered);
            Hovered = null;
        }
    }

    [EngineRoutine(0x00413280)]
    private void ForgetSprite(Sprite removed)
    {
        _map.RemoveSprite(_sprites, removed);

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

    private void ShareGroupAttacks()
    {
        foreach (var group in _map.Groups)
        {
            var members = group.Members;
            Sprite? target = null;
            Sprite? lastTarget = null;
            var command = Command.None;
            bool hasIdle = false;

            foreach (var member in members)
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

            for (int j = 0; j < members.Count; j++)
            {
                if (members[j].Command == Command.None)
                {
                    SpriteKinematics.SetCommand(members[j], command, target);
                }
            }
        }
    }

    private void ApplyButtons(PlayerInput input, Sprite player, WeaponSystem weapons)
    {
        var flags = input.Flags;
        if (flags.HasFlag(InputFlags.Second))
        {
            var point = input.Cursor with { Y = input.Cursor.Y + _map.Movement.GetHeight(input.Cursor) };
            _map.Action(player, ActionCode.Move, (int)point.X, (int)point.Y);
        }

        if ((flags.HasFlag(InputFlags.Left) || flags.HasFlag(InputFlags.Right) || flags.HasFlag(InputFlags.Down) || flags.HasFlag(InputFlags.Up)) && Sprite!.Command == Command.Move)
        {
            SpriteKinematics.SetCommandWithoutLink(Sprite, Command.None);
        }

        if (flags.HasFlag(InputFlags.First))
        {
            var sprite = (Man)Sprite!;

            if (sprite.Definition.Class == SpriteClass.Man && sprite.Rounds < sprite.Definition.ChildCounts[(int)SpriteAnimation.Fight])
            {
                for (int slot = (int)sprite.Link!.Definition.Id - Sprite.WeaponIdBase - 1; !weapons.ChangeWeapon(sprite, slot); slot--)
                {
                }
            }

            _map.Action(Sprite!, ActionCode.CoorAttack, (int)input.Cursor.X, (int)input.Cursor.Y);
        }

        if (input.Key is >= FirstWeaponKey and <= LastWeaponKey && Sprite!.Definition.Class == SpriteClass.Man)
        {
            weapons.ChangeWeapon((Man)Sprite!, input.Key - FirstWeaponKey);
        }
    }

    private void ScrollWeapons(PlayerInput input, WeaponSystem weapons)
    {
        int weaponSlot = Sprite!.WeaponSlot;
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

        if (notches > 0)
        {
            do
            {
                while (weaponSlot < Sprite.WeaponSlots)
                {
                    weaponSlot++;

                    if (weapons.ChangeWeapon((Man)Sprite!, weaponSlot))
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

                if (weapons.ChangeWeapon((Man)Sprite!, weaponSlot))
                    break;
            }
        }
    }

    private void Steer(Sprite player, InputFlags flags, Angle origin)
    {
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
            player.RotateTact(_map.Collision.RotateForGlide(origin + Angle.West.Value, player), _map.FrameTime);
        }
        else if (right)
        {
            player.RotateTact(_map.Collision.RotateForGlide(origin + Angle.East.Value, player), _map.FrameTime);
        }
        else if (down)
        {
            player.RotateTact(_map.Collision.RotateForGlide(origin + Angle.South.Value, player), _map.FrameTime);
        }
        else if (up)
        {
            player.RotateTact(_map.Collision.RotateForGlide(origin + Angle.North.Value, player), _map.FrameTime);
        }
        else if (player.Command != Command.Move)
        {
            SpriteKinematics.Stop(player);
        }
    }

    private void TurnTorso(Sprite player, Angle cursorDirection)
    {
        var torso = player.Link;

        if (torso is not null && torso.Definition == player.Definition.LinkDefinition)
        {
            if (player.Speed == 0 && player.Definition.Id == KnownObject.PlayerStart)
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
    }

}
