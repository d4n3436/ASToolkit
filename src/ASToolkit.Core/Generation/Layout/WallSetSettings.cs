using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the unit and opening sizes that a wall set builds with.
/// </summary>
/// <param name="WallSet">The wall set.</param>
/// <param name="UnitCells">The number of cells in one layout unit.</param>
/// <param name="ArchWidth">The range of arch widths, in cells.</param>
/// <param name="MinShared">The minimum shared length, in cells.</param>
public sealed record WallSetSettings(
    WallSet WallSet,
    int UnitCells,
    ValueRange<int> ArchWidth,
    int MinShared)
{
    /// <summary>
    /// Represents the settings of the basement set, which is built of one cell per unit.
    /// </summary>
    public static readonly WallSetSettings Basement = new(WallSet.Basement, 1, (1, 2), 3);

    /// <summary>
    /// Represents the settings of the concrete set, which is built of 2 by 2 cells per unit.
    /// </summary>
    public static readonly WallSetSettings Concrete = new(WallSet.Concrete, 2, (1, 2), 2);
}
