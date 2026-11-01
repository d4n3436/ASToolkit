using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a level's floor cut into places.
/// </summary>
internal sealed class PlaceSegmentation
{
    /// <summary>Gets the floor of the level, blocked cells included, connected to the start through anything but walls and jammed doors.</summary>
    public required HashSet<Cell> Floor { get; init; }

    /// <summary>Gets the place index of every floor cell.</summary>
    public required Dictionary<Cell, int> PlaceOf { get; init; }

    /// <summary>Gets the cells of each place.</summary>
    public required List<List<Cell>> Places { get; init; }

    /// <summary>Gets the edges of gaps in wall lines, which part places without a wall piece.</summary>
    public required HashSet<EdgeKey> Openings { get; init; }

    /// <summary>Gets the edges of short holes between a door and the next wall piece on its line, which count as wall.</summary>
    public required HashSet<EdgeKey> Flanks { get; init; }
}
