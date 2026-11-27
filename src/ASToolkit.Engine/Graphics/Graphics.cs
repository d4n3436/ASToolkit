using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the graphics of the game, with the state that persists across levels.
/// </summary>
public sealed partial class Graphics
{
    /// <summary>
    /// Represents the number of screen effects.
    /// </summary>
    public const int EffectCount = 16;

    internal const float NoSquall = -1;
    private const float ViewDepthOffset = 128f;
    private const float ProjectionDepthScale = 0.001f;
    private const float ProjectionRange = 2f;
    private const float WindPerSecond = 0.001f;
    private const byte StartWindDirection = 0xdc;
    private const float StartWindSpeed = 20;
    private const float SquallGain = 0.001953125f;
    private const uint SquallRise = 0x800;
    private const uint SquallEnd = 0x1000;
    private readonly ushort[] _colorTable = new ushort[ColorTableLength];
    private readonly GameSession _session;
    private bool _areBuffersCreated;
    private IWindowDevice? _window;

    [EngineRoutine(0x004018f0)]
    internal Graphics(GameSession session, in GraphicsInit init)
    {
        _session = session;
        Device = session.GraphicsDevice;
        Movie = new Movie(session);
        if (init.Flags.HasFlag(GraphicsInitFlags.AppMenu))
        {
            Flags |= GraphicsFlags.WindowViewport;
        }

        if (init.Flags.HasFlag(GraphicsInitFlags.VSync))
        {
            Flags |= GraphicsFlags.VSync;
        }

        Effect((int)ScreenEffect.None, 0, 0, 0);
        _driver.FillParameters(session.DisplayModes.Span, init, Error);
        ScreenSize = init.ScreenSize;
        if (init.BitsPerPixel == 32)
        {
            Flags |= GraphicsFlags.Color32;
        }

        if (init.IsFullScreen)
        {
            Flags |= GraphicsFlags.FullScreen;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the game is running in full screen.
    /// </summary>
    /// <remarks>When running in a window, the screen size is limited to the desktop size.</remarks>
    public bool IsFullScreen => Flags.HasFlag(GraphicsFlags.FullScreen);

    /// <summary>
    /// Gets a value indicating whether the screen holds 32-bit pixels.
    /// </summary>
    public bool IsColor32 => Flags.HasFlag(GraphicsFlags.Color32);

    /// <summary>
    /// Gets a value indicating whether presents wait for the display's vertical sync.
    /// </summary>
    public bool IsVSync => Flags.HasFlag(GraphicsFlags.VSync);

    /// <summary>
    /// Gets the 16-bit colors of the brightness levels, in the layout of the device's textures.
    /// </summary>
    public ReadOnlySpan<ushort> ColorTable
    {
        get
        {
            if (Screen is not null)
            {
                EnsureBuffers();
            }

            return _areBuffersCreated ? _colorTable : DefaultColorTable;
        }
    }

    /// <summary>
    /// Gets the size of the screen in pixels.
    /// </summary>
    public SizeF ScreenSize { get; internal set; }

    /// <summary>
    /// Gets the area of the back buffer the frame is drawn into.
    /// </summary>
    public RectangleF Viewport { get; private set; }

    /// <summary>
    /// Gets or sets the shift a scroll effect starts from.
    /// </summary>
    public Vector2 ScrollFrom { get; set; }

    /// <summary>
    /// Gets the first argument of each effect.
    /// </summary>
    public int[] EffectFirst { get; } = new int[EffectCount];

    /// <summary>
    /// Gets the second argument of each effect.
    /// </summary>
    public int[] EffectSecond { get; } = new int[EffectCount];

    /// <summary>
    /// Gets the real time each effect started at, or zero for an effect that isn't running.
    /// </summary>
    public uint[] EffectStartTimes { get; } = new uint[EffectCount];

    /// <summary>
    /// Gets the length of each effect in milliseconds.
    /// </summary>
    public int[] EffectDurations { get; } = new int[EffectCount];

    /// <summary>
    /// Gets or sets the gamma a gamma shift starts from.
    /// </summary>
    public Gamma GammaFrom { get; set; }

    /// <summary>
    /// Gets the gamma the frame is drawn with.
    /// </summary>
    public Gamma Gamma { get; private set; }

    /// <summary>
    /// Gets or sets the environment effects the map runs.
    /// </summary>
    public MapEnvironment Environment { get; set; }

    /// <summary>
    /// Gets the direction of the wind.
    /// </summary>
    public Angle WindDirection { get; private set; } = new(StartWindDirection);

    /// <summary>
    /// Gets the speed of the wind in pixels per millisecond.
    /// </summary>
    public float WindSpeed { get; private set; } = StartWindSpeed;

    /// <summary>
    /// Gets or sets the device that frames are drawn on.
    /// </summary>
    public IGraphicsDevice? Device { get; set; }

    /// <summary>
    /// Gets the copy of the screen that an alpha appearance fades from.
    /// </summary>
    public Screen? Capture { get; private set; }

    /// <summary>
    /// Gets the font that the device draws text with.
    /// </summary>
    public D3DFont? DeviceFont { get; private set; }

    /// <summary>
    /// Gets the line of text displayed on the loading screen.
    /// </summary>
    /// <remarks>The text is empty unless the game is running in debug mode.</remarks>
    public ReadOnlyMemory<byte> LoadingText { get; private set; }

    /// <summary>
    /// Gets the matrix that maps the view onto the viewport.
    /// </summary>
    public Matrix4x4 Projection { get; private set; }

    /// <summary>
    /// Gets or sets the screen that the frame is drawn into.
    /// </summary>
    public Screen? Screen
    {
        get;
        set
        {
            field = value;
            value?.Viewport = ToViewportRectangle(Viewport);
        }
    }

    internal MyError Error => _session.Error;

    internal GraphicsFlags Flags { get; set; }

    internal bool IsDepthLocked { get; private set; }

    internal int DepthWidth { get; private set; }

    internal Movie Movie { get; }

    internal Texture? LightBuffer { get; private set; }

    internal Texture? HighBuffer { get; private set; }

    internal Texture? AlphaBuffer { get; private set; }

    /// <summary>
    /// Creates the view transform that moves the shift to the top left of the viewport and raises points by their height.
    /// </summary>
    /// <param name="shift">The top left corner of the view, in map pixels.</param>
    /// <param name="viewport">The viewport of the screen, in pixels.</param>
    /// <returns>The view transform.</returns>
    public static Matrix4x4 CreateView(Vector2 shift, Rectangle viewport) => new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, -1f, 1f, 0f,
        -shift.X - (viewport.Right + (float)viewport.Left) * 0.5f, -shift.Y - (viewport.Bottom + (float)viewport.Top) * 0.5f, ViewDepthOffset, 1f);

    /// <summary>
    /// Sets the area of the back buffer the frame is drawn into.
    /// </summary>
    /// <param name="viewport">The viewport.</param>
    /// <remarks>The screen's viewport and <see cref="Projection"/> are updated to match.</remarks>
    [EngineRoutine(0x00402400)]
    public void SetViewport(RectangleF viewport)
    {
        Viewport = viewport;
        Screen?.Viewport = ToViewportRectangle(viewport);
        int width = (int)(viewport.Right - viewport.Left);
        int height = (int)(viewport.Bottom - viewport.Top);
        Projection = new Matrix4x4(
            ProjectionRange / width, 0f, 0f, 0f,
            0f, -ProjectionRange / height, 0f, 0f,
            0f, 0f, ProjectionDepthScale, 0f,
            0f, 0f, 0f, 1f);
    }

    /// <summary>
    /// Sets the wind.
    /// </summary>
    /// <param name="speed">The speed in pixels per second.</param>
    /// <param name="direction">The direction.</param>
    [EngineRoutine(0x0040c930)]
    public void SetWind(int speed, Angle direction)
    {
        WindSpeed = speed * WindPerSecond;
        WindDirection = direction;
    }

    /// <summary>
    /// Sets the ambient gamma and when it changes, applies it to the palettes of every sprite file.
    /// </summary>
    /// <param name="gamma">The ambient gamma.</param>
    [EngineRoutine(0x00431c50)]
    public void SetGamma(Gamma gamma)
    {
        if (Gamma == gamma)
            return;

        Gamma = gamma;
        foreach (var vid in _session.Map!.Vids)
        {
            vid?.SetGamma(gamma, Vid.AllArmies);
        }
    }

    /// <summary>
    /// Runs one frame of the squall, a gust that ramps the wind up and back down over about four seconds.
    /// </summary>
    /// <param name="time">The game time in milliseconds.</param>
    /// <param name="isPaused"><see langword="true"/> while the level is paused.</param>
    [EngineRoutine(0x00431d50)]
    public void DrawSquall(int time, bool isPaused)
    {
        if (isPaused)
            return;

        if (Environment.HasFlag(MapEnvironment.Squall))
        {
            if (_session.SquallBaseSpeed.IsExactly(NoSquall))
            {
                _session.SquallBaseSpeed = WindSpeed;
            }

            uint elapsed = (uint)(time - _session.SquallStart);

            if (elapsed > SquallRise)
            {
                if (elapsed > SquallEnd)
                {
                    Environment &= ~MapEnvironment.Squall;
                    WindSpeed = _session.SquallBaseSpeed;
                    _session.SquallBaseSpeed = NoSquall;

                    return;
                }

                elapsed = (uint)(_session.SquallStart - time + SquallEnd);
            }

            WindSpeed = elapsed * _session.SquallBaseSpeed * SquallGain + _session.SquallBaseSpeed;

            return;
        }

        _session.SquallStart = time;

        if (_session.SquallBaseSpeed.IsExactly(NoSquall))
            return;

        WindSpeed = _session.SquallBaseSpeed;
        _session.SquallBaseSpeed = NoSquall;
    }

    [EngineRoutine(0x00431cf0)]
    internal void SetEnvironment(MapEnvironment environment)
    {
        if (environment.HasFlag(MapEnvironment.Stop))
        {
            Environment &= ~environment;
            return;
        }

        if (environment is MapEnvironment.Shadow or MapEnvironment.LongShadow)
        {
            Environment &= ~(MapEnvironment.Shadow | MapEnvironment.LongShadow);
        }

        if (environment.HasFlag(MapEnvironment.StopRain) || environment.HasFlag(MapEnvironment.BornRain))
        {
            Environment &= ~MapEnvironment.Rain;
        }

        if (environment.HasFlag(MapEnvironment.StopSnow) || environment.HasFlag(MapEnvironment.BornSnow))
        {
            Environment &= ~MapEnvironment.Snow;
        }

        Environment |= environment;
    }

    [EngineRoutine(0x00433bb0)]
    internal void OldLoadParameters(ref Resource reader)
    {
        var environment = Environment;
        reader.ReadInto(ref environment);
        Environment = environment;
        uint gamma = 0;
        reader.ReadInto(ref gamma);
        SetGamma(Gamma.FromScript(gamma));
        byte direction = 0;
        reader.ReadInto(ref direction);
        short speed = 0;
        reader.ReadInto(ref speed);
        SetWind(speed, new Angle(direction));
    }

    [EngineRoutine(0x00433cd0)]
    internal void Load(ref Resource reader)
    {
        var environment = Environment;
        reader.ReadInto(ref environment);
        Environment = environment;
        var gamma = default(Gamma);
        reader.ReadInto(ref gamma);
        SetGamma(gamma);
        int direction = 0;
        reader.ReadInto(ref direction);
        WindDirection = new Angle((byte)direction);
        float speed = WindSpeed;
        reader.ReadInto(ref speed);
        WindSpeed = speed;
    }

    [EngineRoutine(0x004343e0)]
    internal void PlayMovie(ReadOnlySpan<byte> path) => Movie.Open(path);

    private static Rectangle ToViewportRectangle(RectangleF viewport) => Rectangle.FromLTRB((int)viewport.Left, (int)viewport.Top, (int)viewport.Right, (int)viewport.Bottom);
}
