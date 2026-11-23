using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    internal const ushort ClearDepth = 0x3ff;
    private const int ShakeReach = 4;
    private const uint ClearColor = 0xff000000;
    private const float LoadingDepth = 1;
    private const float LoadingTextLeft = 200;
    private const float LoadingTextRight = 800;
    private const float LoadingTextTop = 5;
    private const float LoadingTextBottom = 30;
    private const uint OpaqueBlack = 0xff000000;
    private const uint TextColor = 0xff00ff00;
    private const float HalfPixel = 0.5f;

    private int _loadingFrame;

    private static ReadOnlySpan<byte> GraphicsLogTag => "GRAPH"u8;

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

    /// <summary>
    /// Gets the line of text displayed on the loading screen.
    /// </summary>
    /// <remarks>The text is empty unless the game is running in debug mode.</remarks>
    public ReadOnlyMemory<byte> LoadingText { get; private set; }

    /// <summary>
    /// Gets the font that the device draws its own text with, or <see langword="null"/> if the font hasn't been created yet.
    /// </summary>
    public TextureFont? DeviceFont { get; private set; }

    /// <summary>
    /// Creates the device font, replacing the previous one.
    /// </summary>
    /// <param name="name">The name of the typeface.</param>
    /// <param name="width">The width of a glyph, in points.</param>
    /// <param name="height">The height of a glyph, in points.</param>
    /// <param name="maxTextureSize">The maximum texture size supported by the device.</param>
    [EngineRoutine(0x00402bc0)]
    public void CreateDeviceFont(string name, int width, int height, int maxTextureSize)
    {
        DeviceFont = new TextureFont(name, width, height, TextureFontStyle.FixedPitch);
        DeviceFont.Create(maxTextureSize);
        DeviceFont.CreateStateBlocks();
    }

    /// <summary>
    /// Immediately displays a line of text on the loading screen over a black box.
    /// </summary>
    /// <param name="text">The text.</param>
    [EngineRoutine(0x00430f10)]
    public void SetLoadingText(ReadOnlySpan<byte> text)
    {
        LoadingText = text.ToArray();
        FillBox(RectangleF.FromLTRB(LoadingTextLeft, Clip.Top + LoadingTextTop, LoadingTextRight, Clip.Top + LoadingTextBottom), OpaqueBlack);
        DrawText(new Vector2(LoadingTextLeft, Clip.Top + LoadingTextTop), LoadingText, TextColor);
    }

    [EngineRoutine(0x00430ed0)]
    private void DrawText(Vector2 position, ReadOnlyMemory<byte> text, uint color) => DrawDeviceText(position, color, text.Span);

    [EngineRoutine(0x00402c40)]
    private void DrawDeviceText(Vector2 position, uint color, ReadOnlySpan<byte> text)
    {
        if (DeviceFont is null || Screen is null)
            return;

        DeviceFont.Draw(Screen, position.X, position.Y, color, text, default);
    }

    /// <summary>
    /// Fills a box with a color without writing to the depth buffer.
    /// </summary>
    /// <param name="box">The box, in screen pixels.</param>
    /// <param name="color">The color, packed as 0xAARRGGBB.</param>
    /// <remarks>Colors with full alpha are drawn opaque. Other colors are alpha-blended.</remarks>
    [EngineRoutine(0x004342b0)]
    public void FillBox(RectangleF box, uint color)
    {
        if (Screen is null)
            return;

        var area = Rectangle.FromLTRB((int)MathF.Ceiling(box.Left - HalfPixel), (int)MathF.Ceiling(box.Top - HalfPixel), (int)MathF.Ceiling(box.Right - HalfPixel), (int)MathF.Ceiling(box.Bottom - HalfPixel));
        if ((color & OpaqueBlack) == OpaqueBlack)
        {
            ScreenPrimitives.FillRectangle(Screen, area, color, BlendFactor.One, BlendFactor.Zero);
        }
        else
        {
            ScreenPrimitives.FillRectangle(Screen, area, color, BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
    }

    [EngineRoutine(0x00430dd0)]
    internal void DrawLoadingScreen(Map map, SpriteDefinition? definition, bool isRedrawDue)
    {
        if (definition is null || !isRedrawDue)
            return;

        if (Screen is not null)
        {
            Clear(Screen, ClearColor);
        }

        var screen = map.Session.ScreenSize;
        DrawVid(map, definition, _loadingFrame, new Vector3(screen.Width * Map.HalfShare, screen.Height * Map.HalfShare + 1, LoadingDepth));
        UpdateEffects(map, true);
        DrawText(new Vector2(LoadingTextLeft, Clip.Top + LoadingTextTop), LoadingText, TextColor);
        _loadingFrame++;
        if (_loadingFrame >= definition.FileFrameCount)
        {
            _loadingFrame = 0;
        }
    }

    [EngineRoutine(0x004333d0)]
    internal void DrawVid(Map map, SpriteDefinition definition, int frame, Vector3 position)
    {
        if (definition == map.Definitions.Empty)
            return;

        if (frame < 0 || frame >= definition.FileFrameCount)
        {
            map.Session.Logger.Error(GraphicsLogTag, LogError.Invalid, frame, "ncadr in DrawVid"u8);
            return;
        }

        var sprite = new Sprite(-1, definition, new Vector3(position.AsVector2() + map.Camera, position.Z), map.Definitions.GetArmy(definition), 0, map: map);
        map.InitializeSprite(sprite, default);
        sprite.Frame = frame;
        if (Screen is not null)
        {
            definition.Draw(Screen, map, sprite, map.Camera);
        }

        map.DestructSprite(sprite);
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
        var depth = screen.Depth;
        while (i < end)
        {
            int count = i < 0 || sourceIndex < 0 ? 0 : Math.Min(rowWidth, Math.Min(depth.Length - i, texture.Length - sourceIndex));
            if (count > 0)
            {
                texture.Slice(sourceIndex, count).CopyTo(depth.Slice(i, count));
            }

            if (count < rowWidth)
                return;

            int copied = Math.Max(rowWidth, 0);
            i += copied + screen.DepthWidth - rowWidth;
            sourceIndex += copied + texturePitch / sizeof(ushort) - rowWidth;
        }
    }
}
