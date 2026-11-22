namespace ASToolkit.Analysis.Levels;

/// <summary>
/// Represents a floor cell of a lattice that spans [U, U+1) x [V, V+1).
/// </summary>
/// <param name="U">The coordinate along the '/' direction.</param>
/// <param name="V">The coordinate along the '\' direction.</param>
public readonly record struct Cell(int U, int V)
{
    /// <summary>
    /// Represents the steps to a cell's four neighbors, in the order of <see cref="EdgeKey.GetNeighbors"/>.
    /// </summary>
    public static readonly Cell[] Steps = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    /// <summary>
    /// Adds a step to a cell.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="step">The step.</param>
    /// <returns>The cell a step away.</returns>
    public static Cell operator +(Cell cell, Cell step) => new(cell.U + step.U, cell.V + step.V);

    /// <summary>
    /// Repeats a step a number of times.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="times">The number of times to repeat the step.</param>
    /// <returns>The repeated step.</returns>
    public static Cell operator *(Cell step, int times) => new(step.U * times, step.V * times);
}
