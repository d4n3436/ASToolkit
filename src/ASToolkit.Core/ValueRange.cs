using System.Numerics;

namespace ASToolkit.Core;

/// <summary>
/// Represents an inclusive range of values.
/// </summary>
/// <typeparam name="T">The numeric type of the values.</typeparam>
/// <param name="Min">The minimum of the range.</param>
/// <param name="Max">The maximum of the range.</param>
public readonly record struct ValueRange<T>(T Min, T Max) where T : INumber<T>
{
    /// <summary>
    /// Determines whether the range holds a value.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if the value is between <see cref="Min"/> and <see cref="Max"/> inclusive; otherwise, <see langword="false"/>.</returns>
    public bool Contains(T value) => value >= Min && value <= Max;

    /// <summary>
    /// Creates a range from a tuple of its minimum and maximum.
    /// </summary>
    /// <param name="range">The tuple of the minimum and the maximum.</param>
    /// <returns>The range.</returns>
    public static implicit operator ValueRange<T>((T Min, T Max) range) => new(range.Min, range.Max);
}
