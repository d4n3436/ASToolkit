using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents how close the original levels place each decor object to a wall, in cells.
/// </summary>
public sealed class WallClearanceTable
{
    private const int MinSamples = 20;
    private const double LowQuantile = 0.05;
    private const int SearchCells = 3;
    private const double FloorHeight = 5;

    private readonly Dictionary<KnownObject, (double Minimum, double Low)> _byId;

    private WallClearanceTable(Dictionary<KnownObject, (double, double)> byId)
    {
        _byId = byId;
    }

    /// <summary>
    /// Measures every decor object standing on the floor of the given levels.
    /// </summary>
    public static WallClearanceTable Build(IEnumerable<LevelModel> models)
    {
        var samples = new Dictionary<KnownObject, List<double>>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight))
            {
                double d = MeasureDistance(model.Edges, levelObject.U, levelObject.V);
                if (double.IsInfinity(d))
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

        var byId = new Dictionary<KnownObject, (double, double)>();
        foreach ((var id, var list) in samples)
        {
            list.Sort();
            double low = list.Count >= MinSamples ? list[(int)(LowQuantile * list.Count)] : list[0];
            byId[id] = (list[0], low);
        }

        return new WallClearanceTable(byId);
    }

    /// <summary>
    /// Returns the distance from a point to the nearest wall, door or fence edge within three cells, or infinity when there is none.
    /// </summary>
    public static double MeasureDistance(IReadOnlyDictionary<EdgeKey, char> edges, double u, double v)
    {
        int cu = (int)Math.Floor(u);
        int cv = (int)Math.Floor(v);
        double best = double.PositiveInfinity;
        for (int a = cu - SearchCells; a <= cu + SearchCells + 1; a++)
        {
            for (int b = cv - SearchCells; b <= cv + SearchCells + 1; b++)
            {
                if (edges.TryGetValue(new EdgeKey('u', a, b), out char k1) && k1 != 'e')
                {
                    double dv = Math.Max(0, Math.Max(b - v, v - (b + 1)));
                    best = Math.Min(best, Math.Sqrt((u - a) * (u - a) + dv * dv));
                }

                if (edges.TryGetValue(new EdgeKey('v', a, b), out char k2) && k2 != 'e')
                {
                    double du = Math.Max(0, Math.Max(a - u, u - (a + 1)));
                    best = Math.Min(best, Math.Sqrt((v - b) * (v - b) + du * du));
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the closest any original puts an object to a wall, and the distance 95% of them keep, or false for an object the originals don't place.
    /// </summary>
    public bool TryGetClearance(KnownObject id, out double minimum, out double low)
    {
        bool found = _byId.TryGetValue(id, out var c);
        (minimum, low) = c;
        return found;
    }
}
