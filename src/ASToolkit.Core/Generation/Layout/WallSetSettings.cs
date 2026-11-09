using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the unit and opening sizes a wall set builds with, such as the cells of one layout unit and the widths of arches.
/// </summary>
public sealed record WallSetSettings(
    WallSet WallSet,
    int UnitCells,
    ValueRange<int> ArchWidth,
    int MinShared)
{
    /// <summary>
    /// Represents the basement set, built of one cell per unit.
    /// </summary>
    public static readonly WallSetSettings Basement = new(WallSet.Basement, 1, (1, 2), 3);

    /// <summary>
    /// Represents the concrete set, built of 2 by 2 cells per unit.
    /// </summary>
    public static readonly WallSetSettings Concrete = new(WallSet.Concrete, 2, (1, 2), 2);
}
