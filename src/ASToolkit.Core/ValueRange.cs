using System.Numerics;

namespace ASToolkit.Core;

/// <summary>
/// Represents an inclusive range of values.
/// </summary>
/// <param name="Min">The minimum of the range.</param>
/// <param name="Max">The maximum of the range.</param>
public readonly record struct ValueRange<T>(T Min, T Max) where T : INumber<T>
{
    /// <summary>
    /// Returns whether the range holds a value.
    /// </summary>
    public bool Contains(T value) => value >= Min && value <= Max;

    /// <summary>
    /// Creates a range from its minimum and maximum.
    /// </summary>
    public static implicit operator ValueRange<T>((T Min, T Max) range) => new(range.Min, range.Max);
}
