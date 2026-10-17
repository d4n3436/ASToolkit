namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents a floor cell of a lattice, spanning [U, U+1) x [V, V+1).
/// </summary>
/// <param name="U">The coordinate along the '/' direction.</param>
/// <param name="V">The coordinate along the '\' direction.</param>
public readonly record struct Cell(int U, int V);
