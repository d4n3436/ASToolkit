namespace ASToolkit.Core;

/// <summary>
/// Provides tolerant comparisons of single-precision values.
/// </summary>
public static class FloatExtensions
{
    private const float Tolerance = 1e-6f;

    extension(float value)
    {
        /// <summary>
        /// Returns whether the value is zero within a small tolerance.
        /// </summary>
        public bool IsZero(float tolerance = Tolerance) => MathF.Abs(value) <= tolerance;

        /// <summary>
        /// Returns whether two values are equal within a tolerance relative to their size.
        /// </summary>
        public bool IsNear(float other, float tolerance = Tolerance) =>
            MathF.Abs(value - other) <= tolerance * MathF.Max(1, MathF.Max(MathF.Abs(value), MathF.Abs(other)));
    }
}
