namespace ASToolkit.Core.Levels;

/// <summary>
/// Specifies what place a level is, as its mission text names it, such as an underground lab, a warehouse or a school.
/// </summary>
public enum Building
{
    /// <summary>The outdoor grounds of a first level, such as the base's grounds or a housing estate's streets.</summary>
    Grounds,

    /// <summary>An underground lab.</summary>
    UndergroundLab,

    /// <summary>A warehouse of depots and storage rooms.</summary>
    Warehouse,

    /// <summary>A prison.</summary>
    Prison,

    /// <summary>An office block.</summary>
    OfficeBlock,

    /// <summary>A conference building with central halls and open spaces.</summary>
    ConferenceBuilding,

    /// <summary>A school.</summary>
    School,

    /// <summary>A training complex with gyms.</summary>
    TrainingComplex
}
