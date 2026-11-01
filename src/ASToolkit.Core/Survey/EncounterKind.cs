namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies how the monsters of an encounter come.
/// </summary>
public enum EncounterKind
{
    /// <summary>
    /// Monsters placed on the map, standing together.
    /// </summary>
    PlacedGroup,

    /// <summary>
    /// A boss.
    /// </summary>
    Boss,

    /// <summary>
    /// Spawners that share a trigger.
    /// </summary>
    Ambush,

    /// <summary>
    /// A spawner with no trigger that starts at once.
    /// </summary>
    Spawner,

    /// <summary>
    /// Endless generators, or a spawner that builds them.
    /// </summary>
    EndlessGenerators
}
