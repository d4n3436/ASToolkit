namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Represents the rows of one screen column of a shape, with the wall cap above the cap row.
/// </summary>
internal readonly record struct ColumnSpan(int Top, int Cap, int Bottom);
