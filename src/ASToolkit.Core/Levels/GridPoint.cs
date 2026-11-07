namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents a point on a lattice or layout grid, in grid steps.
/// </summary>
/// <param name="U">The coordinate along the '/' direction.</param>
/// <param name="V">The coordinate along the '\' direction.</param>
public readonly record struct GridPoint(float U, float V);
