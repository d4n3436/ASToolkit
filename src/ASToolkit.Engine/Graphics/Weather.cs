using System.Numerics;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the snow, rain and ground snow that persist across levels.
/// </summary>
public sealed class Weather
{
    /// <summary>
    /// Represents the largest number of snowflakes.
    /// </summary>
    public const int MaxSnowflakes = 1000;

    /// <summary>
    /// Represents the number of vertices of a snowflake, three crossing lines.
    /// </summary>
    public const int SnowflakeVertexCount = 6;

    /// <summary>
    /// Represents the largest number of raindrops.
    /// </summary>
    public const int MaxRaindrops = 250;

    /// <summary>
    /// Represents the number of vertices of a raindrop, one line.
    /// </summary>
    public const int RaindropVertexCount = 2;
    private const float NoWind = 99999;

    private readonly WeatherVertex[] _snowflakes = new WeatherVertex[MaxSnowflakes * SnowflakeVertexCount];
    private readonly WeatherVertex[] _raindrops = new WeatherVertex[MaxRaindrops * RaindropVertexCount];

    /// <summary>
    /// Gets the number of snowflakes in the air.
    /// </summary>
    public int SnowflakeCount { get; internal set; }

    /// <summary>
    /// Gets the number of raindrops in the air.
    /// </summary>
    public int RaindropCount { get; internal set; }

    /// <summary>
    /// Gets the time the ground snow started to build up at, or the last time the map didn't have ground snow.
    /// </summary>
    public int GroundSnowStart { get; internal set; }

    /// <summary>
    /// Gets how far the ground snow has built up, a value that grows by one every 128 milliseconds until it passes 255.
    /// </summary>
    public int GroundSnowAge { get; internal set; }

    /// <summary>
    /// Gets the line list of the snowflakes in the air, as pairs of vertices.
    /// </summary>
    public ReadOnlySpan<WeatherVertex> SnowLines => _snowflakes.AsSpan(0, SnowflakeCount * 2);

    /// <summary>
    /// Gets the line list of the raindrops in the air, as pairs of vertices.
    /// </summary>
    public ReadOnlySpan<WeatherVertex> RainLines => _raindrops.AsSpan(0, RaindropCount * RaindropVertexCount);

    internal Span<WeatherVertex> Snowflakes => _snowflakes;

    internal Span<WeatherVertex> Raindrops => _raindrops;

    internal float SnowWind { get; set; }

    internal Vector2 SnowCamera { get; set; }

    internal byte SnowWindDirection { get; set; }

    internal float SnowWindSpeed { get; set; } = NoWind;

    internal float RainWind { get; set; }

    internal byte RainWindDirection { get; set; }

    internal float RainWindSpeed { get; set; } = NoWind;
}
