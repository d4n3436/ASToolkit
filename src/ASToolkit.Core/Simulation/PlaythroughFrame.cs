namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the active units of a playthrough at one time.
/// </summary>
/// <param name="Time">The time since the start in milliseconds.</param>
/// <param name="Units">The units awake or in view at that time, the player first.</param>
public sealed record PlaythroughFrame(int Time, IReadOnlyList<UnitState> Units);
