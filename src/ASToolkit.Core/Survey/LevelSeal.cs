namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a way back a trigger closes for good, with the two places on either side of it.
/// </summary>
/// <param name="Kind">The kind of the seal.</param>
/// <param name="Trigger">The place of the trigger.</param>
/// <param name="From">The place on the player's side.</param>
/// <param name="To">The place the seal shuts off.</param>
/// <param name="Phase">The index of the phase holding the trigger, or null when no phase holds it.</param>
public readonly record struct LevelSeal(SealKind Kind, int Trigger, int From, int To, int? Phase);
