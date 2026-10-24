using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules;

/// <summary>
/// Provides the measurement of a level for the rules.
/// </summary>
public static class LevelMeasurer
{
    private const int MaxClosetCells = 60;

    /// <summary>
    /// Measures a level from its map, script and tour.
    /// </summary>
    public static LevelMeasurement Measure(MapResourceBuilder map, LevelScriptSummary script, RouteAnalysis analysis, ObjectTable objects)
    {
        var grid = analysis.Grid;
        int placed = 0;
        int spawned = 0;
        int breakables = 0;
        foreach (var sprite in map.Sprites)
        {
            if (KnownObject.Monsters.Contains(sprite.Id) || KnownObject.Bosses.Contains(sprite.Id))
            {
                placed += objects.TryFind(sprite.Id, out var obj) ? obj.MaxHp : 0;
            }
            else if (sprite.Id == KnownObject.Spawner)
            {
                var program = SpawnerProgram.Parse(sprite.Payload);
                if (program is not null)
                {
                    spawned += program.Monsters.Sum(m => objects.TryFind(m, out var obj) ? obj.MaxHp : 0);
                }
            }

            if (sprite.Id.IsDecor && objects.TryFind(sprite.Id, out var decor) && decor.IsHittable)
            {
                breakables++;
            }
        }

        (var closets, int shortcuts) = FindBreakableWallUses(map, grid, analysis);
        return new LevelMeasurement
        {
            Style = DetectStyle(script, analysis),
            BlockedShare = grid.BlockedCells.Count / (float)Math.Max(1, grid.Cells.Count + grid.BlockedCells.Count),
            Breakables = breakables,
            HitPoints = placed + spawned,
            SpawnedShare = spawned / (float)Math.Max(1, placed + spawned),
            SecretClosets = closets,
            Shortcuts = shortcuts
        };
    }

    private static LevelStyle DetectStyle(LevelScriptSummary script, RouteAnalysis analysis)
    {
        if (analysis.Gating.FencedOffShare is not null)
        {
            return LevelStyle.Fenced;
        }

        bool hasDoors = analysis.Grid.Edges.Values.Any(v => v == 'D');
        bool hasBreaker = analysis.Elements.Any(e => e.Kind == ElementKind.Breaker);
        if (hasDoors && hasBreaker && script.Blackout is not null)
        {
            return LevelStyle.Blackout;
        }

        return hasDoors && hasBreaker && script.DoorsLocked ? LevelStyle.LockedDoors : LevelStyle.Ungated;
    }

    private static (List<SecretCloset> Closets, int Shortcuts) FindBreakableWallUses(MapResourceBuilder map, RouteGrid grid, RouteAnalysis analysis)
    {
        if (analysis.Tour.Count == 0)
        {
            return ([], 0);
        }

        var lattice = grid.Lattice;
        var breakableWalls = map.Sprites
            .Where(s => s.Id is KnownObject.BreakableWall or KnownObject.BreakableWall2)
            .ToArray();
        
        var itemsByCell = map.Sprites
            .Where(s => ItemCategorizer.GetCategory(s.Id) is not null || (s.Id == KnownObject.Crate && KnownObject.GetCrateItem(s.Payload) != 0))
            .ToLookup(s => lattice.GetCell(s.Position.X, s.Position.Y), s => s.Id == KnownObject.Crate ? KnownObject.GetCrateItem(s.Payload) : s.Id);
        
        var items = itemsByCell
            .Select(g => g.Key)
            .ToHashSet();
        
        var progressByCell = analysis.Elements
            .Where(e => e.Kind is ElementKind.Pickup or ElementKind.Crate)
            .ToLookup(e => lattice.GetCell(e.X, e.Y), e => e.Progress);
        
        var spawners = map.Sprites
            .Where(s => s.Id == KnownObject.Spawner)
            .Select(s => lattice.GetCell(s.Position.X, s.Position.Y))
            .ToHashSet();
        
        var open = Flood(grid, [analysis.Tour[0]], IsClosed);
        var counted = new HashSet<Cell>();
        var shortcutWalls = new HashSet<int>();
        var closets = new List<SecretCloset>();
        
        foreach ((var edge, char kind) in grid.Edges)
        {
            if (kind != 'S')
            {
                continue;
            }

            (float mu, float mv) = edge.Axis == 'v' ? (edge.A + 0.5f, (float)edge.B) : (edge.A, edge.B + 0.5f);
            (float mx, float my) = lattice.ToWorld(mu, mv);
            int wall = Array.FindIndex(breakableWalls, w => Math.Abs(w.Position.X - mx) < 50 && Math.Abs(w.Position.Y - my) < 50);
            if (wall < 0)
            {
                continue;
            }

            var (first, second) = edge.Cells;
            bool firstOpen = open.Contains(first);
            bool secondOpen = open.Contains(second);
            if (firstOpen && secondOpen)
            {
                shortcutWalls.Add(wall);
                continue;
            }

            var inside = firstOpen ? second : first;
            if (firstOpen == secondOpen || !grid.Cells.Contains(inside) || counted.Contains(inside))
            {
                continue;
            }

            var pocket = Flood(grid, [inside], IsClosed);
            counted.UnionWith(pocket);
            if (pocket.Count <= MaxClosetCells && pocket.Overlaps(items) && !pocket.Overlaps(spawners))
            {
                var cells = pocket.OrderBy(c => c.U).ThenBy(c => c.V).ToArray();
                float progress = cells.SelectMany(c => progressByCell[c]).DefaultIfEmpty(0).Average();
                closets.Add(new SecretCloset(progress, cells.SelectMany(c => itemsByCell[c]).ToArray()));
            }
        }

        return (closets, shortcutWalls.Count);
        
        bool IsClosed(EdgeKey k) => grid.Edges.TryGetValue(k, out char kind) && (RouteGrid.IsBlocking(kind) || kind == 'S');
    }

    private static HashSet<Cell> Flood(RouteGrid grid, IEnumerable<Cell> sources, Func<EdgeKey, bool> isClosed)
    {
        var seen = new HashSet<Cell>();
        var queue = new Queue<Cell>();
        foreach (var source in sources)
        {
            if (grid.Cells.Contains(source) && seen.Add(source))
            {
                queue.Enqueue(source);
            }
        }

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var (n, k) in EdgeKey.GetNeighbors(c))
            {
                if (grid.Cells.Contains(n) && !isClosed(k) && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }
}
