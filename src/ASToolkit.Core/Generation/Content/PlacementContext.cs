using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the state shared while content is placed, with the map so far, the cells taken and the walking distance of every cell.
/// </summary>
public sealed class PlacementContext
{
    private readonly Dictionary<(int, int), List<(double X, double Y, double Ex, double Ey)>> _wallBoxes = [];
    private readonly IReadOnlySet<Cell> _blocked;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlacementContext"/> class.
    /// </summary>
    public PlacementContext(Random rng, int level, LayoutPlan plan, FineLayout layout, MapBuilder map, ObjectTable objects, Cell start, IReadOnlySet<Cell> blocked, IReadOnlySet<Cell> keepClear)
    {
        Rng = rng;
        Level = level;
        Plan = plan;
        Layout = layout;
        Map = map;
        Objects = objects;
        Start = start;
        Taken = new HashSet<Cell>(blocked.Concat(keepClear));
        foreach (var sprite in map.Sprites)
        {
            if (KnownObject.LabWalls.Contains(sprite.Id) || KnownObject.MilitaryWalls.Contains(sprite.Id) || sprite.Id == KnownObject.Fence)
            {
                AddWallBox(sprite.X, sprite.Y, sprite.Id);
            }
        }

        _blocked = blocked;
        Distance = MeasureDistances(start);
        RouteLength = Distance.Values.DefaultIfEmpty(1).Max();
    }

    /// <summary>Gets the random source.</summary>
    public Random Rng { get; }

    /// <summary>Gets the campaign level number.</summary>
    public int Level { get; }

    /// <summary>Gets the layout plan.</summary>
    public LayoutPlan Plan { get; }

    /// <summary>Gets the layout on the cell lattice.</summary>
    public FineLayout Layout { get; }

    /// <summary>Gets the map being built.</summary>
    public MapBuilder Map { get; }

    /// <summary>Gets the object table.</summary>
    public ObjectTable Objects { get; }

    /// <summary>Gets the player start cell.</summary>
    public Cell Start { get; }

    /// <summary>Gets the cells already holding something.</summary>
    public HashSet<Cell> Taken { get; }

    /// <summary>Gets the walking distance of every reachable cell from the start, in cell steps, with every door open.</summary>
    public IReadOnlyDictionary<Cell, int> Distance { get; }

    /// <summary>Gets the largest walking distance.</summary>
    public int RouteLength { get; }

    /// <summary>Gets the length of one cell step in pixels.</summary>
    public double Step => Layout.Lattice.CellStep;

    /// <summary>
    /// Returns how far along the route a cell lies, from 0 at the start to 1 at the farthest cell.
    /// </summary>
    public double GetProgress(Cell c) => Distance.GetValueOrDefault(c) / (double)Math.Max(1, RouteLength);

    /// <summary>
    /// Returns how far along the route a region begins.
    /// </summary>
    public double GetProgress(PlanRegion region) => GetRegionCells(region).Where(Distance.ContainsKey).Select(GetProgress).DefaultIfEmpty(0).Min();

    /// <summary>
    /// Returns the cells of a region in a stable order.
    /// </summary>
    public IReadOnlyList<Cell> GetRegionCells(PlanRegion region) => Layout.GetRegionCells(region.Id);

    /// <summary>
    /// Returns the free cells of a region, those not taken, without walls on their edges and reachable. Inner cells also have
    /// all four neighbors in the region.
    /// </summary>
    public Cell[] GetFreeCells(PlanRegion region, bool inner = false)
        => GetRegionCells(region) 
            .Where(c => IsFree(c) && (!inner || IsInside(c, region.Id)))
            .ToArray();

    /// <summary>
    /// Returns whether all four neighbors of a cell are free of anything taken.
    /// </summary>
    public bool IsClearAround(Cell c)
    {
        foreach (var (n, _) in EdgeKey.GetNeighbors(c))
        {
            if (Taken.Contains(n))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether a cell can hold something.
    /// </summary>
    public bool IsFree(Cell c) => !Taken.Contains(c) && !Layout.IsWalled(c) && Distance.ContainsKey(c);

    /// <summary>
    /// Returns whether a point lies within a margin of a wall piece's collision box.
    /// </summary>
    public bool IsInWall(double x, double y, double margin = 6)
    {
        (int bx, int by) = ((int)Math.Floor(x / 100), (int)Math.Floor(y / 100));
        for (int index = -1; index <= 1; index++)
        {
            for (int innerIndex = -1; innerIndex <= 1; innerIndex++)
            {
                if (!_wallBoxes.TryGetValue((bx + index, by + innerIndex), out var boxes))
                {
                    continue;
                }

                foreach ((double wx, double wy, double ex, double ey) in boxes)
                {
                    if (Math.Abs(x - wx) < ex + margin && Math.Abs(y - wy) < ey + margin)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a random point inside a cell clear of walls, or null when twelve tries fail.
    /// </summary>
    public (double X, double Y)? FindSpot(Cell c, double margin = 6)
    {
        for (int index = 0; index < 12; index++)
        {
            (double x, double y) = Layout.Lattice.ToWorld(c.U + 0.3 + 0.4 * Rng.NextDouble(), c.V + 0.3 + 0.4 * Rng.NextDouble());
            if (!IsInWall(x, y, margin))
            {
                return (x, y);
            }
        }

        return null;
    }

    /// <summary>
    /// Places an object on a free spot of one of the cells and returns its position and cell, or null when no cell has room.
    /// </summary>
    public (double X, double Y, Cell Cell)? Place(IReadOnlyList<Cell> cells, KnownObject id, int? dir = null, ReadOnlyMemory<byte> payload = default, uint? army = null, bool take = true)
    {
        if (cells.Count == 0)
        {
            return null;
        }

        int[] order = Enumerable.Range(0, cells.Count).ToArray();
        Rng.Shuffle(order);
        foreach (int cellIndex in order.Take(25))
        {
            var spot = FindSpot(cells[cellIndex]);
            if (spot is null)
            {
                continue;
            }

            if (take)
            {
                Taken.Add(cells[cellIndex]);
            }

            Map.Add(id, spot.Value.X, spot.Value.Y, 0, dir ?? Rng.Next(256), payload, army, unique: false);
            return (spot.Value.X, spot.Value.Y, cells[cellIndex]);
        }

        return null;
    }

    /// <summary>
    /// Returns the cells of a shortest walk between two cells, every door open.
    /// </summary>
    public Cell[] FindPath(Cell from, Cell to)
    {
        var previous = new Dictionary<Cell, Cell> { [from] = from };
        var queue = new Queue<Cell>();
        queue.Enqueue(from);
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            var c = queue.Dequeue();
            foreach (var (n, k) in EdgeKey.GetNeighbors(c))
            {
                if (Layout.Cells.ContainsKey(n) && !previous.ContainsKey(n) && !IsBlockingEdge(k))
                {
                    previous[n] = c;
                    queue.Enqueue(n);
                }
            }
        }

        if (!previous.ContainsKey(to))
        {
            return [from, to];
        }

        var path = new List<Cell>();
        for (var c = to; c != from; c = previous[c])
        {
            path.Add(c);
        }

        path.Add(from);
        path.Reverse();
        return path.ToArray();
    }

    /// <summary>
    /// Records a wall piece's collision box for later placements to keep clear of.
    /// </summary>
    public void AddWallBox(double x, double y, KnownObject id)
    {
        var o = Objects.Find(id);
        if (o is null)
        {
            return;
        }

        var bucket = ((int)Math.Floor(x / 100), (int)Math.Floor(y / 100));
        if (!_wallBoxes.TryGetValue(bucket, out var list))
        {
            list = [];
            _wallBoxes[bucket] = list;
        }

        list.Add((x, y, o.ExtentX, o.ExtentY));
    }

    private bool IsInside(Cell c, int region)
    {
        foreach (var (n, _) in EdgeKey.GetNeighbors(c))
        {
            if (Layout.Cells.GetValueOrDefault(n, -1) != region)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsBlockingEdge(EdgeKey k) => Layout.Edges.TryGetValue(k, out char kind) && RouteGrid.IsBlocking(kind);

    /// <summary>
    /// Returns the walking distance of every reachable cell from a cell, in cell steps, with every door open.
    /// </summary>
    public Dictionary<Cell, int> MeasureDistances(Cell start)
    {
        var distance = new Dictionary<Cell, int> { [start] = 0 };
        var queue = new Queue<Cell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var (n, k) in EdgeKey.GetNeighbors(c))
            {
                if (Layout.Cells.ContainsKey(n) && !distance.ContainsKey(n) && !IsBlockingEdge(k) && !_blocked.Contains(n))
                {
                    distance[n] = distance[c] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return distance;
    }
}
