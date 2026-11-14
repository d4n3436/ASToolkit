using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Engine;

// Collision boxes do not use visual scale.
internal sealed class MovementMap
{
    public const int CellSize = 8;
    private const int BucketSize = 128;
    private const float CellScale = 0.125f;
    private const float PlayerFootprintInset = 3;
    private const float CornerInset = 2;
    private const float DefaultMapSize = 8192;
    private const float SweepStep = 2;
    private const int BucketMargin = 2;
    private readonly Dictionary<Point, List<Obstacle>> _objects = [];
    private static readonly List<Obstacle> NoObstacles = [];

    private readonly List<Obstacle>[] _bucketGrid;
    private readonly int _bucketColumns;
    private readonly int _bucketRows;
    private readonly Dictionary<MergedMapSprite, List<RaisedCell>> _raised;
    private readonly Dictionary<MergedMapSprite, List<Obstacle>> _placedObstacles = new(ReferenceEqualityComparer.Instance);
    private readonly SizeF _mapSize;
    private readonly int _columns;
    private readonly int _rows;
    private readonly short[] _sceneryHeights;
    private short[] _objectHeights;
    private bool _sharesObjectHeights;

    [EngineRoutine(0x00410d70)]
    public MovementMap(SizeF mapSize = default)
    {
        _mapSize = mapSize.IsEmpty ? new SizeF(DefaultMapSize, DefaultMapSize) : mapSize;
        _columns = (int)_mapSize.Width / CellSize;
        _rows = (int)_mapSize.Height / CellSize;
        int cells = _columns * _rows;
        _sceneryHeights = new short[cells];
        _objectHeights = new short[cells];
        _raised = new Dictionary<MergedMapSprite, List<RaisedCell>>(ReferenceEqualityComparer.Instance);
        _bucketColumns = GetBucket(_mapSize.Width) + 1 + BucketMargin * 2;
        _bucketRows = GetBucket(_mapSize.Height) + 1 + BucketMargin * 2;
        _bucketGrid = new List<Obstacle>[_bucketColumns * _bucketRows];
        for (int bucket = 0; bucket < _bucketGrid.Length; bucket++)
        {
            _bucketGrid[bucket] = [];
        }
    }

    private MovementMap(MovementMap source)
    {
        _mapSize = source._mapSize;
        _columns = source._columns;
        _rows = source._rows;
        _raised = new Dictionary<MergedMapSprite, List<RaisedCell>>(source._raised, ReferenceEqualityComparer.Instance);
        _sceneryHeights = source._sceneryHeights;
        _objectHeights = source._objectHeights;
        _sharesObjectHeights = true;
        source._sharesObjectHeights = true;
        DoorsLocked = source.DoorsLocked;
        FencesUp = source.FencesUp;
        Version = source.Version;
        _bucketColumns = source._bucketColumns;
        _bucketRows = source._bucketRows;
        _bucketGrid = new List<Obstacle>[source._bucketGrid.Length];
        var copies = new Dictionary<Obstacle, Obstacle>();
        foreach (var (cell, obstacles) in source._objects)
        {
            _objects.Add(cell, CopyBucket(obstacles, copies));
        }

        for (int bucket = 0; bucket < _bucketGrid.Length; bucket++)
        {
            _bucketGrid[bucket] = CopyBucket(source._bucketGrid[bucket], copies);
        }

        foreach (var (sprite, obstacles) in source._placedObstacles)
        {
            _placedObstacles.Add(sprite, obstacles.Select(obstacle => copies[obstacle]).ToList());
        }
    }

    public MovementMap Clone() => new(this);

    public MovementMap(LevelModel model, SpritePartReader parts) : this(model.MapSize)
    {
        foreach (var sprite in model.Sprites)
        {
            Add(sprite, parts, SpriteProgram.TryParse(sprite.Payload, false, out var program) && HasDeathAction(program.GetStack(0)));
        }
    }

    public bool Add(MergedMapSprite sprite, SpritePartReader parts, bool removable)
    {
        var source = removable ? sprite : null;
        bool changed = false;
        foreach (var part in parts.GetParts(sprite))
        {
            var obj = part.Object;

            if (obj.MaxHp > 0)
            {
                source = sprite;
            }

            bool scenery = source is null && obj.SpriteClass == SpriteClass.Scenery;

            if (part.Id != KnownObject.Crate &&
                (obj.SpriteType.HasFlag(SpriteType.Unit) || obj.SpriteType.HasFlag(SpriteType.Avia) ||
                 obj.SpriteType.HasFlag(SpriteType.Cannon) || obj.SpriteClass == SpriteClass.Player))
                continue;

            if (part.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor or KnownObject.Fence)
            {
                AddObstacle(obj, part.Position, sprite, true, part.Id == KnownObject.Fence);
                changed = true;
                continue;
            }

            if (obj.IsSolid && part.File is not null)
            {
                var file = part.File.Value;
                foreach (var point in parts.GetHeightPoints(part))
                {
                    // Skip depth samples projected past the frame's last complete cell
                    if (point.X + file.Width / 2 >= file.Width / CellSize * CellSize ||
                        point.Y + file.Height / 2 >= file.Height / CellSize * CellSize)
                        continue;
                    Raise(part.Position.X + point.X, part.Position.Y + point.Y, part.Position.Z + point.Z, scenery, source);
                    changed = true;
                }
            }

            if (obj.Flags.HasFlag(ObjectFlags.Outline))
            {
                for (int y = 0; y <= (int)MathF.Ceiling(obj.Size.Y / CellSize); y++)
                for (int x = 0; x <= (int)MathF.Ceiling(obj.Size.X / CellSize); x++)
                {
                    Raise(part.Position.X + Math.Min(x * CellSize, obj.Size.X) - obj.Size.X / 2,
                        part.Position.Y + Math.Min(y * CellSize, obj.Size.Y) - obj.Size.Y / 2, part.Position.Z + obj.Size.Z, scenery, source);
                }
                changed = true;
            }

            if (obj.IsHittable && obj.MoveMask != 0)
            {
                AddObstacle(obj, part.Position, sprite);
                changed = true;
            }
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }

    private static bool HasDeathAction(ReadOnlySpan<StackAction> actions)
    {
        foreach (var action in actions)
        {
            if ((int)action.Code is (int)SpriteAnimation.Death or (int)SpriteAnimation.Death2)
            {
                return true;
            }
        }

        return false;
    }

    public bool DoorsLocked { get; private set; }

    public bool FencesUp { get; private set; }

    public void SetGates(bool doorsLocked, bool fencesUp)
    {
        if (DoorsLocked == doorsLocked && FencesUp == fencesUp)
            return;

        DoorsLocked = doorsLocked;
        FencesUp = fencesUp;
        Version++;
    }

    public void CollectGates(bool includeDoors, bool includeFences, List<GameWorld.TerrainChange> gates)
    {
        foreach (var obstacles in _placedObstacles.Values)
        {
            foreach (var obstacle in obstacles)
            {
                if (obstacle.IsGate && (obstacle.IsFence ? includeFences : includeDoors))
                {
                    gates.Add(new GameWorld.TerrainChange(obstacle.Position.AsVector2(), obstacle.Half));
                }
            }
        }
    }

    private short[] GetWritableObjectHeights()
    {
        if (_sharesObjectHeights)
        {
            _objectHeights = (short[])_objectHeights.Clone();
            _sharesObjectHeights = false;
        }

        return _objectHeights;
    }

    [EngineRoutine(0x00411300)]
    [EngineRoutine(0x00411260)]
    public void Raise(float x, float y, float height, bool isScenery = false, MergedMapSprite? source = null)
    {
        if (x < 0 || x >= _mapSize.Width || y < 0 || y >= _mapSize.Height)
            return;

        int index = (int)x / CellSize + (int)(y * CellScale) * _columns;
        short value = (short)(int)height;
        short[] grid = isScenery ? _sceneryHeights : _objectHeights;

        if (grid[index] < value)
        {
            (isScenery ? grid : GetWritableObjectHeights())[index] = value;
        }

        if (source is null)
            return;

        if (!_raised.TryGetValue(source, out var cells))
        {
            _raised[source] = cells = [];
        }
        cells.Add(new RaisedCell(index, value));
    }


    public void AddObstacle(GameObject obj, Vector3 position, MergedMapSprite? source = null, bool isGate = false, bool isFence = false)
    {
        var obstacle = new Obstacle(obj, position, isGate, isFence);

        if (source is not null)
        {
            if (!_placedObstacles.TryGetValue(source, out var obstacles))
            {
                _placedObstacles[source] = obstacles = [];
            }
            obstacles.Add(obstacle);
        }

        var half = obj.Size.AsVector2() / 2;
        for (int y = GetBucket(position.Y - half.Y); y <= GetBucket(position.Y + half.Y); y++)
        for (int x = GetBucket(position.X - half.X); x <= GetBucket(position.X + half.X); x++)
        {
            GetOrAddBucket(x, y).Add(obstacle);
        }
    }

    public void SetDoorHeight(MergedMapSprite door, float height)
    {
        if (!_placedObstacles.TryGetValue(door, out var obstacles))
        {
            return;
        }

        foreach (var obstacle in obstacles)
        {
            if (obstacle is { IsGate: true, IsFence: false })
            {
                obstacle.Position = obstacle.Position with { Z = height };
            }
        }

        Version++;
    }

    public int Version { get; private set; }

    public SizeF MapSize => _mapSize;

    public bool Remove(MergedMapSprite source)
    {
        bool changed = false;

        if (_placedObstacles.TryGetValue(source, out var obstacles))
        {
            foreach (var obstacle in obstacles)
            {
                changed |= obstacle.Active;
                obstacle.Active = false;
            }
        }

        if (_raised.TryGetValue(source, out var cells))
        {
            foreach (var cell in cells)
            {
                if (_objectHeights[cell.Index] == cell.Height)
                {
                    GetWritableObjectHeights()[cell.Index] = 0;
                }
            }

            changed = true;
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }

    [EngineRoutine(0x00410e60)]
    private float GetCellHeight(float x, float y)
    {
        x = ClampToMap(x, _mapSize.Width);
        y = ClampToMap(y, _mapSize.Height);

        return GetCell((int)(y * CellScale), (int)(x * CellScale));
    }

    private short GetCell(int row, int column)
    {
        int index = Math.Min(row, _rows - 1) * _columns + Math.Min(column, _columns - 1);

        return Math.Max(_sceneryHeights[index], _objectHeights[index]);
    }

    public float GetHeight(Vector2 point) => GetCellHeight(point.X, point.Y);

    [EngineRoutine(0x00411050)]
    public float GetSupportHeight(in GameObject walker, Vector2 point)
    {
        if (walker.SpriteClass != SpriteClass.Player)
        {
            return GetHeight(point);
        }

        var inset = walker.Size.AsVector2() * 0.5f - new Vector2(PlayerFootprintInset);
        float left = ClampToMap(point.X - inset.X, _mapSize.Width) * CellScale;
        float right = ClampToMap(point.X + inset.X, _mapSize.Width) * CellScale;
        float top = ClampToMap(point.Y - inset.Y, _mapSize.Height) * CellScale;
        float bottom = ClampToMap(point.Y + inset.Y, _mapSize.Height) * CellScale;
        short ground = 0;
        for (float row = top; row <= bottom; row += 1)
        for (float column = left; column <= right; column += 1)
        {
            ground = Math.Max(ground, GetCell((int)row, (int)column));
        }

        return ground;
    }

    private static float ClampToMap(float value, float size) => value < 0 ? 0 : value >= size ? size - 1 : value;

    public bool CanStand(in GameObject walker, Vector3 point, bool anticipateDoors = false) =>
        CanStandOnTerrain(walker, point) &&
        FindObstacle(walker, point, anticipateDoors) is null;

    private bool CanStandOnTerrain(in GameObject walker, Vector3 point) => CanStandOnTerrain(walker, point, out _);

    private bool CanStandOnTerrain(in GameObject walker, Vector3 point, out float ground)
    {
        ground = float.NaN;

        if (walker.MoveMask == 0)
        {
            return true;
        }

        var half = walker.Size.AsVector2() / 2;
        ground = GetSupportHeight(walker, point.AsVector2());

        if (!AllowsHeight(walker, ground, point.Z))
        {
            return false;
        }

        if (walker.SpriteClass != SpriteClass.Player)
        {
            var inset = half - new Vector2(CornerInset);
            ReadOnlySpan<Vector2> corners = [new(-inset.X, -inset.Y), inset with { Y = -inset.Y }, inset with { X = -inset.X }, inset];
            foreach (var corner in corners)
            {
                if (!AllowsHeight(walker, GetHeight(point.AsVector2() + corner), point.Z))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool CanEscapeOverlap(in GameObject walker, Vector3 point, Vector3 start)
    {
        if (!CanStandOnTerrain(walker, point))
        {
            return false;
        }

        var obstacle = FindObstacle(walker, point);

        return obstacle is not null && GetCollisionVolume(walker, start).Overlaps(obstacle.GetCollisionVolume(false));
    }

    public bool BlocksInstantProjectile(in GameObject projectile, Vector3 point) =>
        point.Z < GetSupportHeight(projectile, point.AsVector2()) ||
        FindObstacle(projectile, point) is not null;

    public bool CanSpawn(in GameObject obj, Vector3 point) => point.Z >= GetSupportHeight(obj, point.AsVector2()) &&
        FindObstacle(obj, point) is null;

    private Obstacle? FindObstacle(in GameObject walker, Vector3 point, bool anticipateDoors = false)
    {
        var half = walker.Size.AsVector2() / 2;
        for (int y = GetBucket(point.Y - half.Y); y <= GetBucket(point.Y + half.Y); y++)
        for (int x = GetBucket(point.X - half.X); x <= GetBucket(point.X + half.X); x++)
        {
            foreach (var obstacle in FindBucket(x, y))
            {
                if (obstacle.Active && (!obstacle.IsGate || (obstacle.IsFence ? FencesUp : !anticipateDoors || DoorsLocked)) &&
                    GetCollisionVolume(walker, point).Overlaps(obstacle.GetCollisionVolume(anticipateDoors && obstacle is { IsGate: true, IsFence: false })))
                {
                    return obstacle;
                }
            }
        }

        return null;
    }

    public bool CanMove(in GameObject walker, Vector3 from, Vector2 to, bool anticipateDoors = false)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from.AsVector2(), to) / SweepStep));
        var point = from;
        var start = from.AsVector2();
        var delta = to - start;
        for (int step = 1; step <= steps; step++)
        {
            point = new Vector3(start + delta * (step / (float)steps), point.Z);

            if (!CanStandOnTerrain(walker, point, out float ground) || FindObstacle(walker, point, anticipateDoors) is not null)
            {
                return false;
            }

            if (walker.Flags.HasFlag(ObjectFlags.FollowsTerrain))
            {
                point.Z = float.IsNaN(ground) ? GetSupportHeight(walker, point.AsVector2()) : ground;
            }
        }

        return true;
    }

    private static bool AllowsHeight(in GameObject walker, float ground, float height) => walker.Flags.HasFlag(ObjectFlags.FollowsTerrain)
        ? ground - height <= walker.StepHeight && height - ground <= walker.DropHeight
        : ground <= height;

    private static CollisionVolume GetCollisionVolume(in GameObject walker, Vector3 point) => new(point, walker.Size.AsVector2() / 2, walker.Size.Z, walker.MoveMask);

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

        if ((uint)column < (uint)_bucketColumns && (uint)row < (uint)_bucketRows)
            return _bucketGrid[row * _bucketColumns + column];

        return _objects.GetValueOrDefault(new Point(x, y), NoObstacles);
    }

    private List<Obstacle> GetOrAddBucket(int x, int y)
    {
        int column = x + BucketMargin;
        int row = y + BucketMargin;

        if ((uint)column < (uint)_bucketColumns && (uint)row < (uint)_bucketRows)
            return _bucketGrid[row * _bucketColumns + column];

        if (!_objects.TryGetValue(new Point(x, y), out var list))
        {
            _objects[new Point(x, y)] = list = [];
        }

        return list;
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

    private readonly record struct RaisedCell(int Index, short Height);
}
