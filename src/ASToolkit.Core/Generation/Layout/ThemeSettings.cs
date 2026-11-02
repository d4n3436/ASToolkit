using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the unit and opening sizes a theme builds with.
/// </summary>
public sealed record ThemeSettings(
    LevelTheme Theme,
    int UnitCells,
    (int Min, int Max) ArchWidth,
    int MinShared)
{
    /// <summary>
    /// Represents the lab theme, built of lab cells.
    /// </summary>
    public static readonly ThemeSettings Lab = new(LevelTheme.Lab, 1, (1, 2), 3);

    /// <summary>
    /// Represents the military theme, built of 2 by 2 military cells.
    /// </summary>
    public static readonly ThemeSettings Military = new(LevelTheme.Military, 2, (1, 2), 2);

    /// <summary>
    /// Gets the walking length of one unit in map pixels.
    /// </summary>
    public float UnitPixels => Theme == LevelTheme.Lab ? MathF.Sqrt(Lattice.LabStep.X * Lattice.LabStep.X + Lattice.LabStep.Y * Lattice.LabStep.Y)
        : 2 * MathF.Sqrt(Lattice.MilitaryStep.X * Lattice.MilitaryStep.X + Lattice.MilitaryStep.Y * Lattice.MilitaryStep.Y);

    /// <summary>
    /// Gets the floor area of one unit in pixels.
    /// </summary>
    public float UnitArea => Theme == LevelTheme.Lab ? 2 * 40 * 28 : 4 * 2 * 45 * 32;
}
