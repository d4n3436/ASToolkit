namespace ASToolkit.Engine;

/// <summary>
/// Specifies how a region sprite covers the map and draws its fog.
/// </summary>
[Flags]
public enum RegionFlags
{
    /// <summary>
    /// No flags are set.
    /// </summary>
    None = 0,

    /// <summary>
    /// The fog fades in and steps toward its far level a little each frame.
    /// </summary>
    AnimatedFog = 1 << 0,

    /// <summary>
    /// The fog adds its color instead of darkening the colors underneath.
    /// </summary>
    LightFog = 1 << 1,

    /// <summary>
    /// The region covers the whole map instead of its own area.
    /// </summary>
    CoversMap = 1 << 3
}
