using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents a level reduced to floor cells, wall edges and dressing on its theme's lattice.
/// Edge kinds are 'W' for walls, 'D' for doors and 'F' for fake or built-in doors.
/// </summary>
public sealed class LevelModel
{
    /// <summary>
    /// Gets the level name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the theme.
    /// </summary>
    public required LevelTheme Theme { get; init; }

    /// <summary>
    /// Gets the lattice of the cells.
    /// </summary>
    public required Lattice Lattice { get; init; }

    /// <summary>
    /// Gets the floor cells.
    /// </summary>
    public required IReadOnlySet<Cell> Cells { get; init; }

    /// <summary>
    /// Gets the wall edges by kind.
    /// </summary>
    public required IReadOnlyDictionary<EdgeKey, char> Edges { get; init; }

    /// <summary>
    /// Gets the dressing objects.
    /// </summary>
    public required IReadOnlyList<DecorObject> Objects { get; init; }

    /// <summary>
    /// Gets every placed object of the map.
    /// </summary>
    public required IReadOnlyList<MapSprite> Sprites { get; init; }

    /// <summary>
    /// Returns the share of the floor's bounding box that is floor.
    /// </summary>
    public double GetBoxFill()
    {
        int width = Cells.Max(c => c.U) - Cells.Min(c => c.U) + 1;
        int height = Cells.Max(c => c.V) - Cells.Min(c => c.V) + 1;
        return Cells.Count / (double)(width * height);
    }
}
