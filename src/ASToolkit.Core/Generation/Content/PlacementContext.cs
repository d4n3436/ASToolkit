using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the state shared while content is placed, with the map so far, the cells taken and the walking distance of every cell.
/// </summary>
public sealed class PlacementContext
{
    private const float SightStep = 6;
    private static readonly SizeF ShadowSpread = new(12, 8);
    private static readonly Dictionary<ItemCategory, float> ShadowsPerPickup = new()
    {
        [ItemCategory.Money] = 1.6f, [ItemCategory.Weapon] = 2.8f, [ItemCategory.Implant] = 0.6f, [ItemCategory.Ammo] = 0.3f, [ItemCategory.Armor] = 0.3f
    };

    private static readonly Dictionary<KnownObject, float> ShadowsPerItem = new() { [KnownObject.ExtraLife] = 2.3f, [KnownObject.Invulnerability] = 8, [KnownObject.Medkit] = 1.75f };

    private readonly Dictionary<Cell, List<CollisionBox>> _wallBoxes = [];
    private readonly Dictionary<int, float> _progress = [];
    private readonly IReadOnlySet<Cell> _blocked;

    /// <summary>
    /// Gets the objects standing on the level's floor and the doorways kept clear of them.
    /// </summary>
    public LevelOccupancy Occupancy { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlacementContext"/> class.
    /// </summary>
    public PlacementContext(Random rng, LevelBrief brief, LayoutPlan plan, FineLayout layout, MapBuilder map, ObjectTable objects, Cell start, IReadOnlySet<Cell> blocked, IReadOnlySet<Cell> keepClear,
        LevelOccupancy occupancy)
    {
        Occupancy = occupancy;
        Rng = rng;
        Brief = brief;
        Plan = plan;
        Layout = layout;
        Map = map;
        Objects = objects;
        Start = start;
        Taken = new HashSet<Cell>(blocked.Concat(keepClear));
        foreach (var sprite in map.Sprites)
        {
            if (KnownObject.BasementWalls.Contains(sprite.Id) || KnownObject.ConcreteWalls.Contains(sprite.Id) || sprite.Id == KnownObject.Fence)
            {
                AddWallBox(sprite.Position.X, sprite.Position.Y, sprite.Id);
            }
        }

        _blocked = blocked;
        Distance = MeasureDistances(start);
        RouteLength = Distance.Values.DefaultIfEmpty(1).Max();
    }

    /// <summary>Gets the random source.</summary>
    public Random Rng { get; }

    /// <summary>Gets the brief of the level.</summary>
    public LevelBrief Brief { get; }

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

    /// <summary>Gets or sets how many more endless generators the level may hold.</summary>
    public int EndlessGeneratorsLeft { get; set; }

    /// <summary>Gets the cells already holding something.</summary>
    public HashSet<Cell> Taken { get; }

    /// <summary>Gets the walking distance of every reachable cell from the start, in cell steps, with every door open.</summary>
    public IReadOnlyDictionary<Cell, int> Distance { get; }

    /// <summary>Gets the largest walking distance.</summary>
    public int RouteLength { get; }

    /// <summary>Gets the length of one cell step in pixels.</summary>
    public float Step => Layout.Lattice.CellStep;

    /// <summary>
    /// Returns how far along the route a cell lies, from 0 at the start to 1 at the farthest cell.
    /// </summary>
    public float GetProgress(Cell c) => Distance.GetValueOrDefault(c) / (float)Math.Max(1, RouteLength);

    /// <summary>
    /// Returns how far along the route a region begins.
    /// </summary>
    public float GetProgress(PlanRegion region)
    {
        if (!_progress.TryGetValue(region.Id, out float progress))
        {
            progress = _progress[region.Id] = GetRegionCells(region).Where(Distance.ContainsKey).Select(GetProgress).DefaultIfEmpty(0).Min();
        }

        return progress;
    }

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
    public bool IsInWall(PointF point, float margin = 6)
    {
        (float x, float y) = point;
        (int bx, int by) = ((int)MathF.Floor(x / 100), (int)MathF.Floor(y / 100));
        for (int index = -1; index <= 1; index++)
        {
            for (int innerIndex = -1; innerIndex <= 1; innerIndex++)
            {
                if (!_wallBoxes.TryGetValue(new Cell(bx + index, by + innerIndex), out var boxes))
                {
                    continue;
                }

                foreach (var box in boxes)
                {
                    if (Math.Abs(x - box.X) < box.HalfWidth + margin && Math.Abs(y - box.Y) < box.HalfHeight + margin)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether the camera sees a floor point, with open floor below it on screen up to the roof height and no wall or roof
    /// drawn over it.
    /// </summary>
    public bool IsVisible(PointF point)
    {
        (float x, float y) = point;
        var previous = Layout.Lattice.GetCell(x, y);
        for (float d = 0; d <= StructureBuilder.BasementRoofZ; d += SightStep)
        {
            var cell = Layout.Lattice.GetCell(x, y + d);
            if (!Layout.Cells.TryGetValue(cell, out int region) || Plan.Regions[region].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset
                || cell != previous && !IsOpenStep(previous, cell))
            {
                return false;
            }

            previous = cell;
        }

        return true;
    }

    /// <summary>
    /// Returns a random point inside a cell clear of walls, or null when twelve tries fail.
    /// </summary>
    public PointF? FindSpot(Cell c)
    {
        for (int index = 0; index < 12; index++)
        {
            var point = Layout.Lattice.ToWorld(c.U + 0.3f + 0.4f * Rng.NextSingle(), c.V + 0.3f + 0.4f * Rng.NextSingle());
            if (!IsInWall(point, 6))
            {
                return point;
            }
        }

        return null;
    }

    /// <summary>
    /// Places an object on a free spot of one of the cells and returns its position and cell, or null when no cell has room.
    /// </summary>
    public (PointF Point, Cell Cell)? Place(IReadOnlyList<Cell> cells, KnownObject id, int? dir, ReadOnlyMemory<byte> payload = default, uint? army = null, bool take = true)
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
            float z = GetRestingHeight(id);
            if (spot is null || !Occupancy.Fits(id, spot.Value, z))
            {
                continue;
            }

            Occupancy.Add(id, spot.Value, z);

            if (take)
            {
                Taken.Add(cells[cellIndex]);
            }

            Map.Add(id, spot.Value.X, spot.Value.Y, z, dir ?? Rng.Next(256), payload, army, unique: false);
            ShadePickup(id, spot.Value.X, spot.Value.Y);
            return (spot.Value, cells[cellIndex]);
        }

        return null;
    }

    /// <summary>
    /// Lays the shadows under a pickup at the count its kind carries, rounded at random.
    /// </summary>
    public void ShadePickup(KnownObject id, float x, float y)
    {
        var category = id.PickupCategory;
        float rate = ShadowsPerItem.GetValueOrDefault(id, category is null ? 0 : ShadowsPerPickup.GetValueOrDefault(category.Value));
        for (int shadow = (int)MathF.Floor(rate + Rng.NextSingle()); shadow > 0; shadow--)
        {
            float sx = x + (Rng.NextSingle() * 2 - 1) * ShadowSpread.Width, sy = y + (Rng.NextSingle() * 2 - 1) * ShadowSpread.Height;
            if (Layout.Cells.ContainsKey(Layout.Lattice.GetCell(sx, sy)) && !IsInWall(new PointF(sx, sy), 0))
            {
                Map.Add(KnownObject.EmbeddedShadow, sx, sy, 0, 0, unique: false);
            }
        }
    }

    /// <summary>
    /// Returns the height an object stands at, raised for a piece linked below it, such as a pickup's glow, to rest on the floor.
    /// </summary>
    public float GetRestingHeight(KnownObject id) => Objects.TryFind(id, out var obj) && obj.Link != 0 && obj.LinkOffset.Z < 0 ? 1 - obj.LinkOffset.Z : 0;

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
    public void AddWallBox(float x, float y, KnownObject id)
    {
        if (!Objects.TryFind(id, out var obj))
        {
            return;
        }

        var bucket = new Cell((int)MathF.Floor(x / 100), (int)MathF.Floor(y / 100));
        if (!_wallBoxes.TryGetValue(bucket, out var list))
        {
            list = [];
            _wallBoxes[bucket] = list;
        }

        list.Add(new CollisionBox(x, y, obj.Size.X, obj.Size.Y));
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

    // Step to a side neighbor across no wall, or to a corner neighbor through either side neighbor
    private bool IsOpenStep(Cell from, Cell to)
    {
        int du = to.U - from.U;
        int dv = to.V - from.V;
        if (Math.Abs(du) + Math.Abs(dv) == 1)
        {
            return !ShowsWallBetween(from, to);
        }

        var first = new Cell(from.U + du, from.V);
        var second = new Cell(from.U, from.V + dv);
        return Layout.Cells.ContainsKey(first) && !ShowsWallBetween(from, first) && !ShowsWallBetween(first, to)
               || Layout.Cells.ContainsKey(second) && !ShowsWallBetween(from, second) && !ShowsWallBetween(second, to);
    }

    private bool ShowsWallBetween(Cell cell, Cell neighbor)
    {
        foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
        {
            if (next == neighbor)
            {
                return Layout.Edges.TryGetValue(edge, out var kind) && kind.ShowsWall;
            }
        }

        return true;
    }

    private bool IsBlockingEdge(EdgeKey k) => Layout.Edges.TryGetValue(k, out var kind) && kind.BlocksWalking;

    /// <summary>
    /// Returns the cells from a cell back to the start down the walking distances.
    /// </summary>
    public IEnumerable<Cell> TraceWalk(Cell from)
    {
        var cell = from;
        while (Distance.TryGetValue(cell, out int distance) && distance > 0)
        {
            yield return cell;
            Cell? next = null;
            foreach (var (neighbor, _) in EdgeKey.GetNeighbors(cell))
            {
                if (!Distance.TryGetValue(neighbor, out int step) || step >= distance)
                    continue;
                
                next = neighbor;
                break;
            }

            if (next is null)
            {
                yield break;
            }

            cell = next.Value;
        }
    }

    /// <summary>
    /// Returns the walking distance of every reachable cell from a cell, in cell steps, with every door open.
    /// </summary>
    public Dictionary<Cell, int> MeasureDistances(Cell start) => Layout.MeasureWalk(start, _blocked);
}
