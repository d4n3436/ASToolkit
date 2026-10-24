using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents how close the original levels place each decor object to a wall, in cells.
/// </summary>
public sealed class WallClearanceTable
{
    private const int MinSamples = 20;
    private const float LowQuantile = 0.05f;
    private const int SearchCells = 3;
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
                float d = MeasureDistance(model.Edges, levelObject.U, levelObject.V);
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
        foreach ((var id, var list) in samples)
        {
            list.Sort();
            float low = list.Count >= MinSamples ? list[(int)(LowQuantile * list.Count)] : list[0];
            byId[id] = (list[0], low);
        }

        return new WallClearanceTable(byId);
    }

    /// <summary>
    /// Returns the distance from a point to the nearest wall, door or fence edge within three cells, or infinity when there is none.
    /// </summary>
    public static float MeasureDistance(IReadOnlyDictionary<EdgeKey, char> edges, float u, float v)
    {
        int cu = (int)MathF.Floor(u);
        int cv = (int)MathF.Floor(v);
        float best = float.PositiveInfinity;
        for (int a = cu - SearchCells; a <= cu + SearchCells + 1; a++)
        {
            for (int b = cv - SearchCells; b <= cv + SearchCells + 1; b++)
            {
                if (edges.TryGetValue(new EdgeKey('u', a, b), out char k1) && k1 != 'e')
                {
                    float dv = Math.Max(0, Math.Max(b - v, v - (b + 1)));
                    best = Math.Min(best, MathF.Sqrt((u - a) * (u - a) + dv * dv));
                }

                if (edges.TryGetValue(new EdgeKey('v', a, b), out char k2) && k2 != 'e')
                {
                    float du = Math.Max(0, Math.Max(a - u, u - (a + 1)));
                    best = Math.Min(best, MathF.Sqrt((v - b) * (v - b) + du * du));
                }
            }
        }

        return best;
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
