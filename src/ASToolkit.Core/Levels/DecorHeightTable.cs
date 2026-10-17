using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the decor objects a set of levels uses and the heights it places each at.
/// </summary>
public sealed class DecorHeightTable
{
    private const double Tolerance = 3;

    private readonly Dictionary<KnownObject, (double Min, double Max)> _ranges = [];

    /// <summary>
    /// Records an object placed at a height.
    /// </summary>
    public void Add(KnownObject id, double z)
    {
        _ranges[id] = _ranges.TryGetValue(id, out var r) ? (Math.Min(r.Min, z), Math.Max(r.Max, z)) : (z, z);
    }

    /// <summary>
    /// Returns whether the levels use an object at all.
    /// </summary>
    public bool Contains(KnownObject id) => _ranges.ContainsKey(id);

    /// <summary>
    /// Returns whether the levels use an object at about the given height.
    /// </summary>
    public bool Allows(KnownObject id, double z) => _ranges.TryGetValue(id, out var r) && z >= r.Min - Tolerance && z <= r.Max + Tolerance;
}
