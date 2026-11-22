using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Graphics
{
    internal const ushort ClearDepth = 0x3ff;
    private const int ShakeReach = 4;
    private const uint ClearColor = 0xff000000;

    /// <summary>
    /// Fills the screen with a color and the depth buffer with its cleared depth.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="color">The color, packed as 0xAARRGGBB.</param>
    [EngineRoutine(0x00402600)]
    public static void Clear(Screen screen, uint color)
    {
        if (screen.IsTrueColor)
        {
            screen.TrueColorPixels.Fill(color);
        }
        else
        {
            screen.HighColorPixels.Fill(screen.Format.Pack(color));
        }

        screen.Depth[..(screen.Size.Height * screen.DepthWidth)].Fill(ClearDepth);
    }

    [EngineRoutine(0x00430870)]
    internal void UpdateScreen(Map map, bool drawFrame)
    {
        var screen = map.Session.ScreenSize;
        var saved = map.Camera;
        Screen?.IsLinearFiltered = false;
        
        if (Environment.HasFlag(MapEnvironment.Earthquake) && !map.IsPaused)
        {
            int y = map.Random.Next() % (ShakeReach * 2 + 1);
            int x = map.Random.Next() % (ShakeReach * 2 + 1);
            map.SetPosition(new Vector2(screen.Width * Map.HalfShare + saved.X + ShakeReach - x, screen.Height * Map.HalfShare + saved.Y + ShakeReach - y), CameraMove.None);
        }

        if (drawFrame)
        {
            if (Screen is not null)
            {
                if (!map.HasHardwareGround || map.Camera.X < 0f || map.Movement.MapSize.Width < Screen.Clip.Right + map.Camera.X - Screen.Clip.Left || map.Camera.Y < 0f || map.Movement.MapSize.Height < Screen.Clip.Bottom + map.Camera.Y - Screen.Clip.Top)
                {
                    Clear(Screen, ClearColor);
                }
            }

            DrawLayers(map, ObjectLayer.Ground, ObjectLayer.Light);
            UpdateGroundSnow(map);
            UpdateSnow(map);
            DrawLayers(map, ObjectLayer.Alpha, ObjectLayer.Alpha);
            UpdateRain(map);
            DrawLayers(map, ObjectLayer.SoftwareTop, ObjectLayer.Cursor);

            if (Screen is not null && !map.CursorSprite.IsHardwareCursor && map.CursorSprite.Definition.Object.Properties.HasFlag(ObjectProperties.AlwaysTop))
            {
                map.DrawCursorChain(Screen, map.Camera, null, true);
            }
        }

        UpdateSquall(map.Time, map.IsPaused);

        if (Environment.HasFlag(MapEnvironment.Earthquake) && !map.IsPaused)
        {
            map.SetPosition(new Vector2(screen.Width * Map.HalfShare + saved.X, screen.Height * Map.HalfShare + saved.Y), CameraMove.None);
        }

        UpdateEffects(map, drawFrame);
    }

    private void DrawLayers(Map map, ObjectLayer first, ObjectLayer last)
    {
        if (Screen is null)
            return;

        for (var layer = first; layer <= last; layer++)
        {
            map.DrawLayer(Screen, map.Camera, layer);
        }
    }

    /// <summary>
    /// Copies an area of a depth texture into the depth buffer, clipped to the screen.
    /// </summary>
    /// <param name="screen">The screen whose depth buffer is written.</param>
    /// <param name="destination">The area of the depth buffer.</param>
    /// <param name="source">The area of the texture.</param>
    /// <param name="texels">The depth values of the texture.</param>
    /// <param name="width">The width of the texture, in texels.</param>
    [EngineRoutine(0x00402820)]
    public static void CopyDepth(Screen screen, Rectangle destination, Rectangle source, Span<ushort> texels, int width)
    {
        if (screen.Clip.Left > destination.Right || destination.Left >= screen.Clip.Right || screen.Clip.Top > destination.Bottom || destination.Top >= screen.Clip.Bottom)
            return;

        int left = destination.Left;
        int top = destination.Top;
        int right = destination.Right;
        int bottom = destination.Bottom;
        int sourceLeft = source.Left;
        int sourceTop = source.Top;
        if (destination.Left < screen.Clip.Left)
        {
            left = screen.Clip.Left;
            sourceLeft += left - destination.Left;
        }

        if (destination.Top < screen.Clip.Top)
        {
            top = screen.Clip.Top;
            sourceTop += top - destination.Top;
        }

        if (screen.Clip.Right < destination.Right)
        {
            right = screen.Clip.Right;
        }

        if (screen.Clip.Bottom < destination.Bottom)
        {
            bottom = screen.Clip.Bottom;
        }

        var texture = new Texture(texels, new Size(width, texels.Length / width), VidPixelFormat.R5G6B5).Lock(new Point(sourceLeft, sourceTop), out int texturePitch);
        int end = (bottom - 1) * screen.DepthWidth + right;
        int i = top * screen.DepthWidth + left;
        int sourceIndex = 0;
        int rowWidth = right - left;
        while (i < end)
        {
            for (int column = left; column < right; column++)
            {
                if ((uint)i >= (uint)screen.Depth.Length || (uint)sourceIndex >= (uint)texture.Length)
                    return;

                screen.Depth[i++] = texture[sourceIndex++];
            }

            i += screen.DepthWidth - rowWidth;
            sourceIndex += texturePitch / sizeof(ushort) - rowWidth;
        }
    }
}
