namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a set of places the tour of a level leaves and never re-enters, with the untoured places off them.
/// </summary>
public sealed record LevelSection
{
    /// <summary>
    /// Gets the index of the section along the tour.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Gets the places of the section, toured ones in the order the tour first visits them and then the untoured ones it holds.
    /// </summary>
    public required IReadOnlyList<int> Places { get; init; }

    /// <summary>
    /// Gets the place the tour enters the section through.
    /// </summary>
    public required int Entrance { get; init; }

    /// <summary>
    /// Gets the place the tour leaves the section from.
    /// </summary>
    public required int Exit { get; init; }

    /// <summary>
    /// Gets the form of the section.
    /// </summary>
    public required SectionArchetype Archetype { get; init; }

    /// <summary>
    /// Gets the number of places of the section other than closets and nooks.
    /// </summary>
    public required int Rooms { get; init; }

    /// <summary>
    /// Gets the boundary the tour enters the section through, or null for the first section.
    /// </summary>
    public required SectionBoundary? Boundary { get; init; }

    /// <summary>
    /// Gets the floor area of the section in square pixels.
    /// </summary>
    public required int AreaPixels { get; init; }

    /// <summary>
    /// Gets the share of the sectioned floor in the section.
    /// </summary>
    public required float FloorShare { get; init; }

    /// <summary>
    /// Gets the share of the section's floor in halls and yards.
    /// </summary>
    public required float HallShare { get; init; }

    /// <summary>
    /// Gets the number of independent door cycles inside the section.
    /// </summary>
    public required int Loops { get; init; }

    /// <summary>
    /// Gets the most doors between a room of the section and the section's body from entrance to exit.
    /// </summary>
    public required int PodDepth { get; init; }

    /// <summary>
    /// Gets the doors between each room of the section and the section's body.
    /// </summary>
    public required IReadOnlyDictionary<int, int> Depths { get; init; }

    /// <summary>
    /// Gets the share of the section's bounding box its floor cells cover.
    /// </summary>
    public required float Compactness { get; init; }

    /// <summary>
    /// Gets the tour pixel the section starts at.
    /// </summary>
    public required int TourStart { get; init; }

    /// <summary>
    /// Gets the tour pixel the section ends at.
    /// </summary>
    public required int TourEnd { get; init; }

    /// <summary>
    /// Gets the number of biconnected blocks of three or more rooms the section shares with other sections.
    /// </summary>
    public required int SharedBlocks { get; init; }
}
