namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies the form of a section of a level, from a few rooms to a hall with side rooms, a compact wing, a looped complex or a loose sprawl.
/// </summary>
public enum SectionArchetype
{
    /// <summary>
    /// One to three rooms with their closets.
    /// </summary>
    Room,

    /// <summary>
    /// A hall holding half the floor or more, with side rooms.
    /// </summary>
    Hall,

    /// <summary>
    /// Rooms off a spine with at most two loops.
    /// </summary>
    Wing,

    /// <summary>
    /// A tangle of rooms with three or more loops and deep pods.
    /// </summary>
    Complex,

    /// <summary>
    /// Large rooms or yards spread loosely, with no loops.
    /// </summary>
    Sprawl
}
