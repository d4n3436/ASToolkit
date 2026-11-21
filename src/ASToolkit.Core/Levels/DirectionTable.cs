using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the directions the original levels give each decor object against each side of a wall and on open floor.
/// </summary>
public sealed class DirectionTable
{
    private const float FloorHeight = 5;
    private const int MinSamples = 5;
    private const float LeanDistance = 1;
    private const float LeanShare = 0.4f;
    private const float LeanContrast = 4;
    private const float RareShare = 0.05f;
    private const int MinDistanceSamples = 3;
    private const float HeightSlack = 3;
    private const float WallShare = 0.9f;

    private readonly ObjectTable _objects;
    private readonly Dictionary<(KnownObject Id, WallSides Side), List<Angle>> _dirs;
    private readonly Dictionary<(KnownObject Id, WallSides Side), Dictionary<int, int>> _frames;
    private readonly Dictionary<KnownObject, List<Angle>> _open;
    private readonly IReadOnlySet<(KnownObject Id, int Frame)> _leaning;
    private readonly Dictionary<(KnownObject Id, WallSides Side, int Frame), List<(float Distance, float Z)>> _placements;
    private readonly Dictionary<(KnownObject Id, WallSides Side, int Frame), (float Min, float Max, int Count)> _distances;
    private readonly Dictionary<KnownObject, float> _wallReaches;

    private DirectionTable(ObjectTable objects, Dictionary<(KnownObject, WallSides), List<Angle>> dirs, Dictionary<KnownObject, List<Angle>> open,
        IReadOnlySet<(KnownObject, int)> leaning, Dictionary<(KnownObject, WallSides, int), List<(float, float)>> placements)
    {
        _distances = placements.ToDictionary(p => p.Key, p => (p.Value.Min(m => m.Item1), p.Value.Max(m => m.Item1), p.Value.Count));
        
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
    /// Builds the table from the direction and wall distance of every decor object standing on the floor or hanging on a wall in the given levels.
    /// </summary>
    /// <param name="models">The level models to read.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="leaning">The frames that the levels mostly stand against a wall.</param>
    /// <returns>The table.</returns>
    public static DirectionTable Build(IEnumerable<LevelModel> models, ObjectTable objects, IReadOnlySet<(KnownObject Id, int Frame)> leaning)
    {
        var dirs = new Dictionary<(KnownObject, WallSides), List<Angle>>();
        var open = new Dictionary<KnownObject, List<Angle>>();
        var placements = new Dictionary<(KnownObject, WallSides, int), List<(float, float)>>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight || o.Id.IsWallMounted))
            {
                var wall = model.Edges.FindNearestWall(levelObject.U, levelObject.V);
                if (wall.HasValue)
                {
                    Add(dirs, (levelObject.Id, wall.Value.Side), levelObject.Direction);
                    if (!objects.TryFind(levelObject.Id, out var obj))
                        continue;
                    
                    var key = (levelObject.Id, wall.Value.Side, obj.FramesPerDirection > 0 ? obj.GetFrame(levelObject.Direction, int.MaxValue) : 0);
                    if (!placements.TryGetValue(key, out var list))
                    {
                        list = [];
                        placements[key] = list;
                    }

                    list.Add((wall.Value.Distance, levelObject.Z));
                }
                else
                {
                    Add(open, levelObject.Id, levelObject.Direction);
                }
            }
        }

        return new DirectionTable(objects, dirs, open, leaning, placements);
    }

    /// <summary>
    /// Returns the frames of objects that the given levels mostly stand right against a wall, such as a corpse slumped on one.
    /// </summary>
    /// <param name="models">The level models to read.</param>
    /// <param name="objects">The object table.</param>
    /// <returns>The object IDs and frames that the levels mostly stand against a wall.</returns>
    public static IReadOnlySet<(KnownObject Id, int Frame)> FindLeaningFrames(IEnumerable<LevelModel> models, ObjectTable objects)
    {
        var counts = new Dictionary<(KnownObject, int), (int Total, int Near)>();
        foreach (var model in models)
        {
            foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight))
            {
                if (!objects.TryFind(levelObject.Id, out var obj) || obj.FramesPerDirection == 0)
                    continue;

                var key = (levelObject.Id, obj.GetFrame(levelObject.Direction, int.MaxValue));
                (int total, int near) = counts.GetValueOrDefault(key);
                bool leans = model.Edges.FindNearestWall(levelObject.U, levelObject.V) is { Distance: < LeanDistance };
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
    /// Determines whether an object with a direction at a point faces the way the original levels turn it against the nearest wall, stands off that
    /// wall the way they stand it when it is solid, and has a wall to lean on if the original levels mostly lean its frame on a wall.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="dir">The direction of the object.</param>
    /// <param name="edges">The wall edges with their kinds.</param>
    /// <param name="u">The u coordinate of the object on the lattice.</param>
    /// <param name="v">The v coordinate of the object on the lattice.</param>
    /// <returns><see langword="true"/> if the object fits; otherwise, <see langword="false"/>.</returns>
    public bool Fits(KnownObject id, Angle dir, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v) => Fits(id, dir, edges.FindNearestWall(u, v));

    /// <summary>
    /// Determines whether an object with a direction fits against the nearest wall, given as its side and distance.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="dir">The direction of the object.</param>
    /// <param name="wall">The side and the distance of the nearest wall, or null if there isn't one.</param>
    /// <returns><see langword="true"/> if the object fits; otherwise, <see langword="false"/>.</returns>
    public bool Fits(KnownObject id, Angle dir, (WallSides Side, float Distance)? wall)
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
            && (wall.Value.Distance < range.Min - EdgeMapExtensions.WallSlack || wall.Value.Distance > range.Max + EdgeMapExtensions.WallSlack))
        {
            return false;
        }

        return !_leaning.Contains((id, frame)) || wall is { Distance: < LeanDistance };
    }

    /// <summary>
    /// Determines whether an object at a point stands within reach of a wall, or is one that the original levels stand away from walls at least one time in ten.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="edges">The wall edges with their kinds.</param>
    /// <param name="u">The u coordinate of the object on the lattice.</param>
    /// <param name="v">The v coordinate of the object on the lattice.</param>
    /// <returns><see langword="true"/> if the object stands within reach of a wall or is allowed away from walls; otherwise, <see langword="false"/>.</returns>
    public bool IsByWall(KnownObject id, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v) => IsByWall(id, edges.FindNearestWall(u, v));

    /// <summary>
    /// Determines whether an object stands within reach of the nearest wall, given as its side and distance.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="wall">The side and the distance of the nearest wall, or null if there isn't one.</param>
    /// <returns><see langword="true"/> if the object stands within reach of the wall or is allowed away from walls; otherwise, <see langword="false"/>.</returns>
    public bool IsByWall(KnownObject id, (WallSides Side, float Distance)? wall) =>
        !_wallReaches.TryGetValue(id, out float reach) || wall?.Distance <= reach + EdgeMapExtensions.WallSlack;

    /// <summary>
    /// Determines whether a wall-mounted object hangs on a wall side, with a frame, distance and height that one of the original levels gives it,
    /// and without a doorway nearer than that wall.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="dir">The direction of the object.</param>
    /// <param name="z">The height of the object.</param>
    /// <param name="edges">The wall edges with their kinds.</param>
    /// <param name="u">The u coordinate of the object on the lattice.</param>
    /// <param name="v">The v coordinate of the object on the lattice.</param>
    /// <returns><see langword="true"/> if the object hangs on the wall; otherwise, <see langword="false"/>.</returns>
    public bool Hangs(KnownObject id, Angle dir, float z, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v) => Hangs(id, dir, z, edges.FindNearestWall(u, v), edges, u, v);

    /// <summary>
    /// Determines whether a wall-mounted object hangs on the nearest wall, given as its side and distance, without a doorway among the edges that is nearer.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="dir">The direction of the object.</param>
    /// <param name="z">The height of the object.</param>
    /// <param name="wall">The side and the distance of the nearest wall, or null if there isn't one.</param>
    /// <param name="edges">The wall edges with their kinds.</param>
    /// <param name="u">The u coordinate of the object on the lattice.</param>
    /// <param name="v">The v coordinate of the object on the lattice.</param>
    /// <returns><see langword="true"/> if the object hangs on the wall; otherwise, <see langword="false"/>.</returns>
    public bool Hangs(KnownObject id, Angle dir, float z, (WallSides Side, float Distance)? wall, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v)
    {
        if (!wall.HasValue || edges.FindNearestEdge(u, v)?.Distance < wall.Value.Distance || !_objects.TryFind(id, out var obj))
        {
            return false;
        }

        var key = (id, wall.Value.Side, obj.GetFrame(dir, int.MaxValue));
        return _placements.TryGetValue(key, out var samples)
            && samples.Any(sample => MathF.Abs(sample.Distance - wall.Value.Distance) <= EdgeMapExtensions.WallSlack && MathF.Abs(sample.Z - z) <= HeightSlack);
    }

    /// <summary>
    /// Determines whether the original levels give an object with a direction at a point that frame at least one time in twenty against the
    /// same side of a wall.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="dir">The direction of the object.</param>
    /// <param name="edges">The wall edges with their kinds.</param>
    /// <param name="u">The u coordinate of the object on the lattice.</param>
    /// <param name="v">The v coordinate of the object on the lattice.</param>
    /// <returns><see langword="true"/> if the frame is common against that side of the wall; otherwise, <see langword="false"/>.</returns>
    public bool IsCommon(KnownObject id, Angle dir, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v) => IsCommon(id, dir, edges.FindNearestWall(u, v));

    /// <summary>
    /// Determines whether the original levels give an object that frame at least one time in twenty against the nearest wall, given as its side and
    /// distance.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="dir">The direction of the object.</param>
    /// <param name="wall">The side and the distance of the nearest wall, or null if there isn't one.</param>
    /// <returns><see langword="true"/> if the frame is common against that side of the wall; otherwise, <see langword="false"/>.</returns>
    public bool IsCommon(KnownObject id, Angle dir, (WallSides Side, float Distance)? wall)
    {
        if (!wall.HasValue || !_objects.TryFind(id, out var obj) || obj.FramesPerDirection == 0 || !_frames.TryGetValue((id, wall.Value.Side), out var frames))
        {
            return true;
        }

        int total = frames.Values.Sum();
        return total < MinSamples || frames.GetValueOrDefault(obj.GetFrame(dir, int.MaxValue)) >= RareShare * total;
    }

    /// <summary>
    /// Picks a direction that the original levels give an object in a similar spot, drawn by how often they give it, or any fitting direction
    /// if they don't give the object one.
    /// </summary>
    /// <param name="rng">The random number generator.</param>
    /// <param name="id">The object ID.</param>
    /// <param name="edges">The wall edges with their kinds.</param>
    /// <param name="u">The u coordinate of the object on the lattice.</param>
    /// <param name="v">The v coordinate of the object on the lattice.</param>
    /// <returns>The direction.</returns>
    public Angle PickDirection(Random rng, KnownObject id, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v)
    {
        var wall = edges.FindNearestWall(u, v);
        var seen = (wall.HasValue ? _dirs.GetValueOrDefault((id, wall.Value.Side)) : _open.GetValueOrDefault(id)) ?? [];
        Span<sbyte> passes = stackalloc sbyte[256];
        int count = 0;
        foreach (var dir in seen)
        {
            if (passes[dir.Value] == 0)
            {
                passes[dir.Value] = (sbyte)(Fits(id, dir, wall) && IsCommon(id, dir, wall) ? 1 : -1);
            }

            if (passes[dir.Value] > 0)
            {
                count++;
            }
        }

        if (count > 0)
        {
            int pick = rng.Next(count);
            foreach (var dir in seen)
            {
                if (passes[dir.Value] > 0 && pick-- == 0)
                {
                    return dir;
                }
            }
        }

        Angle[] dirs = [];
        if (_objects.TryFind(id, out var obj) && obj.Directions > 0)
        {
            dirs = Enumerable.Range(0, obj.Directions)
                .Select(i => new Angle((byte)(i * 256 / obj.Directions)))
                .Where(d => Fits(id, d, wall))
                .ToArray();
        }

        return dirs.Length > 0 ? dirs[rng.Next(dirs.Length)] : rng.NextAngle();
    }

    /// <summary>
    /// Determines whether the original levels support a direction for the object in that spot.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="edges">The wall edges with their kinds.</param>
    /// <param name="u">The u coordinate of the object on the lattice.</param>
    /// <param name="v">The v coordinate of the object on the lattice.</param>
    /// <returns><see langword="true"/> if a direction is supported; otherwise, <see langword="false"/>.</returns>
    public bool HasDirection(KnownObject id, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v)
    {
        var wall = edges.FindNearestWall(u, v);
        var seen = (wall.HasValue ? _dirs.GetValueOrDefault((id, wall.Value.Side)) : _open.GetValueOrDefault(id)) ?? [];
        if (seen.Any(d => Fits(id, d, wall)))
        {
            return true;
        }

        return _objects.TryFind(id, out var obj) && obj.Directions > 0
            && Enumerable.Range(0, obj.Directions).Select(i => new Angle((byte)(i * 256 / obj.Directions))).Any(d => Fits(id, d, wall));
    }

    private static void Add<TKey>(Dictionary<TKey, List<Angle>> lists, TKey key, Angle dir)
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
