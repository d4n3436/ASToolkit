namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the draws a building grammar makes from its random stream.
/// </summary>
internal sealed class GrammarRandom
{
    private readonly Random _rng;

    public GrammarRandom(Random rng)
    {
        _rng = rng;
    }

    public double Next() => _rng.NextDouble();

    public double Uniform(double low, double high) => low + (high - low) * _rng.NextDouble();

    // Returns an integer from low up to high exclusive, or low when the range is empty
    public int Range(int low, int high) => high > low ? _rng.Next(low, high) : low;

    public int Inclusive(int low, int high) => Range(low, high + 1);

    public T Choice<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    public T[] Shuffled<T>(IEnumerable<T> items)
    {
        var array = items.ToArray();
        _rng.Shuffle(array);
        return array;
    }

    public T Weighted<T>(IReadOnlyList<(T Item, double Weight)> items)
    {
        double pick = _rng.NextDouble() * items.Sum(item => item.Weight);
        foreach ((var item, double weight) in items)
        {
            pick -= weight;
            if (pick < 0)
            {
                return item;
            }
        }

        return items[^1].Item;
    }

    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.ToEven);
}
