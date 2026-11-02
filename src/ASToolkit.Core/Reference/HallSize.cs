namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the length and width of a hall in layout units.
/// </summary>
/// <param name="Length">The length of the hall.</param>
/// <param name="Width">The width of the hall.</param>
public readonly record struct HallSize(float Length, float Width);
