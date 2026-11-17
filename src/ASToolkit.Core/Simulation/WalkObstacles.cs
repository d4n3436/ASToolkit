using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

internal sealed class WalkObstacles
{
    private const int BucketSize = 128;
    private const int BucketMargin = 2;
    private const float SweepStep = 2;
    private const float MinMove = 1e-6f;
    private static readonly List<Obstacle> NoObstacles = [];
    private readonly Dictionary<Point, List<Obstacle>> _outside = [];
    private readonly List<Obstacle>[] _buckets;
    private readonly int _bucketColumns;
    private readonly int _bucketRows;
    private readonly Dictionary<MergedMapSprite, Placed> _placed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MergedMapSprite> _removed = new(ReferenceEqualityComparer.Instance);
    private readonly SpritePartReader? _parts;
    private readonly ObjectTable? _objects;
    private List<Placed>? _tracked;
    private int _removedCount;
    private int _spriteCount;

    public WalkObstacles(LevelModel model, SpritePartReader parts, ObjectTable objects, MovementMap terrain) : this(terrain)
    {
        _parts = parts;
        _objects = objects;
        foreach (var sprite in model.Sprites)
        {
            Add(sprite);
        }

        _spriteCount = model.Sprites.Count;
    }

    public WalkObstacles(MovementMap terrain)
    {
        Terrain = terrain;
        _bucketColumns = GetBucket(terrain.MapSize.Width) + 1 + BucketMargin * 2;
        _bucketRows = GetBucket(terrain.MapSize.Height) + 1 + BucketMargin * 2;
        _buckets = new List<Obstacle>[_bucketColumns * _bucketRows];
        for (int bucket = 0; bucket < _buckets.Length; bucket++)
        {
            _buckets[bucket] = [];
        }
    }

    private WalkObstacles(WalkObstacles source, MovementMap terrain)
    {
        _parts = source._parts;
        _objects = source._objects;
        Terrain = terrain;
        _bucketColumns = source._bucketColumns;
        _bucketRows = source._bucketRows;
        _spriteCount = source._spriteCount;
        DoorsLocked = source.DoorsLocked;
        FencesUp = source.FencesUp;
        Version = source.Version;
        var copies = new Dictionary<Obstacle, Obstacle>();
        _buckets = source._buckets.Select(bucket => CopyBucket(bucket, copies)).ToArray();
        foreach (var (cell, obstacles) in source._outside)
        {
            _outside.Add(cell, CopyBucket(obstacles, copies));
        }

        foreach (var (sprite, placed) in source._placed)
        {
            _placed.Add(sprite, new Placed(sprite, placed.Obstacles.Select(obstacle => copies[obstacle]).ToArray()));
        }
    }

    public WalkObstacles Clone(MovementMap terrain) => new(this, terrain);

    public SizeF MapSize => Terrain.MapSize;

    public MovementMap Terrain { get; }

    public bool DoorsLocked { get; private set; }

    public bool FencesUp { get; private set; }

    public int Version { get; private set; }

    public void SetGates(bool doorsLocked, bool fencesUp)
    {
        if (DoorsLocked == doorsLocked && FencesUp == fencesUp)
            return;

        DoorsLocked = doorsLocked;
        FencesUp = fencesUp;
        Version++;
    }

    public void CollectGates(bool includeDoors, bool includeFences, List<TerrainChange> gates)
    {
        foreach (var placed in _placed.Values)
        {
            foreach (var obstacle in placed.Obstacles)
            {
                if (obstacle.IsGate && (obstacle.IsFence ? includeFences : includeDoors))
                {
                    gates.Add(new TerrainChange(obstacle.Position.AsVector2(), obstacle.Half));
                }
            }
        }
    }

    public bool CrossesClosedGate(Vector2 from, Vector2 to)
    {
        if (!DoorsLocked && !FencesUp)
            return false;

        foreach (var placed in _placed.Values)
        {
            foreach (var obstacle in placed.Obstacles)
            {
                if (obstacle is { Active: true, IsGate: true } && (obstacle.IsFence ? FencesUp : DoorsLocked) && Crosses(from, to, obstacle.Position.AsVector2(), obstacle.Half))
                    return true;
            }
        }

        return false;
    }

    private static bool Crosses(Vector2 from, Vector2 to, Vector2 center, Vector2 half)
    {
        var direction = to - from;
        float enter = 0;
        float leave = 1;
        for (int axis = 0; axis < 2; axis++)
        {
            float start = axis == 0 ? from.X - center.X : from.Y - center.Y;
            float step = axis == 0 ? direction.X : direction.Y;
            float extent = axis == 0 ? half.X : half.Y;

            if (MathF.Abs(step) < float.Epsilon)
            {
                if (MathF.Abs(start) > extent)
                    return false;

                continue;
            }

            float first = (-extent - start) / step;
            float second = (extent - start) / step;
            enter = Math.Max(enter, Math.Min(first, second));
            leave = Math.Min(leave, Math.Max(first, second));

            if (enter > leave)
                return false;
        }

        return true;
    }

    // Follows pushed bodies and sinking doors and collects the areas of removed and created sprites
    public void Synchronize(GameWorld world, List<TerrainChange> changes)
    {
        if (_tracked is null)
        {
            _removedCount = world.RemovedCount;
            foreach (var sprite in world.Sprites)
            {
                if (world.IsRemoved(sprite))
                {
                    _removed.Add(sprite);
                }
            }

            _tracked = [];
            foreach (var placed in _placed.Values)
            {
                placed.Unit = world.FindPlacementUnit(placed.Sprite);

                if (placed.Unit is not null && (placed.Unit.Definition.CanBePushed || Array.Exists(placed.Obstacles, obstacle => obstacle is { IsGate: true, IsFence: false })))
                {
                    placed.Position = placed.Unit.Position;
                    _tracked.Add(placed);
                }
            }
        }

        foreach (var placed in _tracked)
        {
            var position = placed.Unit!.Position;

            if (_removed.Contains(placed.Sprite) || Vector3.DistanceSquared(position, placed.Position) <= MinMove)
                continue;

            Move(placed, position - placed.Position);
            placed.Position = position;
        }

        if (world.RemovedCount != _removedCount)
        {
            _removedCount = world.RemovedCount;
            foreach (var sprite in world.Sprites)
            {
                if (!world.IsRemoved(sprite) || !_removed.Add(sprite))
                    continue;

                bool blocked = Remove(sprite);
                bool raised = Terrain.Raises(sprite);

                if (raised)
                {
                    Version++;
                }

                if (blocked || raised || sprite.Id is KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack or KnownObject.BreakableWall)
                {
                    changes.Add(GetArea(sprite, _placed.TryGetValue(sprite, out var placed) ? placed.Position : sprite.Position));
                }
            }
        }

        for (; _spriteCount < world.Sprites.Count; _spriteCount++)
        {
            var sprite = world.Sprites[_spriteCount];

            bool blocks = Add(sprite);
            bool raises = Terrain.Raises(sprite);

            if (raises)
            {
                Version++;
            }

            if (blocks || raises)
            {
                changes.Add(GetArea(sprite, sprite.Position));
            }
        }
    }

    private TerrainChange GetArea(MergedMapSprite sprite, Vector3 position) =>
        new(position.AsVector2(), _objects is not null && _objects.TryFind(sprite.Id, out var obj) ? obj.Size.AsVector2() / 2 : Vector2.Zero);

    public bool Remove(MergedMapSprite sprite)
    {
        if (!_placed.TryGetValue(sprite, out var placed))
            return false;

        bool changed = false;
        foreach (var obstacle in placed.Obstacles)
        {
            changed |= obstacle.Active;
            obstacle.Active = false;
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }

    private void Move(Placed placed, Vector3 offset)
    {
        bool door = Array.Exists(placed.Obstacles, obstacle => obstacle is { IsGate: true, IsFence: false });
        foreach (var obstacle in placed.Obstacles)
        {
            if (door && obstacle is not { IsGate: true, IsFence: false })
                continue;

            Place(obstacle, false);
            obstacle.Position += offset;
            Place(obstacle, true);
        }

        Version++;
    }

    public void AddObstacle(in GameObject obj, Vector3 position, MergedMapSprite? source = null)
    {
        var obstacle = new Obstacle(obj, position, false, false);
        Place(obstacle, true);

        if (source is not null)
        {
            _placed[source] = new Placed(source, [obstacle]);
        }

        Version++;
    }

    private bool Add(MergedMapSprite sprite)
    {
        if (_parts is null)
            return false;

        List<Obstacle>? obstacles = null;
        foreach (var part in _parts.GetParts(sprite))
        {
            var obj = part.Object;

            if (part.Id != KnownObject.Crate &&
                (obj.SpriteType.HasFlag(SpriteType.Unit) || obj.SpriteType.HasFlag(SpriteType.Avia) ||
                 obj.SpriteType.HasFlag(SpriteType.Cannon) || obj.SpriteClass == SpriteClass.Man))
                continue;

            bool gate = part.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor or KnownObject.Fence;

            if (!gate && !(obj.IsHittable && obj.MoveMask != 0))
                continue;

            var obstacle = new Obstacle(obj, part.Position, gate, part.Id == KnownObject.Fence);
            (obstacles ??= []).Add(obstacle);
            Place(obstacle, true);
        }

        if (obstacles is null)
            return false;

        _placed[sprite] = new Placed(sprite, obstacles.ToArray());
        Version++;

        return true;
    }

    public bool CanStand(in GameObject walker, Vector3 point, bool anticipateDoors = false) =>
        Terrain.CanStandOnGround(walker, point) && FindObstacle(walker, point, anticipateDoors) is null;

    public bool CanMove(in GameObject walker, Vector3 from, Vector2 to, bool anticipateDoors = false)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from.AsVector2(), to) / SweepStep));
        var point = from;
        var start = from.AsVector2();
        var delta = to - start;
        for (int step = 1; step <= steps; step++)
        {
            point = new Vector3(start + delta * (step / (float)steps), point.Z);

            if (!Terrain.CanStandOnGround(walker, point, out float ground) || FindObstacle(walker, point, anticipateDoors) is not null)
                return false;

            if (walker.Properties.HasFlag(ObjectProperties.ZeroZ))
            {
                point.Z = float.IsNaN(ground) ? Terrain.GetSupportHeight(walker, point.AsVector2()) : ground;
            }
        }

        return true;
    }

    private Obstacle? FindObstacle(in GameObject walker, Vector3 point, bool anticipateDoors)
    {
        var half = walker.Size.AsVector2() / 2;
        var volume = new CollisionVolume(point, half, walker.Size.Z, walker.MoveMask);
        for (int y = GetBucket(point.Y - half.Y); y <= GetBucket(point.Y + half.Y); y++)
        for (int x = GetBucket(point.X - half.X); x <= GetBucket(point.X + half.X); x++)
        {
            foreach (var obstacle in FindBucket(x, y))
            {
                if (obstacle.Active && (!obstacle.IsGate || (obstacle.IsFence ? FencesUp : !anticipateDoors || DoorsLocked)) &&
                    volume.Overlaps(obstacle.GetCollisionVolume(anticipateDoors && obstacle is { IsGate: true, IsFence: false })))
                {
                    return obstacle;
                }
            }
        }

        return null;
    }

    private void Place(Obstacle obstacle, bool adds)
    {
        var position = obstacle.Position;
        var half = obstacle.Half;
        for (int y = GetBucket(position.Y - half.Y); y <= GetBucket(position.Y + half.Y); y++)
        for (int x = GetBucket(position.X - half.X); x <= GetBucket(position.X + half.X); x++)
        {
            var bucket = GetOrAddBucket(x, y);

            if (adds)
            {
                bucket.Add(obstacle);
            }
            else
            {
                bucket.Remove(obstacle);
            }
        }
    }

    private static int GetBucket(float position) => (int)MathF.Floor(position / BucketSize);

    private static List<Obstacle> CopyBucket(List<Obstacle> obstacles, Dictionary<Obstacle, Obstacle> copies)
    {
        var bucket = new List<Obstacle>(obstacles.Count);
        foreach (var obstacle in obstacles)
        {
            if (!copies.TryGetValue(obstacle, out var copy))
            {
                copy = obstacle.Copy();
                copies.Add(obstacle, copy);
            }

            bucket.Add(copy);
        }

        return bucket;
    }

    private List<Obstacle> FindBucket(int x, int y)
    {
        int column = x + BucketMargin;
        int row = y + BucketMargin;

        if (column >= 0 && column < _bucketColumns && row >= 0 && row < _bucketRows)
            return _buckets[row * _bucketColumns + column];

        return _outside.GetValueOrDefault(new Point(x, y), NoObstacles);
    }

    private List<Obstacle> GetOrAddBucket(int x, int y)
    {
        int column = x + BucketMargin;
        int row = y + BucketMargin;

        if (column >= 0 && column < _bucketColumns && row >= 0 && row < _bucketRows)
            return _buckets[row * _bucketColumns + column];

        ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(_outside, new Point(x, y), out _);

        return list ??= [];
    }

    private sealed class Placed
    {
        public Placed(MergedMapSprite sprite, Obstacle[] obstacles)
        {
            Sprite = sprite;
            Obstacles = obstacles;
            Position = sprite.Position;
        }

        public MergedMapSprite Sprite { get; }

        public Obstacle[] Obstacles { get; }

        public Vector3 Position { get; set; }

        public Unit? Unit { get; set; }
    }

    private sealed class Obstacle
    {
        public Obstacle(in GameObject obj, Vector3 position, bool isGate, bool isFence)
        {
            MoveMask = obj.MoveMask;
            Half = obj.Size.AsVector2() / 2;
            Height = obj.Size.Z;
            Position = position;
            ClosedHeight = position.Z;
            IsGate = isGate;
            IsFence = isFence;
        }

        public Obstacle Copy() => (Obstacle)MemberwiseClone();

        public CollisionVolume GetCollisionVolume(bool closedDoor) => new(Position with { Z = closedDoor ? ClosedHeight : Position.Z }, Half, Height, MoveMask);

        public CollisionLayers MoveMask { get; }

        public Vector2 Half { get; }

        public float Height { get; }

        public Vector3 Position { get; set; }

        public float ClosedHeight { get; }

        public bool IsGate { get; }

        public bool IsFence { get; }

        public bool Active { get; set; } = true;
    }
}

internal readonly record struct TerrainChange(Vector2 Position, Vector2 HalfSize);
