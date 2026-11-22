using System.Numerics;
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

    private const float WindPerSecond = 0.001f;
    private const float NoSquall = -1;
    private const float SquallGain = 0.001953125f;
    private const uint SquallRise = 0x800;
    private const uint SquallEnd = 0x1000;

    private float _squallBaseSpeed = NoSquall;
    private int _squallStart;

    /// <summary>
    /// Gets the wall time each effect started at, or zero for an effect that isn't running.
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
    /// Gets or sets the camera position a scroll effect starts from.
    /// </summary>
    public Vector2 ScrollFrom { get; set; }

    /// <summary>
    /// Gets or sets the gamma a gamma shift starts from.
    /// </summary>
    public Gamma GammaFrom { get; set; }

    /// <summary>
    /// Gets the snow, rain and ground snow.
    /// </summary>
    public Weather Weather { get; } = new();

    /// <summary>
    /// Gets or sets the screen that the frame is drawn into, or <see langword="null"/> to run without drawing.
    /// </summary>
    public Screen? Screen { get; set; }

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
    /// Runs one frame of the squall, a gust that ramps the wind up and back down over about four seconds.
    /// </summary>
    /// <param name="time">The game time in milliseconds.</param>
    /// <param name="isPaused"><see langword="true"/> while the level is paused.</param>
    [EngineRoutine(0x00431d50)]
    public void UpdateSquall(int time, bool isPaused)
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

    /// <summary>
    /// Sets the ambient gamma and, when it changes, rebuilds the palettes of every sprite file with it.
    /// </summary>
    /// <param name="gamma">The ambient gamma.</param>
    /// <param name="definitions">The definitions whose sprite files take the ambient gamma.</param>
    [EngineRoutine(0x00431c50)]
    public void SetGamma(Gamma gamma, SpriteCatalog definitions)
    {
        Gamma = gamma;
        if (definitions.Ambient != gamma)
        {
            definitions.SetAmbient(gamma);
        }
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
    /// Loads the environment, the gamma and the wind a map starts with.
    /// </summary>
    /// <param name="environment">The environment effects.</param>
    /// <param name="ambient">The ambient gamma.</param>
    /// <param name="windDirection">The direction of the wind.</param>
    /// <param name="windSpeed">The speed of the wind in pixels per millisecond.</param>
    /// <param name="definitions">The definitions whose sprite files take the ambient gamma.</param>
    [EngineRoutine(0x00433cd0)]
    public void Load(MapEnvironment environment, Gamma ambient, Angle windDirection, float windSpeed, SpriteCatalog definitions)
    {
        Environment = environment;
        SetGamma(ambient, definitions);
        WindDirection = windDirection;
        WindSpeed = windSpeed;
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
}
