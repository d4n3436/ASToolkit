using System.Drawing;
using System.Numerics;

namespace ASToolkit.Core.Engine;

internal readonly record struct ScrollCamera(SizeF Screen, SizeF Map, Vector2 Position)
{
    private const float HalfShare = 0.5f;
    private const float Pace = -0.004f;
    private const float LeadWidth = 640;
    private const float LeadHeight = 480;

    private Vector2 HalfScreen => new(Screen.Width * HalfShare, Screen.Height * HalfShare);

    public static ScrollCamera Start(SizeF screen, SizeF map, Vector2 start)
    {
        var camera = new ScrollCamera(screen, map, Vector2.Zero);

        return camera.MoveTo(camera.HalfScreen + start);
    }

    [EngineRoutine(0x0040bed0)]
    public ScrollCamera Update(Vector3 player, Vector2 cursor, int elapsed)
    {
        // Scroll as SCROLL_TO_FLAGMAN_WITH_DIRECTION, leading toward the mouse
        var mouse = cursor - Position;
        float leadX = Clamp(mouse.X, Screen.Width - LeadWidth, LeadWidth);
        float leadY = Clamp(mouse.Y, Screen.Height - LeadHeight, LeadHeight);
        float paceX = (HalfScreen.X - (player.X - Position.X + leadX) * HalfShare) * Pace;
        float paceY = (HalfScreen.Y - (player.Y - player.Z - Position.Y + leadY) * HalfShare) * Pace;
        int stepY = (int)(elapsed * paceY);
        float centerY = HalfScreen.Y + stepY + Position.Y;
        int stepX = (int)(elapsed * paceX);

        return MoveTo(new Vector2(HalfScreen.X + stepX + Position.X, centerY));
    }

    [EngineRoutine(0x0040bc20)]
    private ScrollCamera MoveTo(Vector2 center)
    {
        // Let the far edges win on maps smaller than the screen
        var corner = Vector2.Max(center - HalfScreen, Vector2.Zero);
        corner = Vector2.Min(corner, new Vector2(Map.Width - Screen.Width, Map.Height - Screen.Height));

        return this with { Position = corner };
    }

    private static float Clamp(float value, float low, float high) => low <= value ? Math.Min(value, high) : low;
}
