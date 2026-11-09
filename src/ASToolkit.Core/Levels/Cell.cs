namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents a floor cell of a lattice, spanning [U, U+1) x [V, V+1).
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
    /// Returns the cell a step away.
    /// </summary>
    public static Cell operator +(Cell cell, Cell step) => new(cell.U + step.U, cell.V + step.V);

    /// <summary>
    /// Returns the step repeated a number of times.
    /// </summary>
    public static Cell operator *(Cell step, int times) => new(step.U * times, step.V * times);
}
