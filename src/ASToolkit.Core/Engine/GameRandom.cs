namespace ASToolkit.Core.Engine;

internal sealed class GameRandom : Random
{
    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;
    private const int ValueShift = 16;
    private const int MaximumValue = 32767;
    private uint _state;

    public GameRandom(int seed)
    {
        _state = unchecked((uint)seed);
    }

    public override int Next()
    {
        _state = unchecked(_state * Multiplier + Increment);

        return (int)(_state >> ValueShift) & MaximumValue;
    }

    public override int Next(int maxValue) => maxValue > 0 ? Next() % maxValue : 0;

    public override int Next(int minValue, int maxValue) => minValue + Next(maxValue - minValue);

    public override float NextSingle() => Next() / (float)MaximumValue;

    public override double NextDouble() => Next() / (double)MaximumValue;
}
