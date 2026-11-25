using System.Drawing;
using System.Numerics;
using ASToolkit.Engine;

namespace ASToolkit.Bot;

internal sealed class InputDriver
{
    private const int CursorPasses = 3;
    private const float ReclickReach = 16;
    private const float BorderMargin = 8;

    private readonly Map _map;
    private Vector2? _destination;
    private Vector2? _clicked;
    private bool _isLeftDown;
    private bool _isRightDown;

    public InputDriver(Map map)
    {
        _map = map;
    }

    public Vector2 Cursor { get; private set; }

    public bool Press(in PlayerCommand command)
    {
        var input = _map.Input;
        var cursor = command.Cursor;
        bool sent = false;
        bool hasClicked = false;
        bool hasFired = false;

        if (_map.Flagman(0) is not null)
        {
            var player = _map.Flagman(0)!;

            if (command.Destination != _destination)
            {
                _destination = command.Destination;

                if (_destination is not null && (player.Command != Command.Move || _clicked is null || Vector2.DistanceSquared(_clicked.Value, _destination.Value) > ReclickReach * ReclickReach))
                {
                    _clicked = _destination;
                    hasClicked = true;
                    cursor = GetMoveCursor(_destination.Value);
                    sent = true;
                }
                else if (_destination is null && player.Command == Command.Move)
                {
                    _clicked = null;
                    hasClicked = true;
                    cursor = GetMoveCursor(player.Position.AsVector2());
                }
            }

            hasFired = command.IsFiring && !hasClicked;

            if (command.Weapon != player.WeaponSlot)
            {
                input.PostKey(VirtualKey.Number0 + command.Weapon, '0' + command.Weapon);
            }
        }

        if (_map.Flagman(0) is null)
        {
            input.PostKey(VirtualKey.Space, ' ');
        }

        var point = GetMousePoint(cursor);
        var pixel = new Point((int)MathF.Round(point.X), (int)MathF.Round(point.Y));
        Cursor = new Vector2(pixel.X, pixel.Y) + _map.Shift;
        var screen = pixel + new Size(_map.Session.Graphics.WindowPosition);
        input.PostMessage(WindowMessageId.HitTest, 0, (screen.Y & 0xFFFF) << 16 | (screen.X & 0xFFFF));

        if (_isRightDown)
        {
            input.PostMessage(WindowMessageId.RightButtonUp, 0, 0);
            _isRightDown = false;
        }

        if (hasClicked)
        {
            input.PostMessage(WindowMessageId.RightButtonDown, 0, 0);
            _isRightDown = true;
        }

        if (hasFired != _isLeftDown)
        {
            input.PostMessage(hasFired ? WindowMessageId.LeftButtonDown : WindowMessageId.LeftButtonUp, 0, 0);
            _isLeftDown = hasFired;
        }

        return sent;
    }

    private Vector2 GetMousePoint(Vector2 cursor)
    {
        var view = _map.Session.Graphics.Viewport;
        var point = cursor - _map.Shift;

        return new Vector2(Math.Clamp(point.X, view.Left + BorderMargin, view.Right - BorderMargin), Math.Clamp(point.Y, view.Top + BorderMargin, view.Bottom - BorderMargin));
    }

    // Returns the cursor position that maps to the point once the control adds the ground height back, found by repeating the estimate
    private Vector2 GetMoveCursor(Vector2 point)
    {
        var cursor = point;
        for (int pass = 0; pass < CursorPasses; pass++)
        {
            cursor = point with { Y = point.Y - _map.GetGroundZ(cursor) };
        }

        return cursor;
    }
}
