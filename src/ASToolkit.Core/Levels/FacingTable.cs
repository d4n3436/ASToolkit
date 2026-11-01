using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the facings the original levels give each decor object against each side of a wall and on open floor.
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
    private const float RareShare = 0.05f;
    private const float DistanceSlack = 0.05f;
    private const int MinDistanceSamples = 3;
    private const float LowQuantile = 0.1f;
    private const float HeightSlack = 3;
    private const float WallShare = 0.9f;

    private readonly ObjectTable _objects;
    private readonly Dictionary<(KnownObject Id, WallSide Side), List<byte>> _dirs;
    private readonly Dictionary<(KnownObject Id, WallSide Side), Dictionary<int, int>> _frames;
    private readonly Dictionary<KnownObject, List<byte>> _open;
    private readonly IReadOnlySet<(KnownObject Id, int Frame)> _leaning;
    private readonly Dictionary<(KnownObject Id, WallSide Side, int Frame), List<(float Distance, float Z)>> _placements;
    private readonly Dictionary<(KnownObject Id, WallSide Side, int Frame), (float Min, float Max, int Count)> _distances;
    private readonly Dictionary<(KnownObject Id, WallSide Side), float> _lowDistances;
    private readonly Dictionary<KnownObject, float> _wallReaches;

    private FacingTable(ObjectTable objects, Dictionary<(KnownObject, WallSide), List<byte>> dirs, Dictionary<KnownObject, List<byte>> open,
        IReadOnlySet<(KnownObject, int)> leaning, Dictionary<(KnownObject, WallSide, int), List<(float, float)>> placements)
    {
        _distances = placements.ToDictionary(p => p.Key, p => (p.Value.Min(m => m.Item1), p.Value.Max(m => m.Item1), p.Value.Count));
        _lowDistances = placements
            .GroupBy(p => (p.Key.Item1, p.Key.Item2))
            .Select(g => (g.Key, Distances: g.SelectMany(p => p.Value.Select(m => m.Item1)).Order().ToArray()))
            .Where(p => p.Distances.Length >= MinSamples)
            .ToDictionary(p => p.Key, p => p.Distances[(int)(LowQuantile * p.Distances.Length)]);
        
        _wallReaches = placements
            .GroupBy(p => p.Key.Item1)
            .Select(g => (Id: g.Key, Wall: g.Sum(p => p.Value.Count), Open: open.GetValueOrDefault(g.Key)?.Count ?? 0, Reach: g.Max(p => p.Value.Max(m => m.Item1))))
            .Where(p => p.Wall + p.Open >= MinSamples && p.Wall >= WallShare * (p.Wall + p.Open))
            .ToDictionary(p => p.Id, p => p.Reach);
        
        _objects = objects;
        _dirs = dirs;
        _placements = placements;
        _frames = dirs.Where(p => objects.TryFind(p.Key.Item1, out _))
            .ToDictionary(p => p.Key, p =>
            {
                objects.TryFind(p.Key.Item1, out var obj);
                return p.Value.GroupBy(d => obj.GetFrame(d, int.MaxValue)).ToDictionary(g => g.Key, g => g.Count());
            });
        _open = open;
        _leaning = leaning;
    }

    /// <summary>
    /// Records the facing and wall distance of every decor object standing on the floor or hanging on a wall in the given levels.
    /// </summary>
    public static FacingTable Build(IEnumerable<LevelModel> models, ObjectTable objects, IReadOnlySet<(KnownObject Id, int Frame)> leaning)
    {
        var dirs = new Dictionary<(KnownObject, WallSide), List<byte>>();
        var open = new Dictionary<KnownObject, List<byte>>();
        var placements = new Dictionary<(KnownObject, WallSide, int), List<(float, float)>>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight || o.Id.IsWallMounted))
            {
                var wall = FindWall(model.Edges, levelObject.U, levelObject.V);
                if (wall.HasValue)
                {
                    Add(dirs, (levelObject.Id, wall.Value.Side), levelObject.Dir);
                    if (!objects.TryFind(levelObject.Id, out var obj))
                        continue;
                    
                    var key = (levelObject.Id, wall.Value.Side, obj.FramesPerDirection > 0 ? obj.GetFrame(levelObject.Dir, int.MaxValue) : 0);
                    if (!placements.TryGetValue(key, out var list))
                    {
                        list = [];
                        placements[key] = list;
                    }

                    list.Add((wall.Value.Distance, levelObject.Z));
                }
                else
                {
                    Add(open, levelObject.Id, levelObject.Dir);
                }
            }
        }

        return new FacingTable(objects, dirs, open, leaning, placements);
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
                    continue;

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
    /// Returns the side and distance of the nearest wall within one and a half cells of a point, or null when there is none.
    /// </summary>
    public static (WallSide Side, float Distance)? FindWall(IReadOnlyDictionary<EdgeKey, char> edges, float u, float v) => FindEdge(edges, u, v, wallsOnly: true);

    private static (WallSide Side, float Distance)? FindEdge(IReadOnlyDictionary<EdgeKey, char> edges, float u, float v, bool wallsOnly)
    {
        int cu = (int)MathF.Floor(u);
        int cv = (int)MathF.Floor(v);
        float best = Reach;
        WallSide? side = null;
        for (int a = cu - SearchCells; a <= cu + SearchCells + 1; a++)
        {
            for (int b = cv - SearchCells; b <= cv + SearchCells + 1; b++)
            {
                if (edges.TryGetValue(new EdgeKey('u', a, b), out char k1) && (k1 == 'W' || !wallsOnly))
                {
                    float dv = Math.Max(0, Math.Max(b - v, v - (b + 1)));
                    float d = MathF.Sqrt((u - a) * (u - a) + dv * dv);
                    if (d < best)
                    {
                        (best, side) = (d, u >= a ? WallSide.LowerU : WallSide.UpperU);
                    }
                }

                if (edges.TryGetValue(new EdgeKey('v', a, b), out char k2) && (k2 == 'W' || !wallsOnly))
                {
                    float du = Math.Max(0, Math.Max(a - u, u - (a + 1)));
                    float d = MathF.Sqrt((v - b) * (v - b) + du * du);
                    if (d < best)
                    {
                        (best, side) = (d, v >= b ? WallSide.LowerV : WallSide.UpperV);
                    }
                }
            }
        }

        return side.HasValue ? (side.Value, best) : null;
    }

    /// <summary>
    /// Returns whether an object with a facing at a point faces as the originals turn it against the nearest wall, stands off that
    /// wall as they stand it when it is solid, and whether a frame they mostly lean on a wall has one to lean on, unless it lies on a
    /// blood pool as a fresh kill.
    /// </summary>
    public bool Fits(KnownObject id, byte dir, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v, bool onBlood = false) =>
        Fits(id, dir, FindWall(edges, u, v), onBlood);

    /// <summary>
    /// Returns whether an object stands within reach of a wall at a point, or the originals stand it away from walls at least one time in ten.
    /// </summary>
    public bool IsByWall(KnownObject id, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v) =>
        !_wallReaches.TryGetValue(id, out float reach) || FindWall(edges, u, v)?.Distance <= reach + DistanceSlack;

    /// <summary>
    /// Returns the distance from a wall side that nine in ten of the originals' objects keep, or null when they have too few against it.
    /// </summary>
    public float? GetLowDistance(KnownObject id, WallSide side) => _lowDistances.TryGetValue((id, side), out float distance) ? distance : null;

    /// <summary>
    /// Returns whether a wall-mounted object hangs on a wall side, with a frame, distance and height that one of the originals
    /// gives it, and with no doorway nearer than that wall.
    /// </summary>
    public bool Hangs(KnownObject id, byte dir, float z, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v)
    {
        var wall = FindWall(edges, u, v);
        if (!wall.HasValue || FindEdge(edges, u, v, wallsOnly: false)?.Distance < wall.Value.Distance || !_objects.TryFind(id, out var obj))
        {
            return false;
        }

        var key = (id, wall.Value.Side, obj.GetFrame(dir, int.MaxValue));
        return _placements.TryGetValue(key, out var samples)
            && samples.Any(sample => MathF.Abs(sample.Distance - wall.Value.Distance) <= DistanceSlack && MathF.Abs(sample.Z - z) <= HeightSlack);
    }

    /// <summary>
    /// Returns whether the originals give an object with a facing at a point that frame at least one time in twenty against the
    /// same side of a wall.
    /// </summary>
    public bool IsCommon(KnownObject id, byte dir, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v) => IsCommon(id, dir, FindWall(edges, u, v));

    /// <summary>
    /// Returns a facing the originals give an object in a like spot, drawn by how often they give it, or any fitting facing
    /// when they have none.
    /// </summary>
    public byte PickFacing(Random rng, KnownObject id, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v)
    {
        var wall = FindWall(edges, u, v);
        var seen = (wall.HasValue ? _dirs.GetValueOrDefault((id, wall.Value.Side)) : _open.GetValueOrDefault(id)) ?? [];
        Span<sbyte> passes = stackalloc sbyte[256];
        int count = 0;
        foreach (byte dir in seen)
        {
            if (passes[dir] == 0)
            {
                passes[dir] = (sbyte)(Fits(id, dir, wall, false) && IsCommon(id, dir, wall) ? 1 : -1);
            }

            if (passes[dir] > 0)
            {
                count++;
            }
        }

        if (count > 0)
        {
            int pick = rng.Next(count);
            foreach (byte dir in seen)
            {
                if (passes[dir] > 0 && pick-- == 0)
                {
                    return dir;
                }
            }
        }

        byte[] dirs = [];
        if (_objects.TryFind(id, out var obj) && obj.Directions > 0)
        {
            dirs = Enumerable.Range(0, obj.Directions)
                .Select(i => (byte)(i * 256 / obj.Directions))
                .Where(d => Fits(id, d, wall, false))
                .ToArray();
        }

        return dirs.Length > 0 ? dirs[rng.Next(dirs.Length)] : (byte)rng.Next(256);
    }

    /// <summary>
    /// Returns a value indicating whether the originals support a facing for the object in that spot.
    /// </summary>
    public bool HasFacing(KnownObject id, IReadOnlyDictionary<EdgeKey, char> edges, float u, float v)
    {
        var wall = FindWall(edges, u, v);
        var seen = (wall.HasValue ? _dirs.GetValueOrDefault((id, wall.Value.Side)) : _open.GetValueOrDefault(id)) ?? [];
        if (seen.Any(d => Fits(id, d, wall, false)))
        {
            return true;
        }

        return _objects.TryFind(id, out var obj) && obj.Directions > 0
            && Enumerable.Range(0, obj.Directions).Select(i => (byte)(i * 256 / obj.Directions)).Any(d => Fits(id, d, wall, false));
    }

    private bool IsCommon(KnownObject id, byte dir, (WallSide Side, float Distance)? wall)
    {
        if (!wall.HasValue || !_objects.TryFind(id, out var obj) || obj.FramesPerDirection == 0 || !_frames.TryGetValue((id, wall.Value.Side), out var frames))
        {
            return true;
        }

        int total = frames.Values.Sum();
        return total < MinSamples || frames.GetValueOrDefault(obj.GetFrame(dir, int.MaxValue)) >= RareShare * total;
    }

    private bool Fits(KnownObject id, byte dir, (WallSide Side, float Distance)? wall, bool onBlood)
    {
        if (!_objects.TryFind(id, out var obj) || obj.FramesPerDirection == 0)
        {
            return true;
        }

        int frame = obj.GetFrame(dir, int.MaxValue);
        if (wall.HasValue && _frames.TryGetValue((id, wall.Value.Side), out var frames) && _dirs[(id, wall.Value.Side)].Count >= MinSamples && !frames.ContainsKey(frame))
        {
            return false;
        }

        if (obj.IsSolid && wall.HasValue && _distances.TryGetValue((id, wall.Value.Side, frame), out var range) && range.Count >= MinDistanceSamples
            && (wall.Value.Distance < range.Min - DistanceSlack || wall.Value.Distance > range.Max + DistanceSlack))
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
