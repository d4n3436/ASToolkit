using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Represents a layout on the wall set's cell lattice.
/// </summary>
public sealed class FineLayout
{
    private Dictionary<int, Cell[]> _regions = [];
    private bool _regionsBuilt;

    /// <summary>
    /// Gets the region of every floor cell.
    /// </summary>
    public required IReadOnlyDictionary<Cell, int> Cells { get; init; }

    /// <summary>
    /// Gets the wall edges by kind.
    /// </summary>
    public required IReadOnlyDictionary<EdgeKey, EdgeKind> Edges { get; init; }

    /// <summary>
    /// Gets the edges of each link, by link index.
    /// </summary>
    public required IReadOnlyList<EdgeKey[]> LinkEdges { get; init; }

    /// <summary>
    /// Gets the walls that carry a fake door, with a value indicating whether the door is double.
    /// </summary>
    public Dictionary<EdgeKey, bool> FakeDoors { get; } = [];

    /// <summary>
    /// Gets or sets the lattice that places the cells on the map.
    /// </summary>
    public required Lattice Lattice { get; set; }

    /// <summary>
    /// Returns the cell on the given region's side of a link's middle doorway edge.
    /// </summary>
    /// <param name="link">The index of the link in the plan.</param>
    /// <param name="region">The region the link enters.</param>
    public Cell GetDoorwayCell(int link, int region)
    {
        var edges = LinkEdges[link];
        var (a, b) = edges[edges.Length / 2].Cells;
        return Cells.GetValueOrDefault(a, -1) == region ? a : b;
    }

    /// <summary>
    /// Returns the cells of a region.
    /// </summary>
    public IReadOnlyList<Cell> GetRegionCells(int region)
    {
        if (!_regionsBuilt)
        {
            _regions = Cells
                .GroupBy(p => p.Value)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Key).OrderBy(c => c.U).ThenBy(c => c.V).ToArray());
            _regionsBuilt = true;
        }

        return _regions.TryGetValue(region, out var cells) ? cells : [];
    }

    /// <summary>
    /// Returns whether any edge of a cell carries a wall or door.
    /// </summary>
    public bool IsWalled(Cell c) => EdgeKey.GetNeighbors(c).HasEdge(Edges);

    /// <summary>
    /// Returns the walking distance in cells from a cell to every cell reached without crossing a blocking edge or a blocked cell.
    /// </summary>
    public Dictionary<Cell, int> MeasureWalk(Cell start, IReadOnlySet<Cell> blocked)
    {
        var distance = new Dictionary<Cell, int> { [start] = 0 };
        var queue = new Queue<Cell>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        {
            foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (!Cells.ContainsKey(next) ||
                    distance.ContainsKey(next) ||
                    Edges.TryGetValue(edge, out var kind) && kind.BlocksWalking ||
                    blocked.Contains(next))
                    continue;
                
                distance[next] = distance[cell] + 1;
                queue.Enqueue(next);
            }
        }

        return distance;
    }

    /// <summary>
    /// Builds the fine layout of a plan.
    /// </summary>
    public static FineLayout Build(LayoutPlan plan)
    {
        int n = plan.Settings.UnitCells;
        var cells = new Dictionary<Cell, int>();
        foreach ((var unit, int region) in plan.Occupancy)
        {
            for (int index = 0; index < n; index++)
            {
                for (int innerIndex = 0; innerIndex < n; innerIndex++)
                {
                    cells[new Cell(n * unit.U + index, n * unit.V + innerIndex)] = region;
                }
            }
        }

        var opened = new HashSet<EdgeKey>();
        var kinds = new Dictionary<EdgeKey, EdgeKind>();
        var linkEdges = new List<EdgeKey[]>();
        foreach (var link in plan.Links)
        {
            var fine = link.Edges
                .SelectMany(Split)
                .OrderBy(k => k.A)
                .ThenBy(k => k.B)
                .ToArray();
            
            linkEdges.Add(fine);
            switch (link.Kind)
            {
                case LinkKind.SecretWall or LinkKind.SpawnWall:
                    foreach (var fineCell in fine)
                    {
                        kinds[fineCell] = link.Kind == LinkKind.SecretWall ? EdgeKind.SecretWall : EdgeKind.SpawnWall;
                    }

                    break;
                case LinkKind.Door when fine.Length == 2:
                    kinds[fine[1]] = EdgeKind.DoubleDoor;
                    kinds[fine[0]] = EdgeKind.DoubleDoorEnd;
                    break;
                case LinkKind.Door:
                    foreach (var innerFineCell in fine)
                    {
                        kinds[innerFineCell] = EdgeKind.Door;
                    }

                    break;
                case LinkKind.Arch:
                    foreach (var archFineCell in fine)
                    {
                        kinds[archFineCell] = EdgeKind.Doorway;
                    }

                    break;
                default:
                    opened.UnionWith(fine);
                    break;
            }
        }

        var edges = new Dictionary<EdgeKey, EdgeKind>();
        foreach (var jammed in plan.JammedDoors.SelectMany(Split))
        {
            kinds[jammed] = EdgeKind.ShutDoor;
        }

        foreach (var cell in cells.Keys)
        {
            foreach (var (_, k) in EdgeKey.GetNeighbors(cell))
            {
                var (a, b) = k.Cells;
                int ra = cells.GetValueOrDefault(a, -1);
                int rb = cells.GetValueOrDefault(b, -1);
                if (ra != rb && !opened.Contains(k))
                {
                    edges[k] = kinds.GetValueOrDefault(k, EdgeKind.Wall);
                }
            }
        }

        return new FineLayout { Cells = cells, Edges = edges, LinkEdges = linkEdges, Lattice = new Lattice(0, 0) };

        EdgeKey[] Split(EdgeKey k) => Enumerable.Range(0, n)
            .Select(i => k.Axis == 'v' ? new EdgeKey('v', n * k.A + i, n * k.B) : new EdgeKey('u', n * k.A, n * k.B + i)).ToArray();
    }
}
