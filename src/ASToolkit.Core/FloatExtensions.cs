namespace ASToolkit.Core;

/// <summary>
/// Provides exact comparisons of floating-point values.
/// </summary>
public static class FloatExtensions
{
    extension(float value)
    {
        /// <summary>
        /// Determines whether the value equals zero, without any tolerance.
        /// </summary>
        /// <returns><see langword="true"/> if the value is zero; otherwise, <see langword="false"/>.</returns>
        public bool IsExactlyZero() => value == 0;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        /// <summary>
        /// Determines whether the value equals <paramref name="other"/>, without any tolerance.
        /// </summary>
        /// <param name="other">The value to compare with.</param>
        /// <returns><see langword="true"/> if the values are equal; otherwise, <see langword="false"/>.</returns>
        public bool IsExactly(float other) => value == other;
    }
}
