using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents a sprite created, collected or removed during play.
/// </summary>
/// <param name="Placement">The sprite the event concerns.</param>
/// <param name="Time">The simulation time in milliseconds.</param>
public readonly record struct SpriteEvent(MergedMapSprite Placement, int Time);
