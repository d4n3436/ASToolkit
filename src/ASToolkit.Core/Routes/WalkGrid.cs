using System.Numerics;

namespace ASToolkit.Core.Routes;

// The 8 px height grid the Alien Shooter 2 engine walks on, with the solid boxes and the teleport links on top of it
internal sealed class WalkGrid
{
    public const int CellSize = 8;

    private const float FootprintInset = 3;
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;
    private const int SnapCells = 12;
    private const float SlopeRatio = 17f / 12f;
    private const int NoHeight = -999999;
    private const int Stickiness = 7;

    private static readonly (int X, int Y, int Cost)[] Neighbors =
        [(1, 0, StraightCost), (-1, 0, StraightCost), (0, 1, StraightCost), (0, -1, StraightCost),
         (1, 1, DiagonalCost), (1, -1, DiagonalCost), (-1, 1, DiagonalCost), (-1, -1, DiagonalCost)];

    private readonly short[] _sceneryHeights;
    private readonly short[] _objectHeights;
    private readonly short[] _ground;
    private readonly short[] _standing;
    private readonly bool[] _stamped;
    private readonly bool[] _blocked;
    private readonly Dictionary<int, List<int>> _links = [];
    private readonly float _climb;
    private readonly float _drop;

    public WalkGrid(int columns, int rows, float climb, float drop)
    {
        Columns = columns;
        Rows = rows;
        _climb = climb;
        _drop = drop;
        _sceneryHeights = new short[columns * rows];
        _objectHeights = new short[columns * rows];
        _ground = new short[columns * rows];
        _standing = new short[columns * rows];
        _stamped = new bool[columns * rows];
        _blocked = new bool[columns * rows];
    }

    public int Columns { get; }

    public int Rows { get; }

    public int GetCell(Vector3 point) => GetCell((int)MathF.Floor(point.X / CellSize), (int)MathF.Floor(point.Y / CellSize));

    public Vector3 GetPoint(int cell) => new((cell % Columns + 0.5f) * CellSize, (cell / Columns + 0.5f) * CellSize, _standing[cell]);

    public void Stamp(IEnumerable<(float X, float Y, short Height)> points, Vector3 position, Vector3 scale, bool isScenery)
    {
        var heights = isScenery ? _sceneryHeights : _objectHeights;
        foreach (var point in points)
        {
            int cell = GetCell(new Vector3(position.X + point.X * scale.X, position.Y + point.Y * scale.Y, 0));
            short height = (short)Math.Clamp(MathF.Round(position.Z + point.Height * scale.Z), short.MinValue + 1, short.MaxValue);
            if (cell < 0)
            {
                continue;
            }

            _stamped[cell] = true;
            heights[cell] = Math.Max(heights[cell], height);
        }
    }

    public void Cover(Vector3 position, Vector2 halfSize)
    {
        int firstColumn = Math.Max(0, (int)MathF.Floor((position.X - halfSize.X) / CellSize));
        int lastColumn = Math.Min(Columns - 1, (int)MathF.Floor((position.X + halfSize.X) / CellSize));
        int firstRow = Math.Max(0, (int)MathF.Floor((position.Y - halfSize.Y) / CellSize));
        int lastRow = Math.Min(Rows - 1, (int)MathF.Floor((position.Y + halfSize.Y) / CellSize));
        for (int row = firstRow; row <= lastRow; row++)
        {
            Array.Fill(_stamped, true, row * Columns + firstColumn, Math.Max(0, lastColumn - firstColumn + 1));
        }
    }

    // Takes the ground under each footprint from both grids and blocks the footprints with no stamped cell
    public void SetGround(Vector2 halfSize)
    {
        float halfX = halfSize.X > FootprintInset ? halfSize.X - FootprintInset : halfSize.X;
        float halfY = halfSize.Y > FootprintInset ? halfSize.Y - FootprintInset : halfSize.Y;
        for (int row = 0; row < Rows; row++)
        {
            float y = (row + 0.5f) * CellSize;
            int firstRow = Math.Max(0, (int)MathF.Floor((y - halfY) / CellSize));
            int lastRow = Math.Min(Rows - 1, (int)MathF.Floor((y + halfY) / CellSize));
            for (int column = 0; column < Columns; column++)
            {
                float x = (column + 0.5f) * CellSize;
                int firstColumn = Math.Max(0, (int)MathF.Floor((x - halfX) / CellSize));
                int lastColumn = Math.Min(Columns - 1, (int)MathF.Floor((x + halfX) / CellSize));
                int highest = NoHeight;
                int steady = NoHeight;
                bool onFloor = false;
                for (int footprintRow = firstRow; footprintRow <= lastRow; footprintRow++)
                {
                    for (int footprintColumn = firstColumn; footprintColumn <= lastColumn; footprintColumn++)
                    {
                        int cell = footprintRow * Columns + footprintColumn;
                        Raise(_sceneryHeights[cell]);
                        Raise(_objectHeights[cell]);
                        onFloor |= _stamped[cell];
                    }
                }

                _ground[row * Columns + column] = (short)(steady != NoHeight ? steady : highest);
                _standing[row * Columns + column] = Math.Max(_sceneryHeights[row * Columns + column], _objectHeights[row * Columns + column]);
                _blocked[row * Columns + column] = !onFloor;

                void Raise(int height)
                {
                    int difference = Math.Abs(height - highest);
                    highest = Math.Max(highest, height);
                    if (difference < Stickiness)
                    {
                        steady = highest;
                    }
                }
            }
        }
    }

    public void Block(Vector3 position, Vector3 halfSize, float height, Vector2 walkerHalfSize, float walkerHeight)
    {
        int firstColumn = Math.Max(0, (int)MathF.Floor((position.X - halfSize.X - walkerHalfSize.X) / CellSize));
        int lastColumn = Math.Min(Columns - 1, (int)MathF.Floor((position.X + halfSize.X + walkerHalfSize.X) / CellSize));
        int firstRow = Math.Max(0, (int)MathF.Floor((position.Y - halfSize.Y - walkerHalfSize.Y) / CellSize));
        int lastRow = Math.Min(Rows - 1, (int)MathF.Floor((position.Y + halfSize.Y + walkerHalfSize.Y) / CellSize));
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int cell = row * Columns + column;
                float ground = _ground[cell];
                if (position.Z < ground + walkerHeight && ground < position.Z + height)
                {
                    _blocked[cell] = true;
                }
            }
        }
    }

    public void Link(Vector3 center, float radius, Vector3 target)
    {
        int to = Snap(GetCell(target));
        if (to < 0)
        {
            return;
        }

        foreach (int cell in GetCellsWithin(center, radius))
        {
            if (!_links.TryGetValue(cell, out var targets))
            {
                targets = [];
                _links[cell] = targets;
            }

            targets.Add(to);
        }
    }

    // Finds the cells from the start to the first free cell within the radius of the goal, or to the one the preference picks
    public bool TryFindPath(int start, Vector3 goal, float radius, out List<int> path, PathPreference? preference = null)
    {
        path = [];
        start = Snap(start);
        var goals = GetCellsWithin(goal, radius).Where(cell => !_blocked[cell]).ToHashSet();
        if (start < 0 || goals.Count == 0)
        {
            return false;
        }

        int goalColumn = Math.Clamp((int)(goal.X / CellSize), 0, Columns - 1);
        int goalRow = Math.Clamp((int)(goal.Y / CellSize), 0, Rows - 1);
        var reached = new List<int>();
        int[] costs = new int[_ground.Length];
        int[] previous = new int[_ground.Length];
        Array.Fill(costs, int.MaxValue);
        costs[start] = 0;
        previous[start] = -1;
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(start, GetEstimate(start, goalColumn, goalRow));
        var next = new List<(int Cell, int Cost)>();
        while (queue.TryDequeue(out int cell, out int priority))
        {
            int cost = costs[cell];
            if (priority > cost + GetEstimate(cell, goalColumn, goalRow))
            {
                continue;
            }

            if (goals.Contains(cell))
            {
                reached.Add(cell);
                if (preference is null)
                {
                    break;
                }
            }

            GetNext(cell, next);
            foreach (var (neighbor, step) in next)
            {
                if (costs[neighbor] > cost + step)
                {
                    costs[neighbor] = cost + step;
                    previous[neighbor] = cell;
                    queue.Enqueue(neighbor, cost + step + GetEstimate(neighbor, goalColumn, goalRow));
                }
            }
        }

        int end = preference is null ? reached.FirstOrDefault(-1) : ChooseEnd(reached, preference);
        if (end < 0)
        {
            return false;
        }

        for (int cell = end; cell >= 0; cell = previous[cell])
        {
            path.Add(cell);
        }

        path.Reverse();
        return true;
    }

    // Tells whether the goal can be reached from a point and adds the cells of a failed search to the failed set
    public bool CanReach(Vector3 from, Vector3 goal, float radius, HashSet<int> failed)
    {
        int start = Snap(GetCell(from));
        var goals = GetCellsWithin(goal, radius).Where(cell => !_blocked[cell]).ToHashSet();
        if (start < 0 || goals.Count == 0 || failed.Contains(start))
        {
            return false;
        }

        var visited = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        var next = new List<(int Cell, int Cost)>();
        while (queue.TryDequeue(out int cell))
        {
            if (goals.Contains(cell))
            {
                return true;
            }

            GetNext(cell, next);
            foreach (var (neighbor, _) in next)
            {
                if (!failed.Contains(neighbor) && visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        failed.UnionWith(visited);
        return false;
    }

    public bool IsLink(int from, int to) => _links.TryGetValue(from, out var targets) && targets.Contains(to) && !IsNeighbor(from, to);

    private bool IsNeighbor(int first, int second) => Math.Abs(first % Columns - second % Columns) <= 1 && Math.Abs(first / Columns - second / Columns) <= 1;

    // Takes the cell nearest to the target that the accept test allows, or the nearest one when the preference is not strict and none is allowed
    private int ChooseEnd(List<int> reached, PathPreference preference)
    {
        var nearest = reached.OrderBy(cell => Vector2.Distance(new Vector2((cell % Columns + 0.5f) * CellSize, (cell / Columns + 0.5f) * CellSize), new Vector2(preference.Target.X, preference.Target.Y))).ToList();
        foreach (int cell in nearest.Where(cell => preference.Accept(GetPoint(cell))))
        {
            return cell;
        }

        return preference.IsStrict ? -1 : nearest.FirstOrDefault(-1);
    }

    // Lists the cells a walker can step to from a cell, then the cells a teleport leads to
    private void GetNext(int cell, List<(int Cell, int Cost)> next)
    {
        next.Clear();
        int column = cell % Columns;
        int row = cell / Columns;
        foreach ((int x, int y, int step) in Neighbors)
        {
            int nextColumn = column + x;
            int nextRow = row + y;
            if ((uint)nextColumn >= (uint)Columns || (uint)nextRow >= (uint)Rows)
            {
                continue;
            }

            int neighbor = nextRow * Columns + nextColumn;
            if (CanStep(cell, neighbor) && (x == 0 || y == 0 || (CanStep(cell, row * Columns + nextColumn) && CanStep(cell, nextRow * Columns + column))))
            {
                next.Add((neighbor, step));
            }
        }

        if (_links.TryGetValue(cell, out var targets))
        {
            next.AddRange(targets.Select(target => (target, StraightCost)));
        }
    }

    private bool CanStep(int from, int to)
    {
        if (_blocked[to])
        {
            return false;
        }

        float rise = _ground[to] - _standing[from];
        return _ground[to] - _ground[from] <= _climb && rise <= _climb * SlopeRatio && -rise <= _drop;
    }

    private int GetEstimate(int cell, int goalColumn, int goalRow)
    {
        int x = Math.Abs(cell % Columns - goalColumn);
        int y = Math.Abs(cell / Columns - goalRow);
        return StraightCost * Math.Max(x, y) + (DiagonalCost - StraightCost) * Math.Min(x, y);
    }

    private int GetCell(int column, int row) => (uint)column < (uint)Columns && (uint)row < (uint)Rows ? row * Columns + column : -1;

    private IEnumerable<int> GetCellsWithin(Vector3 center, float radius)
    {
        int reach = (int)MathF.Ceiling(radius / CellSize);
        int centerColumn = (int)MathF.Floor(center.X / CellSize);
        int centerRow = (int)MathF.Floor(center.Y / CellSize);
        for (int row = centerRow - reach; row <= centerRow + reach; row++)
        {
            for (int column = centerColumn - reach; column <= centerColumn + reach; column++)
            {
                int cell = GetCell(column, row);
                if (cell >= 0 && Vector2.Distance(new Vector2((column + 0.5f) * CellSize, (row + 0.5f) * CellSize), new Vector2(center.X, center.Y)) <= radius)
                {
                    yield return cell;
                }
            }
        }
    }

    // Moves a cell inside a blocked area to the nearest free cell
    private int Snap(int cell)
    {
        if (cell < 0 || !_blocked[cell])
        {
            return cell;
        }

        int column = cell % Columns;
        int row = cell / Columns;
        for (int reach = 1; reach <= SnapCells; reach++)
        {
            for (int y = -reach; y <= reach; y++)
            {
                for (int x = -reach; x <= reach; x++)
                {
                    int near = GetCell(column + x, row + y);
                    if ((Math.Abs(x) == reach || Math.Abs(y) == reach) && near >= 0 && !_blocked[near])
                    {
                        return near;
                    }
                }
            }
        }

        return -1;
    }
}
