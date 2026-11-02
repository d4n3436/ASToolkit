namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a breaker, generator or blackout trigger that changes a level's state.
/// </summary>
/// <param name="Kind">The kind of the key.</param>
/// <param name="Place">The place of the key.</param>
public readonly record struct PhaseKey(LandmarkKind Kind, int Place);
