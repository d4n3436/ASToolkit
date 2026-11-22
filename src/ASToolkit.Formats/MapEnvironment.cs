namespace ASToolkit.Formats;

/// <summary>
/// Specifies the weather and screen effects of a map, as its graphics record stores them and level scripts set them.
/// </summary>
[Flags]
public enum MapEnvironment
{
    /// <summary>
    /// No effect.
    /// </summary>
    None = 0,

    /// <summary>
    /// Shadows.
    /// </summary>
    Shadow = 1 << 0,

    /// <summary>
    /// Long shadows.
    /// </summary>
    LongShadow = 1 << 1,

    /// <summary>
    /// An earthquake that shakes the camera.
    /// </summary>
    Earthquake = 1 << 2,

    /// <summary>
    /// Fog.
    /// </summary>
    Fog = 1 << 5,

    /// <summary>
    /// Snow lying on the ground.
    /// </summary>
    GroundSnow = 1 << 6,

    /// <summary>
    /// A passing gust of wind.
    /// </summary>
    Squall = 1 << 7,

    /// <summary>
    /// Inverted fog.
    /// </summary>
    FogInverted = 1 << 8,

    /// <summary>
    /// Fog that slowly appears and fades.
    /// </summary>
    FogAppear = 1 << 9,

    /// <summary>
    /// Rain slowly stopping.
    /// </summary>
    StopRain = 1 << 10,

    /// <summary>
    /// Rain slowly starting.
    /// </summary>
    BornRain = 1 << 11,

    /// <summary>
    /// Rain.
    /// </summary>
    Rain = StopRain | BornRain,

    /// <summary>
    /// Snow slowly stopping.
    /// </summary>
    StopSnow = 1 << 14,

    /// <summary>
    /// Snow slowly starting.
    /// </summary>
    BornSnow = 1 << 15,

    /// <summary>
    /// Snow.
    /// </summary>
    Snow = StopSnow | BornSnow,

    /// <summary>
    /// Clear the given effects instead of setting them.
    /// </summary>
    Stop = 1 << 31
}
