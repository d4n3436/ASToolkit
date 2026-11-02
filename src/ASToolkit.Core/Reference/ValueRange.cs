namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the minimum and maximum of a fitted value among the fitting levels.
/// </summary>
/// <param name="Min">The minimum of the range.</param>
/// <param name="Max">The maximum of the range.</param>
public readonly record struct ValueRange(double Min, double Max);
