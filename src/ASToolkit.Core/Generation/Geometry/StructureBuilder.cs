using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Provides the walls, floors, fake doors and fences of a level.
/// </summary>
public static class StructureBuilder
{
    private static readonly int[] LabWallDirs = [0, 0, 0, 0, 0, 0, 128, 128, 128, 64, 64, 32];
    private static readonly int[] MilitaryWallDirs = [0, 0, 0, 0, 0, 85, 85, 170];
    private static readonly int[] BeamDirs = [128, 128, 0];
    private static readonly int[] CornerDirs = [0, 0, 85];

    /// <summary>
    /// Places the lattice with every cell at positive map coordinates and lab floors on the ground tile phase.
    /// </summary>
    public static void PlaceLattice(FineLayout layout, LevelTheme theme)
    {
        (double sx, double sy) = theme == LevelTheme.Lab ? LevelExtractor.LabStep : LevelExtractor.MilitaryStep;
        var probe = new Lattice(sx, sy);
        
        var points = layout.Cells.Keys
            .Select(c => probe.ToWorld(c.U, c.V))
            .ToArray();
        
        double ox = -points.Min(p => p.X) + 500;
        double oy = -points.Min(p => p.Y) + 500;
        if (theme == LevelTheme.Lab)
        {
            ox = 40 * Math.Ceiling(ox / 40);
            oy = 28 * Math.Ceiling(oy / 28);
            if (((long)Math.Floor((ox + 40) / 40) + (long)Math.Floor(oy / 28)) % 2 == 0)
            {
                oy += 28;
            }
        }

        layout.Lattice = new Lattice(sx, sy, ox, oy);
    }

    /// <summary>
    /// Turns two to five hall walls that face the camera from the hall into closed fake doors.
    /// </summary>
    public static void AddFakeDoors(Random rng, FineLayout layout, LayoutPlan plan)
    {
        var halls = plan.Stations.Where(s => s.IsHall).Select(s => s.Id).ToHashSet();
        bool military = plan.Theme.Theme == LevelTheme.Military;
        
        var candidates = layout.Edges
            .Where(p => p.Value == 'W')
            .Select(p => p.Key)
            .Where(k =>
            {
                var (a, b) = k.Cells;
                bool ia = layout.Cells.TryGetValue(a, out int ra);
                bool ib = layout.Cells.TryGetValue(b, out int rb);
                bool nearIsFloor = k.Axis == 'v' ? ib : ia;
                return ia != ib && nearIsFloor && halls.Contains(ia ? ra : rb) && (!military || (k.Axis == 'v' ? IsEven(k.A) : IsEven(k.B)));
            })
            .OrderBy(k => k.Axis)
            .ThenBy(k => k.A)
            .ThenBy(k => k.B)
            .ToArray();
        
        rng.Shuffle(candidates);
        var candidateSet = candidates.ToHashSet();
        int count = rng.Next(2, 6);
        foreach (var candidate in candidates.Where(k => !HasMeetingWall(layout.Edges, k)).Take(count))
        {
            var previous = GetPreviousEdge(candidate);
            if (!military && candidateSet.Contains(previous) && !HasMeetingWall(layout.Edges, previous) && !layout.FakeDoors.ContainsKey(previous) && rng.NextDouble() < 0.5)
            {
                layout.FakeDoors[candidate] = true;
            }
            else if (!layout.FakeDoors.ContainsKey(previous))
            {
                layout.FakeDoors[candidate] = false;
            }
        }
    }

    /// <summary>
    /// Builds the lab floors, walls, doors and posts of the visible edges and returns the sprite index of each spawn wall.
    /// </summary>
    public static Dictionary<EdgeKey, int> BuildLab(Random rng, MapBuilder map, FineLayout layout, IReadOnlyDictionary<EdgeKey, char> edges)
    {
        foreach (var cell in layout.Cells.Keys.OrderBy(c => c.U).ThenBy(c => c.V))
        {
            (double x, double y) = layout.Lattice.GetCenter(cell);
            map.Add(KnownObject.BasementFloor, x, y, 0, rng.Next(256));
        }

        var spawnWalls = new Dictionary<EdgeKey, int>();
        var skip = layout.FakeDoors.Where(p => p.Value).Select(p => GetPreviousEdge(p.Key)).ToHashSet();
        foreach ((var edge, char kind) in edges)
        {
            ((double x, double y), bool slash) = GetLabPosition(layout.Lattice, edge);
            if (kind == 'e' || skip.Contains(edge))
            {
                continue;
            }

            switch (kind)
            {
                case 'E':
                    map.Add(KnownObject.DoubleDoorWall, x, y, 0, slash ? 0 : 128);
                    map.Add(KnownObject.DoubleDoor, x, y, 0, slash ? 0 : 64);
                    break;
                case 'D':
                    map.Add(KnownObject.SmallDoorWall, x, y, 0, slash ? 0 : 128);
                    map.Add(KnownObject.SmallDoor, x, y, 0, slash ? 0 : 128);
                    break;
                case 'S':
                    map.Add(KnownObject.BreakableWall, x, y, 0, slash ? 0 : 128);
                    break;
                case 'T':
                    spawnWalls[edge] = slash ? map.Add(KnownObject.SpawnWallSlash, x, y) : map.Add(KnownObject.SpawnWallBack, x, y, 0, 64);
                    break;
                default:
                    if (layout.FakeDoors.TryGetValue(edge, out bool isDouble))
                    {
                        map.Add(isDouble ? KnownObject.DoubleDoorWall : KnownObject.SmallDoorWall, x, y, 0, slash ? 0 : 128);
                        map.Add(isDouble ? KnownObject.DoubleDoorFrame : KnownObject.SmallDoorFrame, x, y, 0, slash ? 0 : 128);
                    }
                    else
                    {
                        map.Add(slash ? KnownObject.LabWallSlash : KnownObject.LabWallBack, x, y, 0, LabWallDirs[rng.Next(LabWallDirs.Length)]);
                    }

                    break;
            }
        }

        foreach ((int a, int b) in FindPosts(edges))
        {
            (double x, double y) = layout.Lattice.ToWorld(a + .25, b + .25);
            map.Add(KnownObject.LabBeam, x, y, 0, BeamDirs[rng.Next(BeamDirs.Length)]);
        }

        return spawnWalls;
    }

    /// <summary>
    /// Builds the military walls, posts and floor tiles of the visible edges.
    /// </summary>
    public static void BuildMilitary(Random rng, MapBuilder map, FineLayout layout, IReadOnlyDictionary<EdgeKey, char> edges)
    {
        var l = layout.Lattice;
        foreach (var edge in edges.Keys)
        {
            double x, y;
            KnownObject id;
            int dir;
            if (edge.Axis == 'v' && IsEven(edge.A))
            {
                (x, y) = l.ToWorld(edge.A + 1, edge.B);
                id = KnownObject.MilitaryWallSlash;
                dir = 128;
            }
            else if (edge.Axis == 'u' && IsEven(edge.B))
            {
                (x, y) = l.ToWorld(edge.A, edge.B + 1);
                id = KnownObject.MilitaryWallBack;
                dir = 0;
            }
            else
            {
                continue;
            }

            if (layout.FakeDoors.ContainsKey(edge))
            {
                map.Add(KnownObject.MilitaryDoorWall, x, y, 0, dir);
            }
            else
            {
                map.Add(id, x, y, 0, MilitaryWallDirs[rng.Next(MilitaryWallDirs.Length)]);
            }
        }

        foreach ((int a, int b) in FindPosts(edges))
        {
            if (IsEven(a) && IsEven(b))
            {
                (double x, double y) = l.ToWorld(a, b - 1);
                map.Add(KnownObject.MilitaryCorner, x, y, 0, CornerDirs[rng.Next(CornerDirs.Length)]);
            }
        }

        // 503 tiles have their own 80 by 56 lattice and reach under the walls.
        var centers = layout.Cells.Keys.Select(l.GetCenter).ToArray();
        int i0 = (int)Math.Floor((centers.Min(p => p.X) - 160) / 80);
        int i1 = (int)Math.Floor((centers.Max(p => p.X) + 160) / 80);
        int j0 = (int)Math.Floor((centers.Min(p => p.Y) - 112) / 56);
        int j1 = (int)Math.Floor((centers.Max(p => p.Y) + 112) / 56);
        for (int index = i0; index <= i1; index++)
        {
            for (int innerIndex = j0; innerIndex <= j1; innerIndex++)
            {
                if (((index + innerIndex) & 1) != 0)
                {
                    continue;
                }

                double x = 80 * index;
                double y = 56 * innerIndex;
                bool covers = false;
                foreach (int dx in new[] { -80, -40, 0, 40, 80 })
                {
                    foreach (int dy in new[] { -56, -28, 0, 28, 56 })
                    {
                        covers |= layout.Cells.ContainsKey(l.GetCell(x + dx, y + dy));
                    }
                }

                if (covers)
                {
                    map.Add(KnownObject.MilitaryFloor, x, y, 0, rng.Next(256));
                }
            }
        }
    }

    /// <summary>
    /// Places the 653 emitter pair and the 164 fence above it in each edge of a fence link.
    /// </summary>
    public static void BuildFence(MapBuilder map, Lattice lattice, IEnumerable<EdgeKey> unitEdges, int unitCells)
    {
        foreach (var unitEdge in unitEdges)
        {
            ((double x, double y), int dir) = unitEdge.Axis == 'v' ? (lattice.ToWorld(unitCells * unitEdge.A + 1, unitCells * unitEdge.B), 128) : (lattice.ToWorld(unitCells * unitEdge.A, unitCells * unitEdge.B + 1), 0);
            map.Add(KnownObject.FenceEmitter, x, y, 0, dir);
            map.Add(KnownObject.Fence, x, y - 14, 0, dir);
        }
    }

    /// <summary>
    /// Returns the map position of a lab wall piece on an edge and whether it runs along '/'.
    /// </summary>
    public static ((double X, double Y) Position, bool Slash) GetLabPosition(Lattice l, EdgeKey k) =>
        k.Axis == 'v' ? (l.ToWorld(k.A + .25, k.B + .25), true) : (l.ToWorld(k.A - .25, k.B + .75), false);

    /// <summary>
    /// Returns the edge before an edge along its own direction.
    /// </summary>
    public static EdgeKey GetPreviousEdge(EdgeKey k) => k.Axis == 'v' ? new EdgeKey('v', k.A - 1, k.B) : new EdgeKey('u', k.A, k.B - 1);

    private static bool IsEven(int value) => (value & 1) == 0;

    private static bool HasMeetingWall(IReadOnlyDictionary<EdgeKey, char> edges, EdgeKey k)
    {
        foreach ((int a, int b) in k.Axis == 'u' ? new[] { (k.A, k.B), (k.A, k.B + 1) } : new[] { (k.A, k.B), (k.A + 1, k.B) })
        {
            bool meets = k.Axis == 'u'
                ? edges.ContainsKey(new EdgeKey('v', a - 1, b)) || edges.ContainsKey(new EdgeKey('v', a, b))
                : edges.ContainsKey(new EdgeKey('u', a, b - 1)) || edges.ContainsKey(new EdgeKey('u', a, b));
            if (meets)
            {
                return true;
            }
        }

        return false;
    }

    // Vertices where walls meet without running straight through.
    private static (int A, int B)[] FindPosts(IReadOnlyDictionary<EdgeKey, char> edges)
    {
        var vertices = new List<(int, int)>();
        var seen = new HashSet<(int, int)>();
        foreach (var edge in edges.Keys)
        {
            foreach (var vertex in edge.Axis == 'v' ? new[] { (edge.A, edge.B), (edge.A + 1, edge.B) } : new[] { (edge.A, edge.B), (edge.A, edge.B + 1) })
            {
                if (seen.Add(vertex))
                {
                    vertices.Add(vertex);
                }
            }
        }

        var posts = new List<(int, int)>();
        foreach ((int a, int b) in vertices)
        {
            bool w1 = edges.ContainsKey(new EdgeKey('v', a - 1, b));
            bool w2 = edges.ContainsKey(new EdgeKey('v', a, b));
            bool s1 = edges.ContainsKey(new EdgeKey('u', a, b - 1));
            bool s2 = edges.ContainsKey(new EdgeKey('u', a, b));
            bool straight = (w1 && w2 && !s1 && !s2) || (s1 && s2 && !w1 && !w2);
            if ((w1 || w2 || s1 || s2) && !straight)
            {
                posts.Add((a, b));
            }
        }

        return posts.ToArray();
    }
}
