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

    public float Next() => _rng.NextSingle();

    public float Uniform(float low, float high) => low + (high - low) * _rng.NextSingle();

    // Returns an integer from low up to high exclusive, or low when the range is empty
    public int Range(int low, int high) => high > low ? _rng.Next(low, high) : low;

    public int Inclusive(int low, int high) => Range(low, high + 1);

    public int Inclusive(ValueRange<int> range) => Inclusive(range.Min, range.Max);

    public T Choice<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    public T[] Shuffled<T>(IEnumerable<T> items)
    {
        var array = items.ToArray();
        _rng.Shuffle(array);
        return array;
    }

    public T Weighted<T>(IReadOnlyList<(T Item, float Weight)> items)
    {
        float pick = _rng.NextSingle() * items.Sum(item => item.Weight);
        foreach ((var item, float weight) in items)
        {
            pick -= weight;
            if (pick < 0)
            {
                return item;
            }
        }

        return items[^1].Item;
    }

    public static int Round(float value) => (int)MathF.Round(value, MidpointRounding.ToEven);
}
