namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides extension methods for <see cref="Angle"/>.
/// </summary>
public static class AngleExtensions
{
    extension(Random rng)
    {
        /// <summary>
        /// Returns a random direction.
        /// </summary>
        public Angle NextAngle() => new((byte)rng.Next(Angle.FullTurn));
    }
}
