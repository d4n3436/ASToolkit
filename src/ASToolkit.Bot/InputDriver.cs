using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Bot;

internal sealed class InputDriver
{
    private const int FirstWeaponKey = '0';
    private const int RebirthKey = ' ';
    private const int CursorPasses = 3;
    private const float ReclickReach = 16;
    private const float BorderMargin = 8;

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
            input.Key = RebirthKey;
            input.Flags = flags;
            input.Cursor = cursor;
            input.ScreenCursor = GetMousePoint(cursor);

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

        if (command.Weapon != player.WeaponSlot)
        {
            input.Key = FirstWeaponKey + command.Weapon;
        }

        if (_map.GetFlagman(0) is null)
        {
            input.Key = RebirthKey;
        }

        input.Flags = flags;
        input.Cursor = cursor;
        input.ScreenCursor = GetMousePoint(cursor);

        return sent;
    }

    private Vector2 GetMousePoint(Vector2 cursor)
    {
        var view = _map.Session.View;
        var point = cursor - _map.Camera;

        return new Vector2(Math.Clamp(point.X, view.Left + BorderMargin, view.Right - BorderMargin), Math.Clamp(point.Y, view.Top + BorderMargin, view.Bottom - BorderMargin));
    }

    // Returns the cursor position above the point by the ground height the control adds back, found by repeated estimates
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
