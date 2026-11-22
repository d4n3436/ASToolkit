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

    /// <summary>
    /// Represents the number of entries of the ground snow brightness ramp.
    /// </summary>
    public const int GroundSnowRampLength = 256;

    private const float NoWind = 99999;

    private readonly WeatherVertex[] _snowflakes = new WeatherVertex[MaxSnowflakes * SnowflakeVertexCount];
    private readonly WeatherVertex[] _raindrops = new WeatherVertex[MaxRaindrops * RaindropVertexCount];
    private readonly ushort[] _groundSnowRamp = CreateGroundSnowRamp();

    /// <summary>
    /// Gets the number of snowflakes in the air.
    /// </summary>
    public int SnowflakeCount { get; internal set; }

    /// <summary>
    /// Gets the number of raindrops in the air.
    /// </summary>
    public int RaindropCount { get; internal set; }

    /// <summary>
    /// Gets the time the ground snow started to build up at, or the last time the map had none.
    /// </summary>
    public int GroundSnowStart { get; internal set; }

    /// <summary>
    /// Gets how far the ground snow has built up, which grows by one every 128 milliseconds until it passes 255.
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

    /// <summary>
    /// Gets the 16-bit colors of the ground snow brightness levels.
    /// </summary>
    public ReadOnlySpan<ushort> GroundSnowRamp => _groundSnowRamp;

    internal Span<WeatherVertex> Snowflakes => _snowflakes;

    internal Span<WeatherVertex> Raindrops => _raindrops;

    internal float SnowWind { get; set; }

    internal Vector2 SnowCamera { get; set; }

    internal byte SnowWindDirection { get; set; }

    internal float SnowWindSpeed { get; set; } = NoWind;

    internal float RainWind { get; set; }

    internal byte RainWindDirection { get; set; }

    internal float RainWindSpeed { get; set; } = NoWind;

    private static int Clamp(int value) => Math.Clamp(value, 0, byte.MaxValue);

    private static ushort Pack(int red, int green, int blue) =>
        HighColorFormat.Rgb565.Pack((uint)(Clamp(red) << 16 | Clamp(green) << 8 | Clamp(blue)));

    private static ushort[] CreateGroundSnowRamp()
    {
        ushort[] ramp = new ushort[GroundSnowRampLength];
        int i = 0;
        for (int top = 8; top <= byte.MaxValue + 1; top += 8)
        {
            ramp[i++] = Pack(top - 8, top - 8, top - 8);
            ramp[i++] = Pack(top - 7, top - 7, top);
            ramp[i++] = Pack(top, top - 6, top - 6);
            ramp[i++] = Pack(top, top - 5, top);
            ramp[i++] = Pack(top - 4, top, top - 4);
            ramp[i++] = Pack(top - 3, top, top);
            ramp[i++] = Pack(top, top, top - 2);
            ramp[i++] = Pack(top, top, top);
        }

        return ramp;
    }
}
