namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents an area of a level, a group of rooms with one job that the player recognizes as one place.
/// </summary>
/// <param name="Kind">The kind of the area.</param>
/// <param name="Places">The places of the area, its hall or big room first.</param>
public sealed record LevelArea(AreaKind Kind, IReadOnlyList<int> Places);
