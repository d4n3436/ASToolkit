namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Represents a shape drawn at a screen position, limited to a range of screen columns.
/// </summary>
internal readonly record struct RoofShape(ColumnProfile Profile, int Left, int Top, PixelKind Kind, int From = int.MinValue, int To = int.MaxValue);
