namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the state of the player at a point of a playthrough.
/// </summary>
/// <param name="Progress">The share of the tour walked.</param>
/// <param name="Time">The time since the start in milliseconds.</param>
/// <param name="Health">The health of the player.</param>
/// <param name="Armor">The hit points of the armor the player wears.</param>
/// <param name="AmmoBatches">The ammunition of the weapons besides the pistols, counted in pickup batches.</param>
/// <param name="Money">The money picked up.</param>
public readonly record struct PlaythroughSample(float Progress, int Time, int Health, int Armor, float AmmoBatches, int Money);
