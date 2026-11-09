namespace ASToolkit.Core;

/// <summary>
/// Provides extension methods for <see cref="Random"/>.
/// </summary>
public static class RandomExtensions
{
    extension(Random rng)
    {
        /// <summary>
        /// Returns an integer drawn uniformly from an inclusive range.
        /// </summary>
        public int NextInclusive(ValueRange<int> range) => rng.Next(range.Min, range.Max + 1);

        /// <summary>
        /// Returns a draw from the standard normal distribution.
        /// </summary>
        public float NextNormal() => MathF.Sqrt(-2 * MathF.Log(1 - rng.NextSingle())) * MathF.Cos(2 * MathF.PI * rng.NextSingle());

        /// <summary>
        /// Returns a draw from the gamma distribution of a shape and unit scale.
        /// </summary>
        public float NextGamma(float shape)
        {
            // Use Marsaglia and Tsang's method, boosting shapes under 1
            if (shape < 1)
            {
                return rng.NextGamma(shape + 1) * MathF.Pow(1 - rng.NextSingle(), 1 / shape);
            }

            float d = shape - 1f / 3, c = 1 / MathF.Sqrt(9 * d);
            while (true)
            {
                float x = rng.NextNormal(), v = MathF.Pow(1 + c * x, 3);
                if (v > 0 && MathF.Log(1 - rng.NextSingle()) < x * x / 2 + d - d * v + d * MathF.Log(v))
                {
                    return d * v;
                }
            }
        }
    }
}
