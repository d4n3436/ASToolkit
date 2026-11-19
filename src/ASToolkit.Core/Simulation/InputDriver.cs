using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class InputDriver
{
    private const int FirstWeaponKey = '0';
    private const int RebirthKey = ' ';
    private readonly Map _map;
    private Vector2? _destination;

    public InputDriver(Map map)
    {
        _map = map;
    }

    public bool Press(in PlayerCommand command)
    {
        var input = _map.Input;
        var player = _map.Player;

        bool sent = false;

        if (command.Destination != _destination)
        {
            _destination = command.Destination;

            if (_destination is not null)
            {
                _map.Motion.SetMoveTarget(player, null, new Vector3(_destination.Value, 0));
                sent = true;
            }
            else if (player.Command == Command.Move)
            {
                SpriteKinematics.Stop(player);
            }
        }

        var flags = input.Flags & ~(InputFlags.Movement | InputFlags.First | InputFlags.Second);

        if (command.IsFiring)
        {
            flags |= InputFlags.First;
        }

        if (command.Weapon != _map.Weapons.Slot)
        {
            input.Key = FirstWeaponKey + command.Weapon;
        }

        if (_map.GetFlagman(0) is null)
        {
            input.Key = RebirthKey;
        }

        input.Flags = flags;
        input.Cursor = command.Cursor;

        return sent;
    }
}
