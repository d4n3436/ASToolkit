using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class InputDriver
{
    private const int FirstWeaponKey = '0';
    private const int RebirthKey = ' ';
    private const int CursorPasses = 3;
    private const float ReclickReach = 16;
    private readonly Map _map;
    private Vector2? _destination;
    private Vector2? _clicked;

    public InputDriver(Map map)
    {
        _map = map;
    }

    public bool Press(in PlayerCommand command)
    {
        var input = _map.Input;
        var flags = input.Flags & ~(InputFlags.Movement | InputFlags.First | InputFlags.Second);
        var cursor = command.Cursor;
        bool sent = false;

        if (!_map.HasPlayer)
        {
            if (_map.GetFlagman(0) is null)
            {
                input.Key = RebirthKey;
            }

            input.Flags = flags;
            input.Cursor = cursor;

            return sent;
        }

        var player = _map.Player;

        if (command.Destination != _destination)
        {
            _destination = command.Destination;

            if (_destination is not null && (player.Command != Command.Move || _clicked is null || Vector2.DistanceSquared(_clicked.Value, _destination.Value) > ReclickReach * ReclickReach))
            {
                _clicked = _destination;
                flags |= InputFlags.Second;
                cursor = GetMoveCursor(_destination.Value);
                sent = true;
            }
            else if (_destination is null && player.Command == Command.Move)
            {
                _clicked = null;
                flags |= InputFlags.Second;
                cursor = GetMoveCursor(player.Position.AsVector2());
            }
        }

        if (command.IsFiring && !flags.HasFlag(InputFlags.Second))
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
        input.Cursor = cursor;

        return sent;
    }

    // Returns the cursor above the point by the ground height the control adds back
    private Vector2 GetMoveCursor(Vector2 point)
    {
        var cursor = point;
        for (int pass = 0; pass < CursorPasses; pass++)
        {
            cursor = point with { Y = point.Y - _map.Movement.GetHeight(cursor) };
        }

        return cursor;
    }
}
