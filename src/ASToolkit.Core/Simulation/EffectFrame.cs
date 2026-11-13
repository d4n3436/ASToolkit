using ASToolkit.Core.Engine;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the active visual effects at a simulation tick.
/// </summary>
/// <param name="Time">The time since the start in milliseconds.</param>
/// <param name="Effects">The effects visible at that time.</param>
public sealed record EffectFrame(int Time, EffectState[] Effects);
