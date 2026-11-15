using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class InputDriver
{
    private const int FirstWeaponKey = '0';
    private readonly GameWorld _world;
    private Vector2? _destination;

    public InputDriver(GameWorld world)
    {
        _world = world;
    }

    public bool Press(in PlayerCommand command)
    {
        var input = _world.Input;
        var player = _world.Player;

        bool sent = false;

        if (command.Destination != _destination)
        {
            _destination = command.Destination;

            if (_destination is not null)
            {
                _world.Motion.SetMoveTarget(player, null, new Vector3(_destination.Value, 0));
                sent = true;
            }
            else if (player.Command == UnitCommand.Move)
            {
                UnitKinematics.StopMoving(player);
            }
        }

        var flags = input.Flags & ~(InputFlags.Movement | InputFlags.First | InputFlags.Second);

        if (command.IsFiring)
        {
            flags |= InputFlags.First;
        }

        if (command.Weapon != _world.Weapons.Slot)
        {
            input.Key = FirstWeaponKey + command.Weapon;
        }

        input.Flags = flags;
        input.Cursor = command.Cursor;

        return sent;
    }
}
