using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the graphics of the game, with the state that persists across levels.
/// </summary>
public sealed partial class Graphics
{
    /// <summary>
    /// Represents the screen width used when the registry doesn't hold one.
    /// </summary>
    public const int DefaultScreenWidth = 1024;

    /// <summary>
    /// Represents the screen height used when the registry doesn't hold one.
    /// </summary>
    public const int DefaultScreenHeight = 768;

    /// <summary>
    /// Represents the number of screen effects.
    /// </summary>
    public const int EffectCount = 16;

    private const float ViewDepthOffset = 128f;
    private const float ProjectionDepthScale = 0.001f;
    private const float ProjectionRange = 2f;
    private const float WindPerSecond = 0.001f;
    private const float NoSquall = -1;
    private const float SquallGain = 0.001953125f;
    private const uint SquallRise = 0x800;
    private const uint SquallEnd = 0x1000;
    private readonly GameSession _session;

    private float _squallBaseSpeed = NoSquall;
    private int _squallStart;

    internal Graphics(GameSession session)
    {
        _session = session;
        Movie = new Movie(session.Error);
    }

    /// <summary>
    /// Gets the size of the screen in pixels.
    /// </summary>
    public SizeF ScreenSize { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the game is running full screen.
    /// </summary>
    /// <remarks>When running in a window, the screen size is limited to the desktop size.</remarks>
    public bool IsFullScreen { get; internal set; }

    /// <summary>
    /// Gets the real time each effect started at, or zero for an effect that isn't running.
    /// </summary>
    public uint[] EffectStartTimes { get; } = new uint[EffectCount];

    /// <summary>
    /// Gets the length of each effect in milliseconds.
    /// </summary>
    public int[] EffectDurations { get; } = new int[EffectCount];

    /// <summary>
    /// Gets the first argument of each effect.
    /// </summary>
    public int[] EffectFirst { get; } = new int[EffectCount];

    /// <summary>
    /// Gets the second argument of each effect.
    /// </summary>
    public int[] EffectSecond { get; } = new int[EffectCount];

    /// <summary>
    /// Gets or sets the shift a scroll effect starts from.
    /// </summary>
    public Vector2 ScrollFrom { get; set; }

    /// <summary>
    /// Gets or sets the gamma a gamma shift starts from.
    /// </summary>
    public Gamma GammaFrom { get; set; }

    /// <summary>
    /// Gets the 16-bit colors of the brightness levels, in the layout of the device's textures.
    /// </summary>
    public ReadOnlySpan<ushort> ColorTable => Screen is not null ? EnsureBuffers().ColorTable : Buffers.IsCreated ? Buffers.ColorTable : DefaultColorTable;

    /// <summary>
    /// Gets the area of the back buffer the frame is drawn into.
    /// </summary>
    public RectangleF Viewport { get; private set; }

    /// <summary>
    /// Gets the matrix that maps the view onto the viewport.
    /// </summary>
    public Matrix4x4 Projection { get; private set; }

    /// <summary>
    /// Gets the gamma the frame is drawn with.
    /// </summary>
    public Gamma Gamma { get; private set; }

    /// <summary>
    /// Gets the direction of the wind.
    /// </summary>
    public Angle WindDirection { get; private set; }

    /// <summary>
    /// Gets the speed of the wind in pixels per millisecond.
    /// </summary>
    public float WindSpeed { get; private set; }

    /// <summary>
    /// Gets or sets the environment effects the map runs.
    /// </summary>
    public MapEnvironment Environment { get; set; }

    /// <summary>
    /// Gets or sets the screen that the frame is drawn into, or <see langword="null"/> to run without drawing.
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

    internal Movie Movie { get; }

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
            if (_squallBaseSpeed.IsExactly(NoSquall))
            {
                _squallBaseSpeed = WindSpeed;
            }

            uint elapsed = (uint)(time - _squallStart);

            if (elapsed > SquallRise)
            {
                if (elapsed > SquallEnd)
                {
                    Environment &= ~MapEnvironment.Squall;
                    WindSpeed = _squallBaseSpeed;
                    _squallBaseSpeed = NoSquall;

                    return;
                }

                elapsed = (uint)(_squallStart - time + SquallEnd);
            }

            WindSpeed = elapsed * _squallBaseSpeed * SquallGain + _squallBaseSpeed;

            return;
        }

        _squallStart = time;

        if (_squallBaseSpeed.IsExactly(NoSquall))
            return;

        WindSpeed = _squallBaseSpeed;
        _squallBaseSpeed = NoSquall;
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
    internal void PlayMovie(IGameFileSystem fileSystem, ReadOnlySpan<byte> path) => Movie.Open(fileSystem, path);

    private static Rectangle ToViewportRectangle(RectangleF viewport) => Rectangle.FromLTRB((int)viewport.Left, (int)viewport.Top, (int)viewport.Right, (int)viewport.Bottom);
}
