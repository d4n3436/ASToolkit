using System.Drawing;
using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Bot;

internal sealed class InputDriver
{
    private const float BorderMargin = 8;
    private const float ReclickReach = 16;

    private static readonly Angle[] Headings = [Angle.North, new(0x20 + 8), Angle.East, new(0x60 - 8), Angle.South, new(0xA0 + 8), Angle.West, new(0xE0 - 8)];

    private static ReadOnlySpan<VirtualKey> VerticalKeys => [VirtualKey.Up, VirtualKey.Up, default, VirtualKey.Down, VirtualKey.Down, VirtualKey.Down, default, VirtualKey.Up];

    private static ReadOnlySpan<VirtualKey> HorizontalKeys => [default, VirtualKey.Right, VirtualKey.Right, VirtualKey.Right, default, VirtualKey.Left, VirtualKey.Left, VirtualKey.Left];

    private static ReadOnlySpan<VirtualKey> Directions => [VirtualKey.Up, VirtualKey.Down, VirtualKey.Left, VirtualKey.Right];

    private readonly Map _map;
    private readonly bool _isRelative;
    private Vector2? _destination;
    private Vector2? _goal;
    private Vector2 _start;
    private int? _heading;
    private bool _isLeftDown;

    public InputDriver(Map map, ControlSettings controls)
    {
        _map = map;
        _isRelative = controls.IsMovementRelativeToCursor;

        foreach (var key in Directions)
        {
            map.Input.PostMessage(WindowMessageId.KeyUp, (int)key, 0);
        }
    }

    public Vector2 Cursor { get; private set; }

    public Vector2? Arrival { get; private set; }

    public bool Press(in PlayerCommand command)
    {
        var input = _map.Input;
        var point = GetMousePoint(command.Cursor);
        var pixel = new Point((int)MathF.Round(point.X), (int)MathF.Round(point.Y));
        Cursor = new Vector2(pixel.X, pixel.Y) + _map.Shift;
        var player = _map.Flagman(0);
        bool hasStarted = false;
        Arrival = null;

        if (player is not null && _goal is not null && CanCoastTo(player, _goal.Value))
        {
            Arrival = _goal;
            _goal = null;
        }

        if (player is null)
        {
            _destination = null;
            _goal = null;
        }
        else if (command.Destination != _destination)
        {
            _destination = command.Destination;

            if (_destination is null)
            {
                _goal = null;
            }
            else if (_goal is null || Vector2.DistanceSquared(_goal.Value, _destination.Value) > ReclickReach * ReclickReach)
            {
                _goal = _destination;
                _start = player.Position.AsVector2();
                hasStarted = true;
            }
        }

        Hold(player is not null && _goal is not null ? ChooseHeading(player, _goal.Value) : null);

        if (player is not null && command.Weapon != player.WeaponSlot)
        {
            input.PostKey(VirtualKey.Number0 + command.Weapon, '0' + command.Weapon);
        }

        if (player is null)
        {
            input.PostKey(VirtualKey.Space, ' ');
        }

        var screen = pixel + new Size(_map.Session.Graphics.WindowPosition);
        input.PostMessage(WindowMessageId.HitTest, 0, (screen.Y & 0xFFFF) << 16 | (screen.X & 0xFFFF));
        bool isFiring = player is not null && command.IsFiring;

        if (isFiring != _isLeftDown)
        {
            input.PostMessage(isFiring ? WindowMessageId.LeftButtonDown : WindowMessageId.LeftButtonUp, 0, 0);
            _isLeftDown = isFiring;
        }

        return hasStarted;
    }

    private bool CanCoastTo(Sprite player, Vector2 goal)
    {
        float deceleration = player.Vid.Deceleration;
        float step = player.Speed * _map.FrameTime;
        float coast = step + (deceleration >= Vid.Unlimited ? 0 : player.Speed * player.Speed / (2 * deceleration));
        var stop = player.Position.AsVector2() + player.Direction.Vector * coast;
        var reach = player.Vid.Half + _map.EmptyVid.Half - new Vector2(step);

        return Math.Abs(stop.X - goal.X) < reach.X && Math.Abs(stop.Y - goal.Y) < reach.Y;
    }

    private static float Cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

    private int ChooseHeading(Sprite player, Vector2 goal)
    {
        var position = player.Position;
        var origin = _isRelative ? Angle.FromOffset(new Vector2(Cursor.X - position.X, position.Z + Cursor.Y - position.Y)) : default;
        var wanted = Angle.FromOffset(goal - position.AsVector2());
        var line = goal - _start;
        float length = line.Length();
        float side = length > 0 ? Cross(line, position.AsVector2() - _start) / length : 0;
        int heading = Math.Abs(side) > player.MaxSpeed * _map.FrameTime / 2 ? FindHeading(origin, wanted, line, side) : -1;

        return heading >= 0 ? heading : FindHeading(origin, wanted, line, null);
    }

    private static int FindHeading(Angle origin, Angle wanted, Vector2 line, float? side)
    {
        int best = -1;
        int bestTurn = 0;
        for (int heading = 0; heading < Headings.Length; heading++)
        {
            var direction = origin + Headings[heading].Value;
            int turn = direction.MeasureTurnTo(wanted);

            if (side is not null && (turn >= Angle.QuarterTurn || Cross(line, direction.Vector) * side.Value > 0))
                continue;

            if (best < 0 || turn < bestTurn)
            {
                best = heading;
                bestTurn = turn;
            }
        }

        return best;
    }

    private void Hold(int? heading)
    {
        if (heading == _heading)
            return;

        var input = _map.Input;
        var vertical = heading is null ? default : VerticalKeys[heading.Value];
        var horizontal = heading is null ? default : HorizontalKeys[heading.Value];
        var heldVertical = _heading is null ? default : VerticalKeys[_heading.Value];
        var heldHorizontal = _heading is null ? default : HorizontalKeys[_heading.Value];

        if (heldVertical != default && heldVertical != vertical)
        {
            input.PostMessage(WindowMessageId.KeyUp, (int)heldVertical, 0);
        }

        if (heldHorizontal != default && heldHorizontal != horizontal)
        {
            input.PostMessage(WindowMessageId.KeyUp, (int)heldHorizontal, 0);
        }

        if (vertical != default && vertical != heldVertical)
        {
            input.PostMessage(WindowMessageId.KeyDown, (int)vertical, 0);
        }

        if (horizontal != default && horizontal != heldHorizontal)
        {
            input.PostMessage(WindowMessageId.KeyDown, (int)horizontal, 0);
        }

        _heading = heading;
    }

    private Vector2 GetMousePoint(Vector2 cursor)
    {
        var view = _map.Session.Graphics.Viewport;
        var point = cursor - _map.Shift;

        return new Vector2(Math.Clamp(point.X, view.Left + BorderMargin, view.Right - BorderMargin), Math.Clamp(point.Y, view.Top + BorderMargin, view.Bottom - BorderMargin));
    }
}
