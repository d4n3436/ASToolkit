using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the facings the original levels give each decor object against walls of each direction and on open floor.
/// </summary>
public sealed class FacingTable
{
    private const float Reach = 1.5f;
    private const int SearchCells = 2;
    private const float FloorHeight = 5;
    private const int MinSamples = 5;
    private const float LeanDistance = 1;
    private const float LeanShare = 0.4f;
    private const float LeanContrast = 4;

    private readonly ObjectTable _objects;
    private readonly Dictionary<(KnownObject Id, char Axis), List<byte>> _dirs;
    private readonly Dictionary<(KnownObject Id, char Axis), HashSet<int>> _frames;
    private readonly Dictionary<KnownObject, List<byte>> _open;
    private readonly IReadOnlySet<(KnownObject Id, int Frame)> _leaning;

    private FacingTable(ObjectTable objects, Dictionary<(KnownObject, char), List<byte>> dirs, Dictionary<KnownObject, List<byte>> open, IReadOnlySet<(KnownObject, int)> leaning)
    {
        _objects = objects;
        _dirs = dirs;
        _frames = dirs.Where(p => objects.TryFind(p.Key.Item1, out _))
            .ToDictionary(p => p.Key, p =>
            {
                objects.TryFind(p.Key.Item1, out var obj);
                return p.Value.Select(d => obj.GetFrame(d, int.MaxValue)).ToHashSet();
            });
        _open = open;
        _leaning = leaning;
    }

    /// <summary>
    /// Records the facing of every decor object standing on the floor of the given levels.
    /// </summary>
    public static FacingTable Build(IEnumerable<LevelModel> models, ObjectTable objects, IReadOnlySet<(KnownObject Id, int Frame)> leaning)
    {
        var dirs = new Dictionary<(KnownObject, char), List<byte>>();
        var open = new Dictionary<KnownObject, List<byte>>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight))
            {
                var wall = FindWall(model.Edges, levelObject.U, levelObject.V);
                if (wall.HasValue)
                {
                    Add(dirs, (levelObject.Id, wall.Value.Axis), levelObject.Dir);
                }
                else
                {
                    Add(open, levelObject.Id, levelObject.Dir);
                }
            }
        }

        return new FacingTable(objects, dirs, open, leaning);
    }

    /// <summary>
    /// Returns the frames of objects that the given levels mostly stand right against a wall, such as a corpse slumped on one.
    /// </summary>
    public static IReadOnlySet<(KnownObject Id, int Frame)> FindLeaningFrames(IEnumerable<LevelModel> models, ObjectTable objects)
    {
        var counts = new Dictionary<(KnownObject, int), (int Total, int Near)>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight))
            {
                if (!objects.TryFind(levelObject.Id, out var obj) || obj.FramesPerDirection == 0)
                {
                    continue;
                }

                var key = (levelObject.Id, obj.GetFrame(levelObject.Dir, int.MaxValue));
                (int total, int near) = counts.GetValueOrDefault(key);
                bool leans = FindWall(model.Edges, levelObject.U, levelObject.V) is { Distance: < LeanDistance };
                counts[key] = (total + 1, near + (leans ? 1 : 0));
            }
        }

        var leaning = new HashSet<(KnownObject, int)>();
        foreach (((var id, int frame), (int total, int near)) in counts)
        {
            var others = counts
                .Where(p => p.Key.Item1 == id && p.Key.Item2 != frame)
                .Select(p => p.Value)
                .ToArray();
            
            int otherTotal = others.Sum(p => p.Total);
            int otherNear = others.Sum(p => p.Near);
            if (total >= MinSamples && near >= LeanShare * total && otherTotal >= MinSamples && otherNear * LeanContrast < otherTotal * (near / (float)total))
            {
                leaning.Add((id, frame));
            }
        }

        return leaning;
    }

    /// <summary>
    /// Returns the axis and distance of the nearest wall within one and a half cells of a point, or null when there is none.
    /// </summary>
    public static (char Axis, float Distance)? FindWall(IReadOnlyDictionary<EdgeKey, char> edges, float u, float v)
    {
        int cu = (int)MathF.Floor(u);
        int cv = (int)MathF.Floor(v);
        float best = Reach;
        char? axis = null;
        for (int a = cu - SearchCells; a <= cu + SearchCells + 1; a++)
        {
            for (int b = cv - SearchCells; b <= cv + SearchCells + 1; b++)
            {
                if (edges.TryGetValue(new EdgeKey('u', a, b), out char k1) && k1 == 'W')
                {
                    float dv = Math.Max(0, Math.Max(b - v, v - (b + 1)));
                    float d = MathF.Sqrt((u - a) * (u - a) + dv * dv);
                    if (d < best)
                    {
                        (best, axis) = (d, 'u');
                    }
                }

                if (edges.TryGetValue(new EdgeKey('v', a, b), out char k2) && k2 == 'W')
                {
                    float du = Math.Max(0, Math.Max(a - u, u - (a + 1)));
                    float d = MathF.Sqrt((v - b) * (v - b) + du * du);
                    if (d < best)
                    {
                        (best, axis) = (d, 'v');
                    }
                }
            }
        }

        return axis.HasValue ? (axis.Value, best) : null;
    }

    /// <summary>
    /// Returns whether an object with a facing at a point faces as the originals turn it against the nearest wall, and
    /// whether a frame they mostly lean on a wall has one to lean on, unless it lies on a blood pool as a fresh kill.
    /// </summary>
    public bool Fits(KnownObject id, byte dir, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v, bool onBlood = false) =>
        Fits(id, dir, FindWall(edges, u, v), onBlood);

    /// <summary>
    /// Returns a facing the originals give an object in a like spot that fits, or any fitting facing when they have none.
    /// </summary>
    public byte PickFacing(Random rng, KnownObject id, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v)
    {
        var wall = FindWall(edges, u, v);
        var seen = (wall.HasValue ? _dirs.GetValueOrDefault((id, wall.Value.Axis)) : _open.GetValueOrDefault(id)) ?? [];
        byte[] dirs = seen.Where(d => Fits(id, d, wall, false)).ToArray();
        if (dirs.Length == 0 && _objects.TryFind(id, out var obj) && obj.Directions > 0)
        {
            dirs = Enumerable.Range(0, obj.Directions)
                .Select(i => (byte)(i * 256 / obj.Directions))
                .Where(d => Fits(id, d, wall, false))
                .ToArray();
        }

        return dirs.Length > 0 ? dirs[rng.Next(dirs.Length)] : (byte)rng.Next(256);
    }

    private bool Fits(KnownObject id, byte dir, (char Axis, float Distance)? wall, bool onBlood)
    {
        if (!_objects.TryFind(id, out var obj) || obj.FramesPerDirection == 0)
        {
            return true;
        }

        int frame = obj.GetFrame(dir, int.MaxValue);
        if (wall.HasValue && _frames.TryGetValue((id, wall.Value.Axis), out var frames) && _dirs[(id, wall.Value.Axis)].Count >= MinSamples && !frames.Contains(frame))
        {
            return false;
        }

        return onBlood || !_leaning.Contains((id, frame)) || wall is { Distance: < LeanDistance };
    }

    private static void Add<TKey>(Dictionary<TKey, List<byte>> lists, TKey key, byte dir)
        where TKey : notnull
    {
        if (!lists.TryGetValue(key, out var list))
        {
            list = [];
            lists[key] = list;
        }

        list.Add(dir);
    }
}
