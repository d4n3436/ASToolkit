using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
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

    private int _loadingFrame;
    private uint _loadingDrawnAt;

    private bool _isBlendEnabled;

    private QuadBlend _blendFactors = QuadBlend.None;

    /// <summary>
    /// Gets the line of text displayed on the loading screen.
    /// </summary>
    /// <remarks>The text is empty unless the game is running in debug mode.</remarks>
    public ReadOnlyMemory<byte> LoadingText { get; private set; }

    /// <summary>
    /// Gets the font that the device draws text with, or <see langword="null"/> if the font hasn't been created yet.
    /// </summary>
    public D3DFont? DeviceFont { get; private set; }

    internal bool IsDepthLocked { get; private set; }

    internal QuadBlend Blend => _isBlendEnabled ? _blendFactors : QuadBlend.None;

    private static ReadOnlySpan<byte> GraphicsLogTag => "GRAPH"u8;

    /// <summary>
    /// Fills the viewport of the screen with a color and clears the depth buffer.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="color">The color, packed as 0xAARRGGBB.</param>
    [EngineRoutine(0x00402600)]
    public static void ClearScreen(Screen screen, uint color)
    {
        var area = Rectangle.Intersect(screen.Viewport, new Rectangle(Point.Empty, screen.Size));
        ushort packed = screen.IsTrueColor ? default : screen.Format.Pack(color);
        for (int y = area.Top; y < area.Bottom; y++)
        {
            if (screen.IsTrueColor)
            {
                screen.TrueColorPixels.Slice(y * screen.Pitch + area.Left, area.Width).Fill(color);
            }
            else
            {
                screen.HighColorPixels.Slice(y * screen.Pitch + area.Left, area.Width).Fill(packed);
            }
        }

        screen.Depth[..(screen.Size.Height * screen.DepthWidth)].Fill(ClearDepth);
    }

    /// <summary>
    /// Copies an area of a depth texture into the depth buffer, clipped to the screen.
    /// </summary>
    /// <param name="screen">The screen to write to.</param>
    /// <param name="destination">The area of the depth buffer.</param>
    /// <param name="source">The area of the texture.</param>
    /// <param name="texture">The depth texture.</param>
    [EngineRoutine(0x00402820)]
    public static void CopyDepth(Screen screen, Rectangle destination, Rectangle source, Texture texture)
    {
        if (screen.Viewport.Left > destination.Right || destination.Left >= screen.Viewport.Right || screen.Viewport.Top > destination.Bottom || destination.Top >= screen.Viewport.Bottom)
            return;

        int left = destination.Left;
        int top = destination.Top;
        int right = destination.Right;
        int bottom = destination.Bottom;
        int sourceLeft = source.Left;
        int sourceTop = source.Top;
        if (destination.Left < screen.Viewport.Left)
        {
            left = screen.Viewport.Left;
            sourceLeft += left - destination.Left;
        }

        if (destination.Top < screen.Viewport.Top)
        {
            top = screen.Viewport.Top;
            sourceTop += top - destination.Top;
        }

        if (screen.Viewport.Right < destination.Right)
        {
            right = screen.Viewport.Right;
        }

        if (screen.Viewport.Bottom < destination.Bottom)
        {
            bottom = screen.Viewport.Bottom;
        }

        var texels = texture.Lock(null, out int texturePitch);
        int end = (bottom - 1) * screen.DepthWidth + right;
        int i = top * screen.DepthWidth + left;
        int sourceIndex = sourceTop * (texturePitch / sizeof(ushort)) + sourceLeft;
        int rowWidth = right - left;
        var depth = screen.Depth;
        while (i < end)
        {
            int copied = Math.Max(rowWidth, 0);
            texels.Slice(sourceIndex, copied).CopyTo(depth[i..]);
            i += copied + screen.DepthWidth - rowWidth;
            sourceIndex += copied + texturePitch / sizeof(ushort) - rowWidth;
        }
    }

    /// <summary>
    /// Creates the device font and replaces the previous one.
    /// </summary>
    /// <param name="name">The name of the typeface.</param>
    /// <param name="glyphSize">The size of a glyph, in points.</param>
    /// <param name="maxTextureSize">The maximum texture size supported by the device.</param>
    [EngineRoutine(0x00402bc0)]
    public void CreateDeviceFont(string name, Size glyphSize, int maxTextureSize)
    {
        DeviceFont = new D3DFont(name, glyphSize, D3DFontStyle.FixedPitch);
        DeviceFont.InitDeviceObjects(maxTextureSize);
        DeviceFont.CreateStateBlocks();
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

        var area = Rectangle.FromLTRB((int)MathF.Ceiling(box.Left), (int)MathF.Ceiling(box.Top), (int)MathF.Ceiling(box.Right), (int)MathF.Ceiling(box.Bottom));
        if ((color & OpaqueBlack) == OpaqueBlack)
        {
            SetRenderState(RenderState.AlphaBlendEnable, 0);
        }
        else
        {
            SetAlphaBlend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }

        ScreenPrimitives.FillRectangle(Screen, area, color, Blend.Source, Blend.Destination);
    }

    [EngineRoutine(0x00402090)]
    internal int PreTact()
    {
        if (Flags.HasFlag(GraphicsFlags.PreTact))
            return 0;

        int begun = Device?.BeginScene() ?? 0;
        if (begun != 0)
        {
            Error?.Error(GraphicsLogTag, LogError.Text, begun, "3dBeginScene for PreTact"u8);
            return 4;
        }

        Flags |= GraphicsFlags.PreTact;

        return 0;
    }

    [EngineRoutine(0x00402250)]
    internal void PostTact(bool present)
    {
        if (!Flags.HasFlag(GraphicsFlags.PreTact))
            return;

        IsDepthLocked = false;
        int ended = Device?.EndScene() ?? 0;
        if (ended != 0)
        {
            Error?.Error(GraphicsLogTag, LogError.Text, ended, "3dEndScene for PostTact"u8);
        }

        if (present && EffectStartTimes[(int)ScreenEffect.PixelAppear] == 0 && EffectStartTimes[(int)ScreenEffect.LineShift] == 0)
        {
            var source = Rectangle.FromLTRB((int)Viewport.Left, (int)Viewport.Top, (int)Viewport.Right, (int)Viewport.Bottom);
            var destination = source;
            if (!Flags.HasFlag(GraphicsFlags.FullScreen))
            {
                destination.Offset(WindowPosition.X - ClientPosition.X, WindowPosition.Y - ClientPosition.Y);
            }

            int presented = Device?.Present(source, destination) ?? 0;
            if (presented != 0)
            {
                Error?.Error(GraphicsLogTag, LogError.Invalid, presented, "Present"u8);
            }
        }

        Flags &= ~GraphicsFlags.PreTact;
    }

    [EngineRoutine(0x00402c40)]
    internal void DrawDeviceText(Vector2 position, uint color, ReadOnlySpan<byte> text)
    {
        IsDepthLocked = false;
        if (DeviceFont is null || Screen is null)
            return;

        DeviceFont.Draw(Screen, position, color, text);
    }

    [EngineRoutine(0x00430780)]
    internal void SetAlphaBlend(BlendFactor source, BlendFactor destination)
    {
        if (!Flags.HasFlag(GraphicsFlags.AlphaBlendEnabled))
        {
            _isBlendEnabled = true;
            Flags |= GraphicsFlags.AlphaBlendEnabled;
        }

        _blendFactors = new QuadBlend(source, destination);
    }

    [EngineRoutine(0x004307e0)]
    internal void SetRenderState(RenderState state, uint value)
    {
        if (state is RenderState.DepthFunction or RenderState.DepthWrite or RenderState.DepthEnable)
            return;

        if (state == RenderState.SpecularEnable)
        {
            if ((Flags.HasFlag(GraphicsFlags.SpecularEnabled) ? 1u : 0u) == value)
                return;

            Flags = value != 0 ? Flags | GraphicsFlags.SpecularEnabled : Flags & ~GraphicsFlags.SpecularEnabled;
        }
        else if (state == RenderState.AlphaBlendEnable)
        {
            if ((Flags.HasFlag(GraphicsFlags.AlphaBlendEnabled) ? 1u : 0u) == value)
                return;

            Flags = value != 0 ? Flags | GraphicsFlags.AlphaBlendEnabled : Flags & ~GraphicsFlags.AlphaBlendEnabled;
            _isBlendEnabled = value != 0;
        }
    }

    [EngineRoutine(0x00430870)]
    internal void Tact(Map map, bool drawFrame)
    {
        var screen = ScreenSize;
        var saved = map.Shift;

        if (Environment.HasFlag(MapEnvironment.Earthquake) && !map.IsPaused)
        {
            int y = map.Random.Next() % (ShakeReach * 2 + 1);
            int x = map.Random.Next() % (ShakeReach * 2 + 1);
            map.SetShiftCoordinates(new Vector2(screen.Width * Map.HalfShare + saved.X + ShakeReach - x, screen.Height * Map.HalfShare + saved.Y + ShakeReach - y), ShiftMove.None);
        }

        if (drawFrame)
        {
            if (Screen is not null)
            {
                if (map.FindVid(Map.HardwareGroundId) is null || map.Shift.X < 0f || map.Size.Width < Viewport.Right + map.Shift.X - Viewport.Left || map.Shift.Y < 0f || map.Size.Height < Viewport.Bottom + map.Shift.Y - Viewport.Top)
                {
                    ClearScreen(Screen, ClearColor);
                }
            }

            SetRenderState(RenderState.AlphaBlendEnable, 0);
            SetRenderState(RenderState.DepthFunction, 8);
            SetRenderState(RenderState.DepthWrite, 1);
            Screen?.IsLinearFiltered = false;
            IsDepthLocked = false;
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Ground);
            LockDepth();
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Built);
            map.DrawLayer(Screen, map.Shift, ObjectLayer.BuiltAlpha);
            map.DrawLayer(Screen, map.Shift, ObjectLayer.SoftwareGround);
            IsDepthLocked = false;
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Ground2);
            LockDepth();
            map.DrawLayer(Screen, map.Shift, ObjectLayer.SoftwareGrid);
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Software);
            map.DrawLayer(Screen, map.Shift, ObjectLayer.SoftwareAlpha);
            IsDepthLocked = false;
            SetRenderState(RenderState.DepthFunction, 7);
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Hardware);
            SetRenderState(RenderState.DepthWrite, 0);
            SetRenderState(RenderState.AlphaBlendEnable, 1);
            Screen?.IsLinearFiltered = false;
            map.DrawLayer(Screen, map.Shift, ObjectLayer.AlphaGround);
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Region);
            Screen?.IsLinearFiltered = true;
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Light);
            UpdateGroundSnow(map);
            DrawSnow(map);
            Screen?.IsLinearFiltered = false;
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Alpha);
            DrawRain(map);
            SetRenderState(RenderState.DepthWrite, 1);
            Screen?.IsLinearFiltered = false;
            LockDepth();
            map.DrawLayer(Screen, map.Shift, ObjectLayer.SoftwareTop);
            IsDepthLocked = false;
            map.DrawLayer(Screen, map.Shift, ObjectLayer.Cursor);

            if (Screen is not null && !map.Mouse.IsHardwareCursor && map.Mouse.Vid.Properties.HasFlag(ObjectProperties.AlwaysTop))
            {
                map.DrawCursorChain(Screen, map.Shift, null);
            }
        }

        DrawSquall(map.CurrentTime, map.IsPaused);

        if (Environment.HasFlag(MapEnvironment.Earthquake) && !map.IsPaused)
        {
            map.SetShiftCoordinates(new Vector2(screen.Width * Map.HalfShare + saved.X, screen.Height * Map.HalfShare + saved.Y), ShiftMove.None);
        }

        IsDepthLocked = false;
        if (Movie.IsOpen && Movie.Update())
        {
            Movie.Release();
        }

        SetRenderState(RenderState.SpecularEnable, 0);
        UpdateEffects(map, drawFrame);
        IsDepthLocked = false;
    }

    [EngineRoutine(0x00430d20)]
    internal void LockDepth()
    {
        if (!IsDepthLocked)
        {
            IsDepthLocked = true;
        }
    }

    [EngineRoutine(0x00430dd0)]
    internal void DrawLoadBar(Map map, Vid? vid)
    {
        uint realTime = map.Session.TimeGetTime();
        map.Session.RealCurrentTime = realTime;
        if (vid is null || (uint)vid.SpriteSpeed >= realTime - _loadingDrawnAt)
            return;

        PreTact();
        if (Screen is not null)
        {
            ClearScreen(Screen, ClearColor);
        }

        var screen = ScreenSize;
        DrawVid(map, vid, _loadingFrame, new Vector3(screen.Width * Map.HalfShare, screen.Height * Map.HalfShare + 1, LoadingDepth));
        UpdateEffects(map, true);
        DrawText(new Vector2(LoadingTextLeft, Viewport.Top + LoadingTextTop), LoadingText, TextColor);
        PostTact(true);
        _loadingDrawnAt = realTime;
        _loadingFrame++;
        if (_loadingFrame >= vid.FileFrameCount)
        {
            _loadingFrame = 0;
        }
    }

    [EngineRoutine(0x00430ed0)]
    internal void DrawText(Vector2 position, ReadOnlyMemory<byte> text, uint color) => DrawDeviceText(position, color, text.Span);

    [EngineRoutine(0x00430f10)]
    internal void SetLoadingText(ReadOnlySpan<byte> text)
    {
        LoadingText = text.ToArray();
        PreTact();
        FillBox(RectangleF.FromLTRB(LoadingTextLeft, Viewport.Top + LoadingTextTop, LoadingTextRight, Viewport.Top + LoadingTextBottom), OpaqueBlack);
        DrawText(new Vector2(LoadingTextLeft, Viewport.Top + LoadingTextTop), LoadingText, TextColor);
        PostTact(true);
    }

    [EngineRoutine(0x004333d0)]
    internal void DrawVid(Map map, Vid vid, int frame, Vector3 position)
    {
        if (vid == map.EmptyVid)
            return;

        if (frame < 0 || frame >= vid.FileFrameCount)
        {
            map.Session.Error.Error(GraphicsLogTag, LogError.Invalid, frame, "ncadr in DrawVid"u8);
            return;
        }

        SetRenderState(RenderState.AlphaBlendEnable, vid.Layer < ObjectLayer.AlphaGround ? 0u : 1u);
        var sprite = new Sprite(vid, new Vector3(position.AsVector2() + map.Shift, position.Z), default, null, map);
        sprite.Frame = frame;
        if (Screen is not null)
        {
            vid.Draw(Screen, map, sprite, map.Shift);
        }

        sprite.Destruct();
    }
}
