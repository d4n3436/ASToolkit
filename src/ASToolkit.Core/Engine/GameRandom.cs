namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the engine's random source, the C runtime's LCG.
/// </summary>
public sealed class GameRandom : Random
{
    private const int Multiplier = 214013;
    private const int Increment = 2531011;
    private const int ValueShift = 16;
    private const int MaximumValue = 32767;

    /// <summary>
    /// Creates a random source with a seed.
    /// </summary>
    /// <param name="seed">The seed.</param>
    public GameRandom(int seed)
    {
        State = seed;
    }

    /// <summary>
    /// Gets the generator's state.
    /// </summary>
    public int State { get; private set; }

    /// <summary>
    /// Returns the next draw, from 0 to 32767.
    /// </summary>
    /// <returns>The draw.</returns>
    public override int Next()
    {
        State = unchecked(State * Multiplier + Increment);

        return State >> ValueShift & MaximumValue;
    }

    /// <summary>
    /// Returns the remainder of the next draw by a bound.
    /// </summary>
    /// <param name="maxValue">The bound.</param>
    /// <returns>The remainder, negative for a negative bound.</returns>
    /// <exception cref="DivideByZeroException">The bound is 0.</exception>
    public override int Next(int maxValue) => Next() % maxValue;

    /// <summary>
    /// Returns the next draw in a range.
    /// </summary>
    /// <param name="minValue">The inclusive lower bound.</param>
    /// <param name="maxValue">The exclusive upper bound.</param>
    /// <returns>The draw.</returns>
    public override int Next(int minValue, int maxValue) => minValue + Next(maxValue - minValue);

    /// <summary>
    /// Returns the next draw scaled to 0 to 1.
    /// </summary>
    /// <returns>The draw.</returns>
    public override float NextSingle() => Next() / (float)MaximumValue;

    /// <summary>
    /// Returns the next draw scaled to 0 to 1.
    /// </summary>
    /// <returns>The draw.</returns>
    public override double NextDouble() => Next() / (double)MaximumValue;
}
