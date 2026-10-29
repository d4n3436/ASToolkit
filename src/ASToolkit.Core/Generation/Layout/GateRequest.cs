namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the gate a layout must have.
/// </summary>
/// <param name="Kind">The kind of gate.</param>
/// <param name="Share">The share of the area behind the gate.</param>
/// <param name="SearchPixels">For a blackout, the walk from the trigger to the breaker; otherwise 0.</param>
/// <param name="WalkPixels">For locked doors, the walk from the start to the breaker; otherwise 0.</param>
/// <param name="IsWing">A value indicating whether the gate closes a branch off the route instead of the rest of the route.</param>
public sealed record GateRequest(GateKind Kind, float Share, float SearchPixels, bool IsWing = false, int WalkPixels = 0);
