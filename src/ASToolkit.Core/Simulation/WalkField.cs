using System.Buffers;
using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Simulation;

internal sealed class WalkField
{
    private const float StartReach = 60;
    private const float StartTolerance = 0.25f;
    private const int Margin = 2;
    private const int Sides = 8;
    private const float RecoveryReach = 512;
    private static readonly Cell[] SideSteps = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];

    private readonly Lattice _lattice;
    private readonly int _minU;
    private readonly int _minV;
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cells;
    private readonly Cell[] _places;
    private readonly Vector2[] _centers;
    private readonly MovementMap _movement;
    private readonly GameObject _walker;
    private readonly int[] _neighbors;
    private readonly int[] _distance;
    private readonly int[] _queue;
    private const byte SharedCell = 0;
    private const byte OwnCell = 1;
    private const byte StaleCell = 2;
    private readonly byte[] _cellState;
    private readonly SharedLinks _shared;
    private readonly List<GameWorld.TerrainChange> _gates = [];
    private int _movementVersion;
    private bool _doorsLocked;
    private bool _fencesUp;
    private Vector3 _lastStartPoint;
    private int _lastStart;
    private int _lastStartVersion = -1;
    private int _floodSource = -1;
    private int _floodSteps = -1;
    private int _floodLimit = -1;
    private int _floodCount;

    private WalkField(WalkField source, MovementMap movement)
    {
        _lattice = source._lattice;
        _minU = source._minU;
        _minV = source._minV;
        _width = source._width;
        _height = source._height;
        _cells = source._cells;
        _places = source._places;
        _centers = source._centers;
        _neighbors = (int[])source._neighbors.Clone();
        _cellState = (byte[])source._cellState.Clone();
        _shared = source._shared;
        _distance = (int[])source._distance.Clone();
        _queue = (int[])source._queue.Clone();
        ReachedCount = source.ReachedCount;
        _movement = movement;
        _walker = source._walker;
        _movementVersion = source._movementVersion;
        _doorsLocked = source._doorsLocked;
        _fencesUp = source._fencesUp;
        _lastStartPoint = source._lastStartPoint;
        _lastStart = source._lastStart;
        _lastStartVersion = source._lastStartVersion;
        _floodSource = source._floodSource;
        _floodSteps = source._floodSteps;
        _floodLimit = source._floodLimit;
        _floodCount = source._floodCount;
    }

    public WalkField Clone(MovementMap movement) => new(this, movement);

    public WalkField(RouteGrid grid, bool doorsLocked, bool fencesUp, MovementMap movement, GameObject walker)
    {
        _movement = movement;
        _walker = walker;
        _lattice = grid.Lattice.Halve();
        var cells = GetCellsInMap(grid, _lattice, movement.MapSize);
        _minU = cells.Min(cell => cell.U) - Margin;
        _minV = cells.Min(cell => cell.V) - Margin;
        _width = cells.Max(cell => cell.U) + Margin - _minU + 1;
        _height = cells.Max(cell => cell.V) + Margin - _minV + 1;
        _cells = new int[_width * _height];
        Array.Fill(_cells, -1);
        _places = new Cell[cells.Count];
        _centers = new Vector2[cells.Count];
        int index = 0;
        foreach (var cell in cells)
        {
            _cells[(cell.V - _minV) * _width + (cell.U - _minU)] = index;
            _places[index] = cell;
            _centers[index++] = _lattice.GetCenter(cell).ToVector2();
        }

        _neighbors = new int[cells.Count * Sides];
        _cellState = new byte[cells.Count];
        _shared = new SharedLinks(cells.Count);
        _distance = new int[cells.Count];
        _queue = new int[cells.Count];
        Array.Fill(_distance, -1);
        _doorsLocked = doorsLocked;
        _fencesUp = fencesUp;
        _movement.SetGates(doorsLocked, fencesUp);
        _movementVersion = _movement.Version;
    }

    public float CellStep => _lattice.CellStep;

    public void SetGates(bool doorsLocked, bool fencesUp)
    {
        bool doorsChange = _doorsLocked != doorsLocked;
        bool fencesChange = _fencesUp != fencesUp;

        if (_movementVersion == _movement.Version && !doorsChange && !fencesChange)
            return;

        _doorsLocked = doorsLocked;
        _fencesUp = fencesUp;
        _movement.SetGates(doorsLocked, fencesUp);
        if (doorsChange || fencesChange)
        {
            _gates.Clear();
            _movement.CollectGates(doorsChange, fencesChange, _gates);
            foreach (var gate in _gates)
            {
                RefreshCells(gate.Position, gate.HalfSize);
            }
        }
        else
        {
            MarkAllStale();
        }

        _movementVersion = _movement.Version;
        ClearDistances();
    }

    public void RefreshMovement(Vector2 center, Vector2 half)
    {
        _movementVersion = _movement.Version;
        RefreshCells(center, half);
        ClearDistances();
    }

    private void RefreshCells(Vector2 center, Vector2 half)
    {
        var origin = _lattice.GetCell(center.X, center.Y);
        int radius = (int)MathF.Ceiling((half.Length() + _walker.Size.Length() + CellStep) / Math.Min(_lattice.StepX, _lattice.StepY)) + Margin;
        for (int v = Math.Max(0, origin.V - radius - _minV); v <= Math.Min(_height - 1, origin.V + radius - _minV); v++)
        for (int u = Math.Max(0, origin.U - radius - _minU); u <= Math.Min(_width - 1, origin.U + radius - _minU); u++)
        {
            int index = _cells[v * _width + u];

            if (index >= 0)
            {
                _cellState[index] = StaleCell;
            }
        }
    }

    private void MarkAllStale() => Array.Fill(_cellState, StaleCell);

    // Reads untouched cells from the links all copies share, marking a cell computed only after its links are written
    private ReadOnlySpan<int> GetLinks(int cell)
    {
        switch (_cellState[cell])
        {
            case SharedCell:
                if (!Volatile.Read(ref _shared.Computed[cell]))
                {
                    UpdateNeighbors(cell, _shared.Links);
                    Volatile.Write(ref _shared.Computed[cell], true);
                }

                return _shared.Links;
            case StaleCell:
                UpdateNeighbors(cell, _neighbors);
                _cellState[cell] = OwnCell;
                break;
        }

        return _neighbors;
    }

    private int GetNeighbor(int cell, int side) => GetLinks(cell)[cell * Sides + side];

    private void UpdateNeighbors(int index, int[] links)
    {
        var cell = _places[index];
        var from = new Vector3(_centers[index], _movement.GetSupportHeight(_walker, _centers[index]));
        for (int side = 0; side < Sides; side++)
        {
            int target = Find(new Cell(cell.U + SideSteps[side].U, cell.V + SideSteps[side].V));
            links[index * Sides + side] = target >= 0 && _movement.CanMove(_walker, from, _centers[target], anticipateDoors: true) ? target : -1;
        }
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

    public int ReachedCount { get; private set; }

    public int GetReached(int order) => _queue[order];

    public int FindTrigger(Vector2 point, float radius)
    {
        int best = -1;
        float nearest = radius;
        for (int order = 0; order < ReachedCount; order++)
        {
            int cell = _queue[order];
            float distance = GetCenter(cell).MeasureDistance(point);

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

    public bool CanMove(Vector3 from, Vector2 to) => Find(to) >= 0 && _movement.CanMove(_walker, from, to, anticipateDoors: true);

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

            if (nextDistance <= distance && _movement.CanStand(_walker, center.AsVector3(), anticipateDoors: true))
            {
                best = candidate;
                distance = nextDistance;
            }
        }

        return best;
    }

    public int FindStart(Vector3 point)
    {
        if (_lastStartVersion == _movement.Version && Vector3.DistanceSquared(_lastStartPoint, point) < StartTolerance)
            return _lastStart;

        _lastStart = SearchStart(point);
        _lastStartPoint = point;
        _lastStartVersion = _movement.Version;

        return _lastStart;
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

    private int CountWithin(int maxSteps)
    {
        int low = 0, high = _floodCount;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_distance[_queue[middle]] <= maxSteps)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void ClearDistances()
    {
        _floodSource = -1;
        for (int order = 0; order < Math.Max(_floodCount, ReachedCount); order++)
        {
            _distance[_queue[order]] = -1;
        }
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

    private readonly record struct Candidate(int Place, float Distance);

    private sealed class SharedLinks
    {
        public SharedLinks(int cells)
        {
            Links = new int[cells * Sides];
            Computed = new bool[cells];
        }

        public int[] Links { get; }

        public bool[] Computed { get; }
    }
}
