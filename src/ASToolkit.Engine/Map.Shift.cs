using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{
    internal const float HalfShare = 0.5f;
    private const float BorderScrollReach = 5;
    private const float FlagmanPace = 0.001f;
    private const float DirectionPace = -0.004f;

    private static readonly Vector2 ScrollAcceleration = new(0.04f, 0.03f);

    private static readonly Vector2 LeadReach = new(640, 480);

    /// <summary>
    /// Gets the center of the screen on the map.
    /// </summary>
    public Vector2 ViewCenter => Session.Graphics.ScreenSize.ToVector2() * HalfShare + Shift;

    [EngineRoutine(0x0040bc20)]
    internal void SetShiftCoordinates(Vector2 center, ShiftMove move)
    {
        var corner = center - Session.Graphics.ScreenSize.ToVector2() * HalfShare;

        if (!move.HasFlag(ShiftMove.Unclamped))
        {
            var min = Vector2.Zero - Session.Graphics.Viewport.Location.ToVector2();
            var max = Size.ToVector2() - new Vector2(Session.Graphics.Viewport.Right, Session.Graphics.Viewport.Bottom);

            if (corner.X < min.X)
            {
                corner.X = min.X;
            }

            if (corner.Y < min.Y)
            {
                corner.Y = min.Y;
            }

            if (!(corner.X <= max.X))
            {
                corner.X = max.X;
            }

            if (!(corner.Y <= max.Y))
            {
                corner.Y = max.Y;
            }
        }

        if (Shift == corner)
            return;

        if (move == ShiftMove.Smooth)
        {
            Session.Graphics.Effect(this, (int)ScreenEffect.Scroll, (int)center.X, (int)center.Y, 0);

            return;
        }

        var delta = corner - Shift;
        Shift = corner;

        foreach (var menuSprite in Menu.Items)
        {
            menuSprite.ChangeCoor(new Vector3(menuSprite.Position.X + delta.X, menuSprite.Position.Y + delta.Y, menuSprite.Position.Z));
        }

        Mouse.ChangeCoor(new Vector3(Mouse.Position.AsVector2() + delta, Mouse.Position.Z));

        Input.Cursor += delta;
    }

    private static float GetLead(float cursor, float low, float high) => low > cursor ? low : cursor > high ? high : cursor;

    private static float Accelerate(float speed, float limit, int direction, float acceleration) => direction switch
    {
        < 0 => -limit < speed ? speed - acceleration : speed,
        > 0 => speed < limit ? speed + acceleration : speed,
        _ => 0
    };

    [EngineRoutine(0x0040bed0)]
    private protected void ControlShiftCoordinates()
    {
        var screen = Session.Graphics.ScreenSize.ToVector2();
        var cursor = Input.ScreenCursor;
        var flags = Input.Flags;
        var speed = Session.ScrollSpeed;

        if (!_scrollType.HasFlag(ScrollType.FromBorder) && !_scrollType.HasFlag(ScrollType.WithArrows))
        {
            speed = Vector2.Zero;
        }
        else
        {
            bool fromBorder = _scrollType.HasFlag(ScrollType.FromBorder);
            bool withArrows = _scrollType.HasFlag(ScrollType.WithArrows);
            var direction = new Point(
                !(cursor.X > BorderScrollReach) && fromBorder ? -1
                : !(screen.X - BorderScrollReach > cursor.X) && fromBorder ? 1
                : flags.HasFlag(InputFlags.Left) && withArrows ? -1
                : flags.HasFlag(InputFlags.Right) && withArrows ? 1 : 0,
                !(cursor.Y > BorderScrollReach) && fromBorder ? -1
                : !(screen.Y - BorderScrollReach > cursor.Y) && fromBorder ? 1
                : flags.HasFlag(InputFlags.Up) && withArrows ? -1
                : flags.HasFlag(InputFlags.Down) && withArrows ? 1 : 0);

            speed.X = Accelerate(speed.X, Constant.ScrollSpeed.X, direction.X, ScrollAcceleration.X);
            speed.Y = Accelerate(speed.Y, Constant.ScrollSpeed.Y, direction.Y, ScrollAcceleration.Y);
        }

        var flagman = Flagman(ControlledPlayer);

        bool isStill = speed.X.IsExactlyZero() && speed.Y.IsExactlyZero();

        if (flagman is not null && _scrollType.HasFlag(ScrollType.ToFlagman) && isStill)
        {
            speed = (flagman.Position.ToDrawPosition() - Shift - screen * HalfShare) * FlagmanPace;
        }
        else if (flagman is not null && _scrollType.HasFlag(ScrollType.ToFlagmanWithDirection) && isStill)
        {
            var low = screen - LeadReach;
            var lead = new Vector2(GetLead(cursor.X, low.X, LeadReach.X), GetLead(cursor.Y, low.Y, LeadReach.Y));
            speed = (screen * HalfShare - (flagman.Position.ToDrawPosition() - Shift + lead) * HalfShare) * DirectionPace;
        }
        else if (flagman is not null && _scrollType.HasFlag(ScrollType.FlagmanInCenter) && isStill)
        {
            Session.ScrollSpeed = speed;
            SetShiftCoordinates(flagman.Position.ToDrawPosition() - Shift + Shift, ShiftMove.None);

            return;
        }

        Session.ScrollSpeed = speed;
        float elapsed = (uint)FrameTime;
        SetShiftCoordinates(screen * HalfShare + Vector2.Truncate(elapsed * speed) + Shift, ShiftMove.None);
    }
}
