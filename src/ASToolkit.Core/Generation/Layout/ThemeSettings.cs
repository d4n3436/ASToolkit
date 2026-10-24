using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the size ranges a theme builds with, in units.
/// </summary>
public sealed record ThemeSettings(
    LevelTheme Theme,
    int UnitCells,
    (int Min, int Max) HallWidth,
    (int Min, int Max) HallLength,
    (int Min, int Max) RoomSize,
    (int Min, int Max) ArchWidth,
    int MinShared)
{
    /// <summary>
    /// Represents the lab theme, built of lab cells.
    /// </summary>
    public static readonly ThemeSettings Lab = new(LevelTheme.Lab, 1, (4, 7), (7, 15), (5, 12), (2, 4), 3);

    /// <summary>
    /// Represents the military theme, built of 2 by 2 military cells.
    /// </summary>
    public static readonly ThemeSettings Military = new(LevelTheme.Military, 2, (3, 6), (4, 9), (4, 10), (2, 3), 3);

    /// <summary>
    /// Gets the walking length of one unit in map pixels.
    /// </summary>
    public float UnitPixels => Theme == LevelTheme.Lab ? MathF.Sqrt(40 * 40 + 28 * 28) : 2 * MathF.Sqrt(22.5f * 22.5f + 16 * 16);
}
