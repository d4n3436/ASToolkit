using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Represents a layout on the theme's cell lattice. Edge kinds are 'W' wall, 'D' single door, 'E' and 'e' the two halves of
/// a double door, 'S' breakable wall and 'T' spawn wall.
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
    public required IReadOnlyDictionary<EdgeKey, char> Edges { get; init; }

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
    /// Builds the fine layout of a plan.
    /// </summary>
    public static FineLayout Build(LayoutPlan plan)
    {
        int n = plan.Theme.UnitCells;
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
        var kinds = new Dictionary<EdgeKey, char>();
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
                        kinds[fineCell] = link.Kind == LinkKind.SecretWall ? 'S' : 'T';
                    }

                    break;
                case LinkKind.Door when fine.Length == 2:
                    kinds[fine[1]] = 'E';
                    kinds[fine[0]] = 'e';
                    break;
                case LinkKind.Door:
                    foreach (var innerFineCell in fine)
                    {
                        kinds[innerFineCell] = 'D';
                    }

                    break;
                default:
                    opened.UnionWith(fine);
                    break;
            }
        }

        var edges = new Dictionary<EdgeKey, char>();
        foreach (var cell in cells.Keys)
        {
            foreach (var (_, k) in EdgeKey.GetNeighbors(cell))
            {
                var (a, b) = k.Cells;
                int ra = cells.GetValueOrDefault(a, -1);
                int rb = cells.GetValueOrDefault(b, -1);
                if (ra != rb && !opened.Contains(k))
                {
                    edges[k] = kinds.GetValueOrDefault(k, 'W');
                }
            }
        }

        return new FineLayout { Cells = cells, Edges = edges, LinkEdges = linkEdges, Lattice = new Lattice(0, 0) };

        EdgeKey[] Split(EdgeKey k) => Enumerable.Range(0, n)
            .Select(i => k.Axis == 'v' ? new EdgeKey('v', n * k.A + i, n * k.B) : new EdgeKey('u', n * k.A, n * k.B + i)).ToArray();
    }
}
