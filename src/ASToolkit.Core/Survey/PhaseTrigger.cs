namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a trigger area that spawns monsters, with the place the player trips it in and the place the monsters land in.
/// </summary>
/// <param name="Trigger">The place of the trigger area.</param>
/// <param name="Place">The place the monsters land in.</param>
/// <param name="Source">Where the monsters come from.</param>
/// <param name="HitPoints">The hit points of the monsters.</param>
public readonly record struct PhaseTrigger(int Trigger, int Place, SpawnSource? Source, int HitPoints);
