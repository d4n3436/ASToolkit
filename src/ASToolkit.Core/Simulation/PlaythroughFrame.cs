using System.Numerics;
using ASToolkit.Core.Engine;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the active units of a playthrough at one time.
/// </summary>
/// <param name="Time">The time since the start in milliseconds.</param>
/// <param name="Units">The units awake or in view at that time, the player first.</param>
/// <param name="Effects">The effects still moving or animating at this time.</param>
/// <param name="Cursor">The ground point under the player's mouse cursor at this time.</param>
public sealed record PlaythroughFrame(int Time, IReadOnlyList<UnitState> Units, IReadOnlyList<EffectState> Effects, Vector2 Cursor);
