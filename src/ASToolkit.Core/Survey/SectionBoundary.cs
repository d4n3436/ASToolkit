namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents the crossing of the tour into a section of a level.
/// </summary>
/// <param name="Section">The index of the section the boundary leads into.</param>
/// <param name="FromPlace">The place of the earlier section.</param>
/// <param name="ToPlace">The place of the section the boundary leads into.</param>
/// <param name="Kind">The kind of the boundary.</param>
public readonly record struct SectionBoundary(int Section, int FromPlace, int ToPlace, BoundaryKind Kind);
