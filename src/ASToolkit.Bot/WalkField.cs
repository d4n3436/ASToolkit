using System.Buffers;
using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Routes;

namespace ASToolkit.Bot;

internal sealed class WalkField
{
    private const float StartReach = 60;
    private const float StartTolerance = 0.25f;
    private const int Margin = 2;
    private const int Sides = 8;
    private const float RecoveryReach = 512;
    private const float SweepStep = 2;
    private const float MinMove = 1e-6f;

    private static readonly Cell[] SideSteps = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];
    private readonly Lattice _lattice;
    private readonly int _minU;
    private readonly int _minV;
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cells;
    private readonly Cell[] _places;
    private readonly Vector2[] _centers;
    private readonly GameObject _walker;
    private readonly MovementMap _terrain;
    private readonly SpriteCollector _sprites;
    private readonly int[] _neighbors;
    private readonly bool[] _computed;
    private readonly int[] _distance;
    private readonly int[] _queue;
    private readonly List<Sprite> _gates = [];
    private readonly HashSet<MergedMapSprite> _removed = new(ReferenceEqualityComparer.Instance);
    private Dictionary<MergedMapSprite, Tracked>? _tracked;
    private int _removedCount;
    private int _spriteCount;
    private bool _moved;
    private bool _doorsLocked;
    private Vector3 _lastStartPoint;
    private int _lastStart;
    private int _lastStartVersion = -1;
    private int _floodSource = -1;
    private int _floodSteps = -1;
    private int _floodLimit = -1;
    private int _floodCount;

    public WalkField(RouteGrid grid, bool doorsLocked, MovementMap terrain, SpriteCollector sprites, GameObject walker, int placedCount = 0)
    {
        _terrain = terrain;
        _sprites = sprites;
        _walker = walker;
        _spriteCount = placedCount;
        _lattice = grid.Lattice.Halve();
        var cells = GetCellsInMap(grid, _lattice, terrain.MapSize);
        int minU = int.MaxValue;
        int minV = int.MaxValue;
        int maxU = int.MinValue;
        int maxV = int.MinValue;
        foreach (var cell in cells)
        {
            minU = Math.Min(minU, cell.U);
            minV = Math.Min(minV, cell.V);
            maxU = Math.Max(maxU, cell.U);
            maxV = Math.Max(maxV, cell.V);
        }

        _minU = minU - Margin;
        _minV = minV - Margin;
        _width = maxU + Margin - _minU + 1;
        _height = maxV + Margin - _minV + 1;
        _cells = new int[_width * _height];
        Array.Fill(_cells, -1);
        _places = new Cell[cells.Count];
        _centers = new Vector2[cells.Count];
        int i = 0;
        foreach (var cell in cells)
        {
            _cells[(cell.V - _minV) * _width + (cell.U - _minU)] = i;
            _places[i] = cell;
            _centers[i++] = _lattice.GetCenter(cell).ToVector2();
        }

        _neighbors = new int[cells.Count * Sides];
        _computed = new bool[cells.Count];
        _distance = new int[cells.Count];
        _queue = new int[cells.Count];
        Array.Fill(_distance, -1);
        _doorsLocked = doorsLocked;
        foreach (var sprite in sprites.GetNear(Vector2.Zero, terrain.MapSize.ToVector2()))
        {
            if (IsGate(sprite.Definition.Id))
            {
                _gates.Add(sprite);
            }
        }
    }

    public float CellStep => _lattice.CellStep;

    public int Version { get; private set; }

    public int ReachedCount { get; private set; }

    public void SetDoorsLocked(bool doorsLocked)
    {
        bool doorsChange = _doorsLocked != doorsLocked;

        if (!doorsChange && !_moved)
            return;

        _doorsLocked = doorsLocked;
        if (doorsChange)
        {
            Version++;
            foreach (var gate in _gates)
            {
                if (gate.Definition.Id != KnownObject.Fence)
                {
                    RefreshCells(gate.Position.AsVector2(), gate.Definition.Half);
                }
            }
        }
        else
        {
            Array.Fill(_computed, false);
        }

        _moved = false;
        ClearDistances();
    }

    public void RefreshMovement(Vector2 center, Vector2 half)
    {
        RefreshCells(center, half);
        ClearDistances();
    }

    // Follows pushed bodies, sinking doors and falling fences, and refreshes the cells around removed and created obstacles
    public bool Synchronize(Map map)
    {
        if (_tracked is null)
        {
            _removedCount = map.RemovedCount;
            foreach (var placement in map.Sprites)
            {
                if (map.IsRemoved(placement))
                {
                    _removed.Add(placement);
                }
            }

            _tracked = new Dictionary<MergedMapSprite, Tracked>(ReferenceEqualityComparer.Instance);
            foreach (var placement in map.Sprites)
            {
                var sprite = map.FindPlacementSprite(placement);

                if (sprite is not null && (sprite.Definition.CanBePushed || IsGate(sprite.Definition.Id)))
                {
                    _tracked.Add(placement, new Tracked(sprite));
                }
            }
        }

        bool changed = false;
        foreach (var (placement, tracked) in _tracked)
        {
            var sprite = tracked.Sprite;

            if (sprite.Definition.Id == KnownObject.Fence)
            {
                bool isSolid = IsSolid(sprite);

                if (isSolid != tracked.WasSolid)
                {
                    tracked.WasSolid = isSolid;
                    RefreshCells(sprite.Position.AsVector2(), sprite.Definition.Half);
                    Version++;
                    changed = true;
                }

                continue;
            }

            if (_removed.Contains(placement) || Vector3.DistanceSquared(sprite.Position, tracked.Position) <= MinMove)
                continue;

            tracked.Position = sprite.Position;
            _moved = true;
            Version++;
        }

        if (map.RemovedCount != _removedCount)
        {
            _removedCount = map.RemovedCount;
            foreach (var placement in map.Sprites)
            {
                if (!map.IsRemoved(placement) || !_removed.Add(placement))
                    continue;

                changed |= Refresh(map, placement, _tracked.TryGetValue(placement, out var tracked) ? tracked.Position : placement.Position,
                    placement.Id is KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack or KnownObject.BreakableWall);
            }
        }

        for (; _spriteCount < map.Sprites.Count; _spriteCount++)
        {
            var placement = map.Sprites[_spriteCount];
            changed |= Refresh(map, placement, placement.Position, false);
        }

        if (changed)
        {
            _moved = false;
            ClearDistances();
        }

        return changed;
    }

    public bool CanStand(in GameObject walker, Vector3 point, bool anticipateDoors = false) =>
        _terrain.CanStandOnGround(walker, point) && !IsBlocked(walker, point, anticipateDoors);

    public bool CanMove(in GameObject walker, Vector3 from, Vector2 to, bool anticipateDoors = false)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from.AsVector2(), to) / SweepStep));
        var point = from;
        var start = from.AsVector2();
        var delta = to - start;
        for (int step = 1; step <= steps; step++)
        {
            point = new Vector3(start + delta * (step / (float)steps), point.Z);

            if (!_terrain.CanStandOnGround(walker, point, out float ground) || IsBlocked(walker, point, anticipateDoors))
                return false;

            if (walker.Properties.HasFlag(ObjectProperties.ZeroZ))
            {
                point.Z = float.IsNaN(ground) ? _terrain.GetSupportHeight(walker, point.AsVector2()) : ground;
            }
        }

        return true;
    }

    public bool CrossesClosedGate(Vector2 from, Vector2 to)
    {
        foreach (var gate in _gates)
        {
            if (IsSolid(gate) && (gate.Definition.Id == KnownObject.Fence || _doorsLocked) && Crosses(from, to, gate.Position.AsVector2(), gate.Definition.Half))
                return true;
        }

        return false;
    }

    public int Find(Vector2 point) => Find(_lattice.GetCell(point.X, point.Y));

    public int Find(Cell cell)
    {
        int u = cell.U - _minU;
        int v = cell.V - _minV;

        return u >= 0 && v >= 0 && u < _width && v < _height ? _cells[v * _width + u] : -1;
    }

    public Vector2 GetCenter(int cell) => _centers[cell];

    public int GetDistance(int cell) => _distance[cell] <= _floodLimit ? _distance[cell] : -1;

    public int GetReached(int order) => _queue[order];

    public int FindTrigger(Vector2 point, float radius)
    {
        int best = -1;
        float nearest = radius;
        for (int order = 0; order < ReachedCount; order++)
        {
            int cell = _queue[order];
            float distance = GetCenter(cell).MeasureOctagonalDistance(point);

            if (distance < nearest)
            {
                nearest = distance;
                best = cell;
            }
        }

        return best;
    }

    public int GetDownhill(int cell)
    {
        int best = -1;
        int bestDistance = GetDistance(cell);
        for (int side = 0; side < Sides; side++)
        {
            int next = GetNeighbor(cell, side);

            if (next < 0 || GetDistance(next) < 0 || GetDistance(next) >= bestDistance)
                continue;

            best = next;
            bestDistance = GetDistance(next);
        }

        return best;
    }

    public bool CanMove(Vector3 from, Vector2 to) => Find(to) >= 0 && CanMove(_walker, from, to, anticipateDoors: true);

    public int FindFree(Vector2 point, float reach = 60, bool reachedOnly = false, bool skipReached = false, Vector2 skippedHalf = default)
    {
        var cell = _lattice.GetCell(point.X, point.Y);
        int radius = (int)MathF.Ceiling(reach / Math.Min(_lattice.StepX, _lattice.StepY));
        int best = -1;
        float distance = reach * reach;
        for (int v = -radius; v <= radius; v++)
        for (int u = -radius; u <= radius; u++)
        {
            int candidate = Find(new Cell(cell.U + u, cell.V + v));

            if (candidate < 0 || reachedOnly && GetDistance(candidate) < 0 || skipReached && GetDistance(candidate) >= 0)
                continue;
            var center = GetCenter(candidate);
            float nextDistance = Vector2.DistanceSquared(center, point);

            if (Math.Abs(center.X - point.X) < skippedHalf.X && Math.Abs(center.Y - point.Y) < skippedHalf.Y)
                continue;

            if (nextDistance <= distance && CanStand(_walker, center.AsVector3(), anticipateDoors: true))
            {
                best = candidate;
                distance = nextDistance;
            }
        }

        return best;
    }

    public int FindStart(Vector3 point)
    {
        if (_lastStartVersion == Version && Vector3.DistanceSquared(_lastStartPoint, point) < StartTolerance)
            return _lastStart;

        _lastStart = SearchStart(point);
        _lastStartPoint = point;
        _lastStartVersion = Version;

        return _lastStart;
    }

    public void Flood(int source, int maxSteps)
    {
        if (source >= 0 && source == _floodSource && maxSteps <= _floodSteps)
        {
            _floodLimit = maxSteps;
            ReachedCount = CountWithin(maxSteps);

            return;
        }

        ClearDistances();
        _floodCount = 0;
        _floodSource = source;
        _floodSteps = maxSteps;
        _floodLimit = maxSteps;

        if (source >= 0)
        {
            int head = 0;
            _distance[source] = 0;
            _queue[_floodCount++] = source;
            while (head < _floodCount)
            {
                int cell = _queue[head++];
                int steps = _distance[cell] + 1;

                if (steps > maxSteps)
                    continue;

                var links = GetLinks(cell).Slice(cell * Sides, Sides);
                foreach (int next in links)
                {
                    if (next < 0 || _distance[next] >= 0)
                        continue;

                    _distance[next] = steps;
                    _queue[_floodCount++] = next;
                }
            }
        }

        ReachedCount = _floodCount;
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

    private static bool IsSolid(Sprite sprite) => sprite.Collector is not null && sprite.Animation < SpriteAnimation.Death;

    private static bool IsGate(KnownObject id) => id is KnownObject.SmallDoor or KnownObject.DoubleDoor or KnownObject.Fence;

    private static bool IsObstacle(SpriteDefinition definition)
    {
        ref readonly var obj = ref definition.Object;

        return definition.Id == KnownObject.Crate || !(obj.SpriteType.HasFlag(SpriteType.Unit) || obj.SpriteType.HasFlag(SpriteType.Avia) ||
            obj.SpriteType.HasFlag(SpriteType.Cannon) || obj.SpriteClass == SpriteClass.Man);
    }

    private static HashSet<Cell> GetCellsInMap(RouteGrid grid, Lattice lattice, System.Drawing.SizeF mapSize)
    {
        var cells = new HashSet<Cell>();
        foreach (var cell in grid.Cells.Concat(grid.BlockedCells))
        {
            for (int v = 0; v < 2; v++)
            {
                for (int u = 0; u < 2; u++)
                {
                    var fine = new Cell(cell.U * 2 + u, cell.V * 2 + v);
                    var center = lattice.GetCenter(fine);

                    if (center is { X: >= 0, Y: >= 0 } && center.X < mapSize.Width && center.Y < mapSize.Height)
                    {
                        cells.Add(fine);
                    }
                }
            }
        }

        return cells;
    }

    private bool Refresh(Map map, MergedMapSprite placement, Vector3 position, bool always)
    {
        var definition = map.Definitions.Find(placement.Id);
        bool blocks = definition is not null && IsObstacle(definition) && definition.Object.IsHittable && definition.MoveMask != 0;
        bool raises = definition is not null && definition.HeightPoints.Count != 0;

        if (blocks || raises)
        {
            Version++;
        }

        if (!blocks && !raises && !always)
            return false;

        RefreshCells(position.AsVector2(), definition?.Half ?? Vector2.Zero);

        return true;
    }

    private void RefreshCells(Vector2 center, Vector2 half)
    {
        var origin = _lattice.GetCell(center.X, center.Y);
        int radius = (int)MathF.Ceiling((half.Length() + _walker.Size.Length() + CellStep) / Math.Min(_lattice.StepX, _lattice.StepY)) + Margin;
        for (int v = Math.Max(0, origin.V - radius - _minV); v <= Math.Min(_height - 1, origin.V + radius - _minV); v++)
        {
            for (int u = Math.Max(0, origin.U - radius - _minU); u <= Math.Min(_width - 1, origin.U + radius - _minU); u++)
            {
                int i = _cells[v * _width + u];

                if (i >= 0)
                {
                    _computed[i] = false;
                }
            }
        }
    }

    private ReadOnlySpan<int> GetLinks(int cell)
    {
        if (!_computed[cell])
        {
            UpdateNeighbors(cell);
            _computed[cell] = true;
        }

        return _neighbors;
    }

    private int GetNeighbor(int cell, int side) => GetLinks(cell)[cell * Sides + side];

    private void UpdateNeighbors(int index)
    {
        var cell = _places[index];
        var from = new Vector3(_centers[index], _terrain.GetSupportHeight(_walker, _centers[index]));
        for (int side = 0; side < Sides; side++)
        {
            int target = Find(new Cell(cell.U + SideSteps[side].U, cell.V + SideSteps[side].V));
            _neighbors[index * Sides + side] = target >= 0 && CanMove(_walker, from, _centers[target], anticipateDoors: true) ? target : -1;
        }
    }

    private bool IsBlocked(in GameObject walker, Vector3 point, bool anticipateDoors)
    {
        var volume = new CollisionVolume(point, walker.Size.AsVector2() / 2, walker.Size.Z, walker.MoveMask);
        var reach = volume.Half + _sprites.LargestHalf;
        foreach (var sprite in _sprites.GetNear(point.AsVector2() - reach, point.AsVector2() + reach))
        {
            var definition = sprite.Definition;
            var position = sprite.Position;
            bool door = anticipateDoors && definition.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor;
            if (door)
            {
                if (!_doorsLocked)
                    continue;

                position.Z = sprite.Placement?.Position.Z ?? position.Z;
            }

            if (volume.Overlaps(new CollisionVolume(position, definition.Half, definition.Object.Size.Z, definition.MoveMask)) &&
                sprite.Animation < SpriteAnimation.Death && IsObstacle(definition))
                return true;
        }

        return false;
    }

    private int SearchStart(Vector3 point)
    {
        int here = Find(point.AsVector2());

        if (here >= 0 && CanMove(point, GetCenter(here)))
            return here;

        var cell = _lattice.GetCell(point.X, point.Y);
        int radius = (int)MathF.Ceiling(StartReach / Math.Min(_lattice.StepX, _lattice.StepY));
        Span<Candidate> nearby = stackalloc Candidate[(2 * radius + 1) * (2 * radius + 1)];
        int count = 0;
        for (int v = -radius; v <= radius; v++)
        for (int u = -radius; u <= radius; u++)
        {
            int candidate = Find(new Cell(cell.U + u, cell.V + v));

            if (candidate >= 0)
            {
                count = AddCandidate(nearby, count, candidate, point, StartReach);
            }
        }

        int found = FindFirstReachable(nearby[..count], point);

        if (found >= 0)
            return found;

        var pooled = ArrayPool<Candidate>.Shared.Rent(_places.Length);
        try
        {
            count = 0;
            for (int candidate = 0; candidate < _places.Length; candidate++)
            {
                count = AddCandidate(pooled, count, candidate, point, RecoveryReach);
            }

            return FindFirstReachable(pooled.AsSpan(0, count), point);
        }
        finally
        {
            ArrayPool<Candidate>.Shared.Return(pooled);
        }
    }

    private int AddCandidate(Span<Candidate> candidates, int count, int place, Vector3 point, float reach)
    {
        float distance = Vector2.DistanceSquared(point.AsVector2(), GetCenter(place));

        if (distance < reach * reach)
        {
            candidates[count++] = new Candidate(place, distance);
        }

        return count;
    }

    private int FindFirstReachable(Span<Candidate> candidates, Vector3 point)
    {
        candidates.Sort(static (first, second) => first.Distance.CompareTo(second.Distance));
        foreach (var candidate in candidates)
        {
            if (CanMove(point, GetCenter(candidate.Place)))
                return candidate.Place;
        }

        return -1;
    }

    // Counts the cells reached within maxSteps with a binary search over the flood order. The bound never equals a cell, which makes the search return the complement of the count.
    private int CountWithin(int maxSteps) => ~new ReadOnlySpan<int>(_queue, 0, _floodCount).BinarySearch(new StepBound(_distance, maxSteps));

    private void ClearDistances()
    {
        _floodSource = -1;
        for (int order = 0; order < Math.Max(_floodCount, ReachedCount); order++)
        {
            _distance[_queue[order]] = -1;
        }
    }

    private readonly record struct Candidate(int Place, float Distance);

    private sealed class Tracked
    {
        public Tracked(Sprite sprite)
        {
            Sprite = sprite;
            Position = sprite.Position;
            WasSolid = IsSolid(sprite);
        }

        public Sprite Sprite { get; }

        public Vector3 Position { get; set; }

        public bool WasSolid { get; set; }
}

    private readonly struct StepBound : IComparable<int>
    {
        private readonly int[] _distance;
        private readonly int _maxSteps;

        public StepBound(int[] distance, int maxSteps)
        {
            _distance = distance;
            _maxSteps = maxSteps;
        }

        public int CompareTo(int cell) => _distance[cell] <= _maxSteps ? 1 : -1;
}
}
