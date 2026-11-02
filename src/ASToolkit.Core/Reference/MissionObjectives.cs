namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the dynamite sites and generator rooms a mission graph lays out.
/// </summary>
/// <param name="Sites">The dynamite sites of the level.</param>
/// <param name="GeneratorRooms">The rooms holding the level's generators.</param>
public readonly record struct MissionObjectives(int Sites, int GeneratorRooms);
