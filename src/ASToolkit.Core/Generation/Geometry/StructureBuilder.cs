using System.Drawing;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Provides the walls, floors, fake doors and fences of a level.
/// </summary>
public static class StructureBuilder
{
    private const int RaisedDoubleDoorZ = -70;
    private const int RaisedSmallDoorZ = -63;
    private const float StuckDoorChance = 0.5f;
    private const float MinFakeDoorGap = 160;
    private static readonly (int Min, int Max) StuckDoorZ = (13, 35);
    private static readonly int[] LabWallDirs = [0, 0, 0, 0, 0, 0, 128, 128, 128, 64, 64, 32];
    private static readonly int[] MilitaryWallDirs = [0, 0, 0, 0, 0, 85, 85, 170];
    private static readonly int[] BeamDirs = [128, 128, 0];
    private static readonly int[] CornerDirs = [0, 0, 85];

    /// <summary>
    /// Places the lattice with every cell at positive map coordinates and lab floors on the ground tile phase.
    /// </summary>
    public static void PlaceLattice(FineLayout layout, LevelTheme theme)
    {
        (float sx, float sy) = theme == LevelTheme.Lab ? LevelExtractor.LabStep : LevelExtractor.MilitaryStep;
        var probe = new Lattice(sx, sy);
        
        var points = layout.Cells.Keys
            .Select(c => probe.ToWorld(c.U, c.V))
            .ToArray();
        
        float ox = -points.Min(p => p.X) + 500;
        float oy = -points.Min(p => p.Y) + 500;
        if (theme == LevelTheme.Lab)
        {
            ox = 40 * MathF.Ceiling(ox / 40);
            oy = 28 * MathF.Ceiling(oy / 28);
            if (((long)MathF.Floor((ox + 40) / 40) + (long)MathF.Floor(oy / 28)) % 2 == 0)
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
        var doors = layout.Edges
            .Where(p => p.Value is 'D' or 'E' or 'e' or 'O')
            .Select(p => GetEdgeCenter(layout.Lattice, p.Key))
            .ToList();

        foreach (var candidate in candidates.Where(k => !HasMeetingWall(layout.Edges, k)))
        {
            if (count == 0)
            {
                break;
            }

            var center = GetEdgeCenter(layout.Lattice, candidate);
            if (doors.Any(door => MathF.Sqrt((door.X - center.X) * (door.X - center.X) + (door.Y - center.Y) * (door.Y - center.Y)) < MinFakeDoorGap))
            {
                continue;
            }

            count--;
            doors.Add(center);
            var previous = GetPreviousEdge(candidate);
            if (!military && candidateSet.Contains(previous) && !HasMeetingWall(layout.Edges, previous) && !layout.FakeDoors.ContainsKey(previous) && rng.NextSingle() < 0.5f)
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
            (float x, float y) = layout.Lattice.GetCenter(cell);
            map.Add(KnownObject.BasementFloor, x, y, 0, rng.Next(256));
        }

        var spawnWalls = new Dictionary<EdgeKey, int>();
        var skip = layout.FakeDoors
            .Where(p => p.Value)
            .Select(p => GetPreviousEdge(p.Key))
            .ToHashSet();

        foreach ((var piece, bool isDouble) in GetArchPieces(edges))
        {
            ((float x, float y), bool slash) = GetLabPosition(layout.Lattice, piece);
            map.Add(isDouble ? KnownObject.DoubleDoorWall : KnownObject.SmallDoorWall, x, y, 0, slash ? 0 : 128);
            map.Add(isDouble ? KnownObject.DoubleDoorFrame : KnownObject.SmallDoorFrame, x, y, isDouble ? RaisedDoubleDoorZ : RaisedSmallDoorZ, slash ? 0 : 128);
        }

        foreach ((var edge, char kind) in edges)
        {
            ((float x, float y), bool slash) = GetLabPosition(layout.Lattice, edge);
            if (kind is 'e' or 'O' || skip.Contains(edge))
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
                        int z = rng.NextSingle() < StuckDoorChance ? -rng.Next(StuckDoorZ.Min, StuckDoorZ.Max + 1) : 0;
                        map.Add(isDouble ? KnownObject.DoubleDoorFrame : KnownObject.SmallDoorFrame, x, y, z, slash ? 0 : 128);
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
            (float x, float y) = layout.Lattice.ToWorld(a + 0.25f, b + 0.25f);
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
        var arches = edges.Where(p => p.Value == 'O').Select(p => p.Key).ToHashSet();
        foreach (var edge in edges.Keys)
        {
            float x, y;
            KnownObject id;
            int dir;
            if (edge.Axis == 'v' && IsEven(edge.A))
            {
                // A wall piece spans the two unit edges around this one, and arches stay open
                if (CrossesArch(arches, 'v', edge.A, edge.B))
                {
                    continue;
                }

                (x, y) = l.ToWorld(edge.A + 1, edge.B);
                id = KnownObject.MilitaryWallSlash;
                dir = 128;
            }
            else if (edge.Axis == 'u' && IsEven(edge.B))
            {
                if (CrossesArch(arches, 'u', edge.A, edge.B))
                {
                    continue;
                }

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
                (float x, float y) = l.ToWorld(a, b - 1);
                map.Add(KnownObject.MilitaryCorner, x, y, 0, CornerDirs[rng.Next(CornerDirs.Length)]);
            }
        }

        // 503 tiles use their own 80 by 56 lattice and reach under the walls
        var centers = layout.Cells.Keys.Select(l.GetCenter).ToArray();
        int i0 = (int)MathF.Floor((centers.Min(p => p.X) - 160) / 80);
        int i1 = (int)MathF.Floor((centers.Max(p => p.X) + 160) / 80);
        int j0 = (int)MathF.Floor((centers.Min(p => p.Y) - 112) / 56);
        int j1 = (int)MathF.Floor((centers.Max(p => p.Y) + 112) / 56);
        for (int index = i0; index <= i1; index++)
        {
            for (int innerIndex = j0; innerIndex <= j1; innerIndex++)
            {
                if (((index + innerIndex) & 1) != 0)
                {
                    continue;
                }

                float x = 80 * index;
                float y = 56 * innerIndex;
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
            ((float x, float y), int dir) = unitEdge.Axis == 'v' ? (lattice.ToWorld(unitCells * unitEdge.A + 1, unitCells * unitEdge.B), 128) : (lattice.ToWorld(unitCells * unitEdge.A, unitCells * unitEdge.B + 1), 0);
            map.Add(KnownObject.FenceEmitter, x, y, 0, dir);
            map.Add(KnownObject.Fence, x, y - 14, 0, dir);
        }
    }

    // Returns whether a wall piece on the given edge crosses an open arch
    private static bool CrossesArch(HashSet<EdgeKey> arches, char axis, int a, int b)
    {
        for (int offset = -1; offset <= 2; offset++)
        {
            var key = axis == 'v' ? new EdgeKey('v', a + offset, b) : new EdgeKey('u', a, b + offset);
            if (arches.Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    // An open doorway is a pair of edges under one sunken double frame, or a single edge under a single one
    private static IEnumerable<(EdgeKey Piece, bool IsDouble)> GetArchPieces(IReadOnlyDictionary<EdgeKey, char> edges)
    {
        var lines = new Dictionary<(char Axis, int Line), List<EdgeKey>>();
        foreach ((var key, char kind) in edges)
        {
            if (kind != 'O')
            {
                continue;
            }

            var line = key.Axis == 'v' ? ('v', key.B) : ('u', key.A);
            if (lines.TryGetValue(line, out var run))
            {
                run.Add(key);
            }
            else
            {
                lines[line] = [key];
            }
        }

        foreach (var line in lines.Values)
        {
            var sorted = line.OrderBy(GetLinePosition).ToArray();
            int runStart = 0;
            for (int index = 1; index <= sorted.Length; index++)
            {
                if (index < sorted.Length && GetLinePosition(sorted[index]) == GetLinePosition(sorted[index - 1]) + 1)
                {
                    continue;
                }

                for (int pieceIndex = runStart; pieceIndex < index; pieceIndex += 2)
                {
                    yield return pieceIndex + 1 < index ? (sorted[pieceIndex + 1], true) : (sorted[pieceIndex], false);
                }

                runStart = index;
            }
        }
    }

    private static int GetLinePosition(EdgeKey edge) => edge.Axis == 'v' ? edge.A : edge.B;

    private static PointF GetEdgeCenter(Lattice lattice, EdgeKey edge) =>
        edge.Axis == 'v' ? lattice.ToWorld(edge.A + 0.5f, edge.B) : lattice.ToWorld(edge.A, edge.B + 0.5f);

    /// <summary>
    /// Returns the map position of a lab wall piece on an edge and whether it runs along '/'.
    /// </summary>
    public static (PointF Position, bool Slash) GetLabPosition(Lattice l, EdgeKey k) =>
        k.Axis == 'v' ? (l.ToWorld(k.A + 0.25f, k.B + 0.25f), true) : (l.ToWorld(k.A - 0.25f, k.B + 0.75f), false);

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

    // Posts go where walls meet but don't run straight through
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
