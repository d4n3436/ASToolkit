namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies how the monsters of an encounter are laid out.
/// </summary>
public enum EncounterFormation
{
    /// <summary>
    /// One monster.
    /// </summary>
    Single,

    /// <summary>
    /// Monsters along a line.
    /// </summary>
    Line,

    /// <summary>
    /// Six or more monsters at one spacing.
    /// </summary>
    Grid,

    /// <summary>
    /// Monsters in a loose group.
    /// </summary>
    Cluster,

    /// <summary>
    /// Monsters that a spawner builds one after another.
    /// </summary>
    Stream,

    /// <summary>
    /// Endless generators that build monsters until blown up.
    /// </summary>
    Source
}
