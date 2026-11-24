using System.Buffers;
using System.Drawing;
using System.Numerics;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Routes;
using ASToolkit.Engine;
using ASToolkit.Formats;

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
    private readonly Dictionary<Sprite, float> _gateHeights = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Sprite> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly List<TrackedSprite> _tracked = [];
    private readonly Dictionary<SpriteDefinition, bool> _relevant = new(ReferenceEqualityComparer.Instance);
    private bool _isStarted;
    private int _population;
    private bool _moved;
    private bool _doorsLocked;
    private Vector3 _lastStartPoint;
    private int _lastStart;
    private int _lastStartVersion = -1;
    private int _floodSource = -1;
    private int _floodSteps = -1;
    private int _floodLimit = -1;
    private int _floodCount;

    public WalkField(RouteGrid grid, bool doorsLocked, MovementMap terrain, SpriteCollector sprites, GameObject walker)
    {
        _terrain = terrain;
        _sprites = sprites;
        _walker = walker;
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
                _gateHeights[sprite] = sprite.Position.Z;
            }
        }
    }

    public float CellStep => _lattice.CellStep;

    public int Version { get; private set; }

    public int ReachedCount { get; private set; }

    public static bool IsSolid(Sprite sprite) => sprite.Definition.Object.Properties.HasFlag(ObjectProperties.Hash) && sprite.Animation < SpriteAnimation.Death;

    public void SetDoorsLocked(bool doorsLocked)
    {
        bool haveDoorsChanged = _doorsLocked != doorsLocked;

        if (!haveDoorsChanged && !_moved)
            return;

        _doorsLocked = doorsLocked;
        if (haveDoorsChanged)
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
        bool changed = false;

        if (!_isStarted)
        {
            _isStarted = true;
            _population = GetPopulation(map);
            AddNew(map, false);
        }

        foreach (var tracked in _tracked)
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
            }
            else if (tracked is { IsGone: false, CanMove: true } && Vector3.DistanceSquared(sprite.Position, tracked.Position) > MinMove)
            {
                tracked.Position = sprite.Position;
                _moved = true;
                Version++;
            }

            if (tracked.IsGone || sprite is { Animation: < SpriteAnimation.Death, ReferenceCount: > 0 })
                continue;

            tracked.IsGone = true;
            changed |= Refresh(map, sprite.Definition, tracked.Position, sprite.Definition.Id is KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack or KnownObject.BreakableWall);
        }

        int population = GetPopulation(map);

        if (population != _population)
        {
            _population = population;
            changed |= AddNew(map, true);
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

    private static int GetPopulation(Map map)
    {
        int total = 0;
        for (int layer = 0; layer < Map.LayerCount; layer++)
        {
            total += map.GetLayer(layer).Count;
        }

        return total;
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

    private static bool IsGate(KnownObject id) => id is KnownObject.SmallDoor or KnownObject.DoubleDoor or KnownObject.Fence;

    private static bool IsObstacle(SpriteDefinition definition)
    {
        ref readonly var obj = ref definition.Object;

        return definition.Id == KnownObject.Crate || !(obj.SpriteType.HasFlag(SpriteType.Unit) || obj.SpriteType.HasFlag(SpriteType.Avia) ||
            obj.SpriteType.HasFlag(SpriteType.Cannon) || obj.SpriteClass == SpriteClass.Man);
    }

    private static HashSet<Cell> GetCellsInMap(RouteGrid grid, Lattice lattice, SizeF mapSize)
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

    private static bool Blocks(SpriteDefinition definition) => IsObstacle(definition) && definition.Object.IsHittable && definition.MoveMask != 0;

    private bool AddNew(Map map, bool refresh)
    {
        bool changed = false;

        foreach (var sprite in map.UpdatedSprites)
        {
            var definition = sprite.Definition;

            if (!_relevant.TryGetValue(definition, out bool isRelevant))
            {
                isRelevant = definition.CanBePushed || IsGate(definition.Id) || Blocks(definition) || definition.HeightPoints.Count != 0;
                _relevant[definition] = isRelevant;
            }

            if (!isRelevant || !_seen.Add(sprite))
                continue;

            var tracked = new TrackedSprite(sprite, definition.CanBePushed || IsGate(definition.Id));
            tracked.IsGone = sprite.Animation >= SpriteAnimation.Death;
            _tracked.Add(tracked);

            if (refresh)
            {
                changed |= Refresh(map, definition, sprite.Position, false);
            }
        }

        return changed;
    }

    private bool Refresh(Map map, SpriteDefinition definition, Vector3 position, bool force)
    {
        bool isBlocking = Blocks(definition);
        bool isRaised = definition.HeightPoints.Count != 0;

        if (isBlocking || isRaised)
        {
            Version++;
        }

        if (!isBlocking && !isRaised && !force)
            return false;

        RefreshCells(position.AsVector2(), definition.Half);

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
            bool isDoor = anticipateDoors && definition.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor;
            if (isDoor)
            {
                if (!_doorsLocked)
                    continue;

                position.Z = _gateHeights.GetValueOrDefault(sprite, position.Z);
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
        Span<WalkCandidate> nearby = stackalloc WalkCandidate[(2 * radius + 1) * (2 * radius + 1)];
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

        var pooled = ArrayPool<WalkCandidate>.Shared.Rent(_places.Length);
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
            ArrayPool<WalkCandidate>.Shared.Return(pooled);
        }
    }

    private int AddCandidate(Span<WalkCandidate> candidates, int count, int place, Vector3 point, float reach)
    {
        float distance = Vector2.DistanceSquared(point.AsVector2(), GetCenter(place));

        if (distance < reach * reach)
        {
            candidates[count++] = new WalkCandidate(place, distance);
        }

        return count;
    }

    private int FindFirstReachable(Span<WalkCandidate> candidates, Vector3 point)
    {
        candidates.Sort(static (first, second) => first.Distance.CompareTo(second.Distance));
        foreach (var candidate in candidates)
        {
            if (CanMove(point, GetCenter(candidate.Place)))
                return candidate.Place;
        }

        return -1;
    }

    // Counts the cells reached within maxSteps with a binary search over the flood order. Because the bound never equals a cell, the search always returns the complement of the count.
    private int CountWithin(int maxSteps) => ~new ReadOnlySpan<int>(_queue, 0, _floodCount).BinarySearch(new FloodStepBound(_distance, maxSteps));

    private void ClearDistances()
    {
        _floodSource = -1;
        for (int order = 0; order < Math.Max(_floodCount, ReachedCount); order++)
        {
            _distance[_queue[order]] = -1;
        }
    }
}
