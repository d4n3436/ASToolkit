using System.Numerics;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private const float BorderScrollReach = 5;
    private const float ScrollAccelerationX = 0.04f;
    private const float ScrollAccelerationY = 0.03f;
    private const float FlagmanPace = 0.001f;
    private const float DirectionPace = -0.004f;
    private const float HalfShare = 0.5f;

    private static readonly Vector2 LeadReach = new(640, 480);
    private ScrollType _scrollType = ScrollType.FromBorder;

    /// <summary>
    /// Gets the top-left corner of the screen on the map.
    /// </summary>
    public Vector2 Camera { get; private set; }

    /// <summary>
    /// Gets the center of the screen on the map.
    /// </summary>
    public Vector2 ViewCenter => Session.ScreenSize.ToVector2() * HalfShare + Camera;

    [EngineRoutine(0x0040bc20)]
    internal void SetPosition(Vector2 center, CameraMove move)
    {
        var corner = center - Session.ScreenSize.ToVector2() * HalfShare;

        if (!move.HasFlag(CameraMove.Unclamped))
        {
            var min = Vector2.Zero - Session.View.Location.ToVector2();
            var max = Movement.MapSize.ToVector2() - new Vector2(Session.View.Right, Session.View.Bottom);

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

        if (Camera == corner)
            return;

        if (move == CameraMove.Smooth)
        {
            ExecEffect((int)ScreenEffect.Scroll, (int)center.X, (int)center.Y, 0);

            return;
        }

        var delta = corner - Camera;
        Camera = corner;

        foreach (var menuSprite in _menuSprites)
        {
            menuSprite.ChangeCoor(new Vector3(menuSprite.Position.X + delta.X, menuSprite.Position.Y + delta.Y, menuSprite.Position.Z));
        }

        CursorSprite.ChangeCoor(new Vector3(CursorSprite.Position.AsVector2() + delta, CursorSprite.Position.Z));

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
    private void Scroll()
    {
        var screen = Session.ScreenSize.ToVector2();
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
            int directionX = !(cursor.X > BorderScrollReach) && fromBorder ? -1
                : !(screen.X - BorderScrollReach > cursor.X) && fromBorder ? 1
                : flags.HasFlag(InputFlags.Left) && withArrows ? -1
                : flags.HasFlag(InputFlags.Right) && withArrows ? 1 : 0;

            speed.X = Accelerate(speed.X, Definitions.ScrollSpeed.X, directionX, ScrollAccelerationX);
            int directionY = !(cursor.Y > BorderScrollReach) && fromBorder ? -1
                : !(screen.Y - BorderScrollReach > cursor.Y) && fromBorder ? 1
                : flags.HasFlag(InputFlags.Up) && withArrows ? -1
                : flags.HasFlag(InputFlags.Down) && withArrows ? 1 : 0;

            speed.Y = Accelerate(speed.Y, Definitions.ScrollSpeed.Y, directionY, ScrollAccelerationY);
        }

        var flagman = GetFlagman(ControlledPlayer);

        bool isStill = speed.X.IsExactlyZero() && speed.Y.IsExactlyZero();

        if (flagman is not null && _scrollType.HasFlag(ScrollType.ToFlagman) && isStill)
        {
            speed = (flagman.Position.ToDrawPosition() - Camera - screen * HalfShare) * FlagmanPace;
        }
        else if (flagman is not null && _scrollType.HasFlag(ScrollType.ToFlagmanWithDirection) && isStill)
        {
            var low = screen - LeadReach;
            var lead = new Vector2(GetLead(cursor.X, low.X, LeadReach.X), GetLead(cursor.Y, low.Y, LeadReach.Y));
            speed = (screen * HalfShare - (flagman.Position.ToDrawPosition() - Camera + lead) * HalfShare) * DirectionPace;
        }
        else if (flagman is not null && _scrollType.HasFlag(ScrollType.FlagmanInCenter) && isStill)
        {
            Session.ScrollSpeed = speed;
            SetPosition(flagman.Position.ToDrawPosition() - Camera + Camera, CameraMove.None);

            return;
        }

        Session.ScrollSpeed = speed;
        float elapsed = (uint)FrameTime;
        SetPosition(screen * HalfShare + Vector2.Truncate(elapsed * speed) + Camera, CameraMove.None);
    }
}
