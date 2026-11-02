namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the width and depth of a court a ring of rooms encloses, in layout units.
/// </summary>
/// <param name="Width">The width of the court.</param>
/// <param name="Depth">The depth of the court.</param>
public readonly record struct CourtSize(float Width, float Depth);
