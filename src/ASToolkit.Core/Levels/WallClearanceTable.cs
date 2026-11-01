using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents how close the original levels place each decor object to a wall, in cells.
/// </summary>
public sealed class WallClearanceTable
{
    private const int MinSamples = 20;
    private const float LowQuantile = 0.05f;
    private const float FloorHeight = 5;

    private readonly Dictionary<KnownObject, (float Minimum, float Low)> _byId;

    private WallClearanceTable(Dictionary<KnownObject, (float, float)> byId)
    {
        _byId = byId;
    }

    /// <summary>
    /// Measures every decor object standing on the floor of the given levels.
    /// </summary>
    public static WallClearanceTable Build(IEnumerable<LevelModel> models)
    {
        var samples = new Dictionary<KnownObject, List<float>>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight))
            {
                float d = model.Edges.MeasureClearance(levelObject.U, levelObject.V);
                if (float.IsInfinity(d))
                {
                    continue;
                }

                if (!samples.TryGetValue(levelObject.Id, out var list))
                {
                    list = [];
                    samples[levelObject.Id] = list;
                }

                list.Add(d);
            }
        }

        var byId = new Dictionary<KnownObject, (float, float)>();
        foreach (var (id, list) in samples)
        {
            list.Sort();
            float low = list.Count >= MinSamples ? list[(int)(LowQuantile * list.Count)] : list[0];
            byId[id] = (list[0], low);
        }

        return new WallClearanceTable(byId);
    }

    /// <summary>
    /// Returns the closest any original puts an object to a wall, and the distance 95% of them keep, or false for an object the originals don't place.
    /// </summary>
    public bool TryGetClearance(KnownObject id, out float minimum, out float low)
    {
        bool found = _byId.TryGetValue(id, out var c);
        (minimum, low) = c;
        return found;
    }
}
