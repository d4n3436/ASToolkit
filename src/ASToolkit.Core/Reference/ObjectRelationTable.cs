using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents how the objects of each theme stand by other objects, walls and doorways in a set of levels.
/// </summary>
public sealed class ObjectRelationTable
{
    private const float Reach = 45;
    private const float WallReach = 0.4f;
    private const float FloorHeight = 5;
    private const int PointsPerCell = 3;
    private const int MinCount = 10;
    private const float MinShare = 0.25f;
    private const float MinLift = 3;
    private const float LowQuantile = 0.05f;
    private const int BucketSize = 80;
    private const float TouchingShare = 0.5f;

    private readonly Dictionary<(LevelTheme Theme, KnownObject Id), ObjectRelations> _relations;
    private readonly Dictionary<(LevelTheme Theme, KnownObject Id, KnownObject Anchor), float> _anchorShares;
    private readonly Dictionary<(LevelTheme Theme, BlockingClass Blocking), float> _doorClearances;
    private readonly Dictionary<(LevelTheme Theme, KnownObject Anchor), ObjectCompanion[]> _companions;
    private readonly Dictionary<(LevelTheme Theme, KnownObject Anchor, KnownObject Id), float> _companionRates;
    private readonly Dictionary<(LevelTheme Theme, KnownObject Id, KnownObject Other, bool Raised), float> _spacings;

    private ObjectRelationTable(Dictionary<(LevelTheme, KnownObject), ObjectRelations> relations, Dictionary<(LevelTheme, KnownObject, KnownObject), float> anchorShares,
        Dictionary<(LevelTheme, BlockingClass), float> doorClearances, Dictionary<(LevelTheme, KnownObject), ObjectCompanion[]> companions,
        Dictionary<(LevelTheme, KnownObject, KnownObject), float> companionRates, Dictionary<(LevelTheme, KnownObject, KnownObject, bool), float> spacings)
    {
        _spacings = spacings;
        _companionRates = companionRates;
        _relations = relations;
        _anchorShares = anchorShares;
        _doorClearances = doorClearances;
        _companions = companions;
    }

    /// <summary>
    /// Measures the relations of the objects of every lab and military level of a set.
    /// </summary>
    public static ObjectRelationTable Build(IEnumerable<ReferenceLevel> levels, ObjectTable objects)
    {
        var tallies = new Dictionary<LevelTheme, Tally>();
        int levelIndex = 0;
        foreach (var level in levels)
        {
            var theme = level.Model.Theme;
            if (theme == LevelTheme.Outdoor)
            {
                continue;
            }

            if (!tallies.TryGetValue(theme, out var tally))
            {
                tally = new Tally();
                tallies[theme] = tally;
            }

            tally.Add(level, objects, new Random(levelIndex++));
        }

        var relations = new Dictionary<(LevelTheme, KnownObject), ObjectRelations>();
        var anchorShares = new Dictionary<(LevelTheme, KnownObject, KnownObject), float>();
        var doorClearances = new Dictionary<(LevelTheme, BlockingClass), float>();
        var companions = new Dictionary<(LevelTheme, KnownObject), ObjectCompanion[]>();
        var companionRates = new Dictionary<(LevelTheme, KnownObject, KnownObject), float>();
        var spacings = new Dictionary<(LevelTheme, KnownObject, KnownObject, bool), float>();
        foreach (var (theme, tally) in tallies)
        {
            foreach (((var id, var other, bool raised), float spacing) in tally.Spacings)
            {
                spacings[(theme, id, other, raised)] = spacing;
            }

            float randomShare = 1f / Math.Max(1, tally.RandomPoints);
            foreach (var group in tally.Owned.GroupBy(pair => pair.Key.Anchor))
            {
                int anchorCount = tally.Counts[group.Key];
                foreach (var pair in group)
                {
                    companionRates[(theme, group.Key, pair.Key.Id)] = pair.Value.Count / (float)anchorCount;
                }

                if (anchorCount < MinCount)
                {
                    continue;
                }

                var kept = group
                    .Where(pair => IsCompanion(pair.Key.Id))
                    .Select(pair => (pair.Key.Id, Samples: pair.Value, Share: tally.Near.GetValueOrDefault((group.Key, pair.Key.Id)) / (float)anchorCount,
                        ByShare: tally.Near.GetValueOrDefault((pair.Key.Id, group.Key)) / (float)tally.Counts[pair.Key.Id]))
                    .Where(entry => entry.Share >= MinShare && entry.Share / Math.Max(tally.RandomNear.GetValueOrDefault(entry.Id) * randomShare, randomShare) >= MinLift
                                    || entry.ByShare >= MinShare && entry.ByShare / Math.Max(tally.RandomNear.GetValueOrDefault(group.Key) * randomShare, randomShare) >= MinLift)
                    .OrderBy(entry => entry.Id)
                    .Select(entry => new ObjectCompanion { Id = entry.Id, Rate = entry.Samples.Count / (float)anchorCount, Samples = entry.Samples.ToArray() })
                    .ToArray();
                if (kept.Length > 0)
                {
                    companions[(theme, group.Key)] = kept;
                }
            }

            foreach ((var (id, anchor), int near) in tally.Near)
            {
                anchorShares[(theme, id, anchor)] = near / (float)tally.Counts[id];
            }

            foreach ((var id, int count) in tally.Counts.Where(pair => pair.Value >= MinCount))
            {
                var anchors = tally.Near
                    .Where(pair => pair.Key.Id == id)
                    .Select(pair => (pair.Key.Anchor, Share: pair.Value / (float)count,
                        Lift: pair.Value / (float)count / Math.Max(tally.RandomNear.GetValueOrDefault(pair.Key.Anchor) / (float)tally.RandomPoints, 1f / tally.RandomPoints)))
                    .Where(entry => entry is { Share: >= MinShare, Lift: >= MinLift })
                    .OrderByDescending(entry => entry.Share)
                    .ThenBy(entry => entry.Anchor)
                    .Select(entry => new ObjectAnchor { Id = entry.Anchor, Share = entry.Share, Lift = entry.Lift, Samples = tally.Samples[(id, entry.Anchor)].ToArray() })
                    .ToArray();
                
                relations[(theme, id)] = new ObjectRelations
                {
                    Id = id,
                    Theme = theme,
                    Count = count,
                    GroupShare = tally.Grouped.GetValueOrDefault(id) / (float)count,
                    WallShare = tally.Walled.GetValueOrDefault(id) / (float)count,
                    RandomWallShare = tally.RandomWalled / (float)Math.Max(1, tally.RandomPoints),
                    Anchors = anchors
                };
            }

            foreach (var (blocking, distances) in tally.DoorDistances.Where(pair => pair.Value.Count >= MinCount))
            {
                distances.Sort();
                doorClearances[(theme, blocking)] = distances[(int)(LowQuantile * (distances.Count - 1))];
            }
        }

        return new ObjectRelationTable(relations, anchorShares, doorClearances, companions, companionRates, spacings);
    }

    /// <summary>
    /// Returns the relations of an object in the levels of a theme, or null when too few of the object stand in them.
    /// </summary>
    public ObjectRelations? GetRelations(LevelTheme theme, KnownObject id) => _relations.GetValueOrDefault((theme, id));

    /// <summary>
    /// Returns the relations of every object measured in the levels of a theme.
    /// </summary>
    public ObjectRelations[] GetRelations(LevelTheme theme) => _relations.Values.Where(relations => relations.Theme == theme).OrderBy(relations => relations.Id).ToArray();

    /// <summary>
    /// Returns the share of an object's pieces in the levels of a theme that stand within reach of another object, 0 when none does.
    /// </summary>
    public float GetAnchorShare(LevelTheme theme, KnownObject id, KnownObject anchor) => _anchorShares.GetValueOrDefault((theme, id, anchor));

    /// <summary>
    /// Returns the distance from a doorway that all but the closest 5% of the floor pieces of a blocking class keep in the levels of a theme,
    /// or null when too few stand in them.
    /// </summary>
    public float? GetDoorClearance(LevelTheme theme, BlockingClass blocking) =>
        _doorClearances.TryGetValue((theme, blocking), out float distance) ? distance : null;

    /// <summary>
    /// Returns the objects that lie by an anchor object in the levels of a theme far more often than chance would put them there.
    /// </summary>
    public IReadOnlyList<ObjectCompanion> GetCompanions(LevelTheme theme, KnownObject anchor) => _companions.GetValueOrDefault((theme, anchor)) ?? [];

    /// <summary>
    /// Returns how many of an object lie by each anchor object in the levels of a theme, on average, 0 when none does.
    /// </summary>
    public float GetCompanionRate(LevelTheme theme, KnownObject anchor, KnownObject id) => _companionRates.GetValueOrDefault((theme, anchor, id));

    /// <summary>
    /// Returns how close the levels of a theme stand two decor objects that block the player at the same height, as the share of their summed
    /// sizes their centers keep apart along the nearer axis, at most one half.
    /// </summary>
    public float GetSpacing(LevelTheme theme, KnownObject id, KnownObject other, bool raised) =>
        _spacings.GetValueOrDefault((theme, (KnownObject)Math.Min((int)id, (int)other), (KnownObject)Math.Max((int)id, (int)other), raised), TouchingShare);

    /// <summary>
    /// Counts the decor pieces of a set of levels that block the player and stand closer to another at the same height than the levels of this
    /// table ever stand the pair.
    /// </summary>
    public (int Crowded, int Count) CountCrowded(IEnumerable<ReferenceLevel> levels, ObjectTable objects)
    {
        int crowded = 0;
        int count = 0;
        foreach (var level in levels.Where(level => level.Model.Theme != LevelTheme.Outdoor))
        {
            var pieces = level.Map.Sprites.Where(sprite => sprite.Id.IsDecor && objects.GetBlocking(sprite.Id) != BlockingClass.WalkThrough).ToArray();
            foreach (var piece in pieces)
            {
                objects.TryFind(piece.Id, out var obj);
                bool raised = piece.Position.Z >= FloorHeight;
                count++;
                if (pieces.Any(other => !ReferenceEquals(other, piece) && other.Position.Z >= FloorHeight == raised && IsCloser(piece, obj, other, level.Model.Theme, raised, objects)))
                {
                    crowded++;
                }
            }
        }

        return (crowded, count);
    }

    /// <summary>
    /// Returns a test of whether an object at a map point of a level of a theme lies by the anchor object nearest it as one of that
    /// anchor's companions.
    /// </summary>
    public Func<KnownObject, float, float, bool> GetCompanionTest(LevelTheme theme, ReferenceLevel level)
    {
        var anchors = level.Map.Sprites.Where(sprite => IsAnchor(sprite.Id)).ToArray();
        return (id, x, y) =>
        {
            var owner = anchors
                .Select(anchor => (anchor.Id, Distance: MathF.Sqrt((anchor.Position.X - x) * (anchor.Position.X - x) + (anchor.Position.Y - y) * (anchor.Position.Y - y))))
                .Where(anchor => anchor.Distance <= Reach)
                .OrderBy(anchor => anchor.Distance)
                .Select(anchor => (KnownObject?)anchor.Id)
                .FirstOrDefault();
            return owner is not null && GetCompanions(theme, owner.Value).Any(companion => companion.Id == id);
        };
    }

    private bool IsCloser(MergedMapSprite piece, GameObject obj, MergedMapSprite other, LevelTheme theme, bool raised, ObjectTable objects)
    {
        objects.TryFind(other.Id, out var otherObj);
        float spacing = GetSpacing(theme, piece.Id, other.Id, raised);
        return Math.Abs(other.Position.X - piece.Position.X) < spacing * (obj.Size.X + otherObj.Size.X)
               && Math.Abs(other.Position.Y - piece.Position.Y) < spacing * (obj.Size.Y + otherObj.Size.Y);
    }

    private static bool IsAnchor(KnownObject id) => id.Family is not null || id == KnownObject.Spawner;

    private static bool IsCompanion(KnownObject id) => id.Family is null && id is { IsDecor: true, IsBlood: false, IsCorpse: false } && id != KnownObject.FlyingGibs;

    private static bool IsPiece(KnownObject id) =>
        id != KnownObject.Earth && !KnownObject.Floors.Contains(id) && !KnownObject.LabWalls.Contains(id) && !KnownObject.MilitaryWalls.Contains(id);

    // Count, for one theme, how often each object stands by each other object, a wall and a doorway, and how often random floor points do
    private sealed class Tally
    {
        public Dictionary<KnownObject, int> Counts { get; } = [];

        public Dictionary<(KnownObject Id, KnownObject Anchor), int> Near { get; } = [];

        public Dictionary<(KnownObject Id, KnownObject Anchor), List<AnchorSample>> Samples { get; } = [];

        public Dictionary<KnownObject, int> Grouped { get; } = [];

        public Dictionary<KnownObject, int> Walled { get; } = [];

        public Dictionary<BlockingClass, List<float>> DoorDistances { get; } = [];

        public Dictionary<(KnownObject Id, KnownObject Anchor), List<AnchorSample>> Owned { get; } = [];

        public Dictionary<(KnownObject Id, KnownObject Other, bool Raised), float> Spacings { get; } = [];

        public Dictionary<KnownObject, int> RandomNear { get; } = [];

        public int RandomPoints { get; private set; }

        public int RandomWalled { get; private set; }

        public void Add(ReferenceLevel level, ObjectTable objects, Random rng)
        {
            var pieces = level.Map.Sprites.Where(sprite => IsPiece(sprite.Id)).ToArray();
            var buckets = new Dictionary<(int, int), List<int>>();
            for (int index = 0; index < pieces.Length; index++)
            {
                var key = GetBucket(pieces[index].Position.X, pieces[index].Position.Y);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    buckets[key] = bucket;
                }

                bucket.Add(index);
            }

            var lattice = level.Model.Lattice;
            var edges = level.Model.Edges;
            var doorways = level.Survey.GetDoorways().Where(doorway => doorway.Kind.IsPassage).Select(doorway => doorway.Span).ToArray();
            for (int index = 0; index < pieces.Length; index++)
            {
                var piece = pieces[index];
                var position = piece.Position;
                Counts[piece.Id] = Counts.GetValueOrDefault(piece.Id) + 1;
                var nearest = new Dictionary<KnownObject, (float Distance, int Index)>();
                bool grouped = false;
                foreach (int otherIndex in GetNear(buckets, position.X, position.Y))
                {
                    var other = pieces[otherIndex];
                    float distance = MathF.Sqrt((other.Position.X - position.X) * (other.Position.X - position.X) + (other.Position.Y - position.Y) * (other.Position.Y - position.Y));
                    if (otherIndex == index || distance > Reach)
                    {
                        continue;
                    }

                    if (other.Id == piece.Id)
                    {
                        grouped = true;
                    }
                    else if (!nearest.TryGetValue(other.Id, out var best) || distance < best.Distance)
                    {
                        nearest[other.Id] = (distance, otherIndex);
                    }
                }

                if (grouped)
                {
                    Grouped[piece.Id] = Grouped.GetValueOrDefault(piece.Id) + 1;
                }

                AddSpacings(pieces, buckets, index, objects);

                var furniture = nearest.Where(pair => IsAnchor(pair.Key)).ToArray();
                if (piece.Id.Family is null && furniture.Length > 0)
                {
                    var owner = pieces[furniture.MinBy(pair => pair.Value.Distance).Value.Index];
                    if (!Owned.TryGetValue((piece.Id, owner.Id), out var owned))
                    {
                        owned = [];
                        Owned[(piece.Id, owner.Id)] = owned;
                    }

                    owned.Add(new AnchorSample(owner.Facing, position.X - owner.Position.X, position.Y - owner.Position.Y, position.Z - owner.Position.Z, piece.Facing));
                }

                foreach ((var anchorId, (_, int anchorIndex)) in nearest)
                {
                    var anchor = pieces[anchorIndex];
                    Near[(piece.Id, anchorId)] = Near.GetValueOrDefault((piece.Id, anchorId)) + 1;
                    if (!Samples.TryGetValue((piece.Id, anchorId), out var samples))
                    {
                        samples = [];
                        Samples[(piece.Id, anchorId)] = samples;
                    }

                    samples.Add(new AnchorSample(anchor.Facing, position.X - anchor.Position.X, position.Y - anchor.Position.Y, position.Z - anchor.Position.Z, piece.Facing));
                }

                (float u, float v) = lattice.ToLattice(position.X, position.Y);
                if (edges.FindNearestWall(u, v)?.Distance <= WallReach)
                {
                    Walled[piece.Id] = Walled.GetValueOrDefault(piece.Id) + 1;
                }

                var blocking = objects.GetBlocking(piece.Id);
                if (position.Z < FloorHeight && blocking != BlockingClass.WalkThrough && doorways.Length > 0)
                {
                    if (!DoorDistances.TryGetValue(blocking, out var distances))
                    {
                        distances = [];
                        DoorDistances[blocking] = distances;
                    }

                    distances.Add(doorways.Min(doorway => doorway.MeasureDistance(new PointF(position.X, position.Y))));
                }
            }

            foreach (var cell in level.Model.Cells.OrderBy(cell => cell.U).ThenBy(cell => cell.V))
            {
                for (int point = 0; point < PointsPerCell; point++)
                {
                    float u = cell.U + rng.NextSingle();
                    float v = cell.V + rng.NextSingle();
                    var at = lattice.ToWorld(u, v);
                    RandomPoints++;
                    if (edges.FindNearestWall(u, v)?.Distance <= WallReach)
                    {
                        RandomWalled++;
                    }

                    var seen = new HashSet<KnownObject>();
                    foreach (int otherIndex in GetNear(buckets, at.X, at.Y))
                    {
                        var other = pieces[otherIndex];
                        if ((other.Position.X - at.X) * (other.Position.X - at.X) + (other.Position.Y - at.Y) * (other.Position.Y - at.Y) <= Reach * Reach && seen.Add(other.Id))
                        {
                            RandomNear[other.Id] = RandomNear.GetValueOrDefault(other.Id) + 1;
                        }
                    }
                }
            }
        }

        // Keep, per pair of blocking objects at the same height, the smallest share of their summed sizes their centers stood apart
        private void AddSpacings(MergedMapSprite[] pieces, Dictionary<(int, int), List<int>> buckets, int index, ObjectTable objects)
        {
            var piece = pieces[index];
            if (!piece.Id.IsDecor || objects.GetBlocking(piece.Id) == BlockingClass.WalkThrough || !objects.TryFind(piece.Id, out var obj))
            {
                return;
            }

            bool raised = piece.Position.Z >= FloorHeight;
            foreach (int otherIndex in GetNear(buckets, piece.Position.X, piece.Position.Y))
            {
                var other = pieces[otherIndex];
                if (otherIndex <= index || !other.Id.IsDecor || other.Position.Z >= FloorHeight != raised || objects.GetBlocking(other.Id) == BlockingClass.WalkThrough
                    || !objects.TryFind(other.Id, out var otherObj))
                {
                    continue;
                }

                float spacing = Math.Max(Math.Abs(other.Position.X - piece.Position.X) / Math.Max(1, obj.Size.X + otherObj.Size.X),
                    Math.Abs(other.Position.Y - piece.Position.Y) / Math.Max(1, obj.Size.Y + otherObj.Size.Y));
                var key = ((KnownObject)Math.Min((int)piece.Id, (int)other.Id), (KnownObject)Math.Max((int)piece.Id, (int)other.Id), raised);
                if (spacing < Spacings.GetValueOrDefault(key, TouchingShare))
                {
                    Spacings[key] = spacing;
                }
            }
        }

        private static (int, int) GetBucket(float x, float y) => ((int)MathF.Floor(x / BucketSize), (int)MathF.Floor(y / BucketSize));

        private static IEnumerable<int> GetNear(Dictionary<(int, int), List<int>> buckets, float x, float y)
        {
            (int bx, int by) = GetBucket(x, y);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (buckets.TryGetValue((bx + dx, by + dy), out var bucket))
                    {
                        foreach (int index in bucket)
                        {
                            yield return index;
                        }
                    }
                }
            }
        }
    }
}
