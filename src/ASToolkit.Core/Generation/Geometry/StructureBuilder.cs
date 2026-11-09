using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Provides the floors, walls, fake doors, fences and roof of a level.
/// </summary>
public static class StructureBuilder
{
    private const int RaisedDoubleDoorZ = -70;
    private const int SunkenWallZ = -7;
    private const int RaisedSmallDoorZ = -63;
    private const float StuckDoorChance = 0.5f;
    private const float FramedShare = 0.7f;
    private const float MinFakeDoorGap = 160;
    private const int BasementRoofZ = 90;
    private const int ConcreteRoofZ = 95;
    private const int BasementFloorReach = 2;
    private const int ConcreteFloorReach = 2;
    private const float RoofMargin = 300;
    private const int ConcreteEdgingSteps = 2;
    private const float EarthStepX = 40;
    private const float EarthStepY = 28;
    private static readonly (int Min, int Max) StuckDoorZ = (13, 35);
    private const float BigHoleShare = 0.67f;
    private static readonly Dictionary<KnownObject, float> BrokenKeypadShares = new() { [KnownObject.SmallDoorFrame] = 0.97f, [KnownObject.DoubleDoorFrame] = 0.85f };

    private static readonly Dictionary<KnownObject, Dictionary<int, WallSpot>> Keypads = new()
    {
        [KnownObject.SmallDoor] = new() { [0] = new(new PointF(0, 7), 6, 0), [128] = new(new PointF(3, 1), 6, 128) },
        [KnownObject.DoubleDoor] = new() { [0] = new(new PointF(-2, 5), 4, 0), [64] = new(new PointF(0, 0), 4, 128) },
        [KnownObject.SmallDoorFrame] = new() { [0] = new(new PointF(-1, 1), 4, 0), [128] = new(new PointF(5, 4), 10, 128) },
        [KnownObject.DoubleDoorFrame] = new() { [0] = new(new PointF(-2, 6), 5, 0), [128] = new(new PointF(3, 2), 10, 128) }
    };

    private static readonly Dictionary<KnownObject, DoorFixture> DoorFixtures = new()
    {
        [KnownObject.SmallDoorWall] = new(KnownObject.ElectricPanel, 0.12f, new() { [0] = new(new PointF(-24, 27), 0, 128), [128] = new(new PointF(-40, -19), 0, 0) }),
        [KnownObject.DoubleDoorWall] = new(KnownObject.WallObjects, 0.1f, new() { [0] = new(new PointF(35, -11), 45, 128), [128] = new(new PointF(21, 28), 55, 102) })
    };

    private const float ClosetShadowShare = 0.8f;
    private static readonly (int Min, int Max) ClosetShadows = (7, 27);
    private const float ClosetShadowDepth = 0.75f;
    private const float HoleRubbleShare = 0.25f;
    private static readonly (int Min, int Max) HoleRubble = (25, 40);
    private const float RubbleZ = 25;
    private const float RubbleSpread = 8;

    private static readonly int[] BasementWallDirs = [0, 0, 0, 0, 0, 0, 128, 128, 128, 64, 64, 32];
    private static readonly int[] ConcreteWallDirs = [0, 0, 0, 0, 0, 85, 85, 170];
    private static readonly int[] BeamDirs = [128, 128, 0];
    private static readonly int[] CornerDirs = [0, 0, 85];
    private static readonly SizeF BasementRoofShift = new(0, 17);
    private static readonly SizeF[] ConcreteCellFill = [new(-5, 0), new(5, 0), new(0, -4), new(0, 4)];
    private static readonly (int U, int V)[] Around = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)];

    /// <summary>
    /// Places the lattice with every cell at positive map coordinates and basement floors on the ground tile phase.
    /// </summary>
    public static void PlaceLattice(FineLayout layout, WallSet wallSet)
    {
        (float sx, float sy) = wallSet == WallSet.Basement ? Lattice.BasementStep : Lattice.ConcreteStep;
        var probe = new Lattice(sx, sy);
        
        var points = layout.Cells.Keys
            .Select(c => probe.ToWorld(c.U, c.V))
            .ToArray();
        
        float ox = -points.Min(p => p.X) + 500;
        float oy = -points.Min(p => p.Y) + 500;
        if (wallSet == WallSet.Basement)
        {
            ox = sx * MathF.Ceiling(ox / sx);
            oy = sy * MathF.Ceiling(oy / sy);
            if (((long)MathF.Floor((ox + sx) / sx) + (long)MathF.Floor(oy / sy)) % 2 == 0)
            {
                oy += sy;
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
        bool concrete = plan.Settings.WallSet == WallSet.Concrete;
        
        var candidates = layout.Edges
            .Where(p => p.Value == EdgeKind.Wall)
            .Select(p => p.Key)
            .Where(k =>
            {
                var (a, b) = k.Cells;
                bool ia = layout.Cells.TryGetValue(a, out int ra);
                bool ib = layout.Cells.TryGetValue(b, out int rb);
                bool nearIsFloor = k.Axis == 'v' ? ib : ia;
                return ia != ib && nearIsFloor && halls.Contains(ia ? ra : rb) && (!concrete || (k.Axis == 'v' ? IsEven(k.A) : IsEven(k.B)));
            })
            .OrderBy(k => k.Axis)
            .ThenBy(k => k.A)
            .ThenBy(k => k.B)
            .ToArray();
        
        rng.Shuffle(candidates);
        var candidateSet = candidates.ToHashSet();
        int count = rng.Next(2, 6);
        var doors = layout.Edges
            .Where(p => p.Value is EdgeKind.Door or EdgeKind.DoubleDoor or EdgeKind.DoubleDoorEnd or EdgeKind.Doorway)
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
            if (!concrete && candidateSet.Contains(previous) && !HasMeetingWall(layout.Edges, previous) && !layout.FakeDoors.ContainsKey(previous) && rng.NextSingle() < 0.5f)
            {
                layout.FakeDoors[candidate] = true;
            }
            else if (!layout.FakeDoors.ContainsKey(previous))
            {
                layout.FakeDoors[candidate] = false;
            }
        }
    }

    private static void AddDoor(Random rng, MapBuilder map, KnownObject door, float x, float y, float z, bool slash, bool locked)
    {
        int dir = door == KnownObject.DoubleDoor ? slash ? 0 : 64 : slash ? 0 : 128;
        map.Add(door, x, y, z, dir);
        var keypad = Keypads[door][dir];
        bool working = door is KnownObject.SmallDoor or KnownObject.DoubleDoor;
        if (working || rng.NextSingle() < BrokenKeypadShares[door])
        {
            map.Add(working ? locked ? KnownObject.KeypadOff : KnownObject.KeypadOn : KnownObject.BrokenKeypad, x + keypad.Offset.X, y + keypad.Offset.Y, keypad.Z, keypad.Dir);
        }

        var fixture = DoorFixtures[door is KnownObject.DoubleDoor or KnownObject.DoubleDoorFrame ? KnownObject.DoubleDoorWall : KnownObject.SmallDoorWall];
        var spot = fixture.Spots[slash ? 0 : 128];
        if (rng.NextSingle() < fixture.Share)
        {
            map.Add(fixture.Id, x + spot.Offset.X, y + spot.Offset.Y, spot.Z, spot.Dir);
        }
    }

    /// <summary>
    /// Builds the basement floors, walls, monster holes, doors with their keypads and posts of the visible edges, then the walls, sunk under the
    /// roof and without posts, that close the closets off from the void, and returns the sprite index of each spawn wall.
    /// </summary>
    public static Dictionary<EdgeKey, int> BuildBasement(Random rng, MapBuilder map, FineLayout layout, IReadOnlyDictionary<EdgeKey, EdgeKind> edges,
        Random closetRng, IReadOnlyDictionary<EdgeKey, EdgeKind> closetEdges, IReadOnlySet<Cell> closets, IReadOnlySet<EdgeKey> holes, bool locked)
    {
        var floor = GetCellsWithin(layout.Cells.Keys, BasementFloorReach).Keys.OrderBy(c => c.U).ThenBy(c => c.V);
        AddTiles(rng, map, KnownObject.BasementFloor, 0, floor.Select(layout.Lattice.GetCenter));

        var spawnWalls = new Dictionary<EdgeKey, int>();
        var skip = layout.FakeDoors
            .Where(p => p.Value)
            .Select(p => GetPreviousEdge(p.Key))
            .ToHashSet();

        foreach ((var piece, bool isDouble) in GetArchPieces(edges))
        {
            ((float x, float y), bool slash) = GetBasementPosition(layout.Lattice, piece);
            map.Add(isDouble ? KnownObject.DoubleDoorWall : KnownObject.SmallDoorWall, x, y, 0, slash ? 0 : 128);
            AddDoor(rng, map, isDouble ? KnownObject.DoubleDoorFrame : KnownObject.SmallDoorFrame, x, y, isDouble ? RaisedDoubleDoorZ : RaisedSmallDoorZ, slash, locked);
        }

        foreach (var (edge, kind) in edges)
        {
            ((float x, float y), bool slash) = GetBasementPosition(layout.Lattice, edge);
            if (kind is EdgeKind.DoubleDoorEnd or EdgeKind.Doorway || skip.Contains(edge))
            {
                continue;
            }

            switch (kind)
            {
                case EdgeKind.DoubleDoor:
                    map.Add(KnownObject.DoubleDoorWall, x, y, 0, slash ? 0 : 128);
                    AddDoor(rng, map, KnownObject.DoubleDoor, x, y, 0, slash, locked);
                    break;
                case EdgeKind.Door:
                    map.Add(KnownObject.SmallDoorWall, x, y, 0, slash ? 0 : 128);
                    AddDoor(rng, map, KnownObject.SmallDoor, x, y, 0, slash, locked);
                    break;
                case EdgeKind.SecretWall:
                    map.Add(KnownObject.BreakableWall, x, y, 0, slash ? 0 : 128);
                    ShadeCloset(rng, map, layout.Lattice, edge, closets);
                    break;
                case EdgeKind.SpawnWall:
                    spawnWalls[edge] = slash ? map.Add(KnownObject.SpawnWallSlash, x, y) : map.Add(KnownObject.SpawnWallBack, x, y, 0, 64);
                    ShadeCloset(rng, map, layout.Lattice, edge, closets);
                    break;
                case EdgeKind.ShutDoor:
                    map.Add(KnownObject.SmallDoorWall, x, y, 0, slash ? 0 : 128);
                    AddDoor(rng, map, KnownObject.SmallDoorFrame, x, y, 0, slash, locked);
                    break;
                default:
                    if (layout.FakeDoors.TryGetValue(edge, out bool isDouble))
                    {
                        map.Add(isDouble ? KnownObject.DoubleDoorWall : KnownObject.SmallDoorWall, x, y, 0, slash ? 0 : 128);
                        int z = rng.NextSingle() < StuckDoorChance ? -rng.Next(StuckDoorZ.Min, StuckDoorZ.Max + 1) : 0;
                        AddDoor(rng, map, isDouble ? KnownObject.DoubleDoorFrame : KnownObject.SmallDoorFrame, x, y, z, slash, locked);
                    }
                    else if (holes.Contains(edge))
                    {
                        map.Add(rng.NextSingle() < BigHoleShare ? KnownObject.BigMonsterHole : KnownObject.SmallMonsterHole, x, y, 0, slash ? 0 : 128);
                        for (int piece = rng.NextSingle() < HoleRubbleShare ? rng.Next(HoleRubble.Min, HoleRubble.Max + 1) : 0; piece > 0; piece--)
                        {
                            map.Add(KnownObject.RockDebris, x + (rng.NextSingle() * 2 - 1) * RubbleSpread, y + (rng.NextSingle() * 2 - 1) * RubbleSpread, RubbleZ, rng.Next(256),
                                unique: false);
                        }
                    }
                    else
                    {
                        map.Add(slash ? KnownObject.BasementWallSlash : KnownObject.BasementWallBack, x, y, 0, BasementWallDirs[rng.Next(BasementWallDirs.Length)]);
                    }

                    break;
            }
        }

        foreach ((int a, int b) in FindPosts(edges))
        {
            (float x, float y) = layout.Lattice.ToWorld(a + 0.25f, b + 0.25f);
            map.Add(KnownObject.BasementBeam, x, y, 0, BeamDirs[rng.Next(BeamDirs.Length)]);
        }

        foreach (var edge in closetEdges.Where(p => p.Value == EdgeKind.Wall).Select(p => p.Key))
        {
            ((float x, float y), bool slash) = GetBasementPosition(layout.Lattice, edge);
            map.Add(slash ? KnownObject.BasementWallSlash : KnownObject.BasementWallBack, x, y, SunkenWallZ, BasementWallDirs[closetRng.Next(BasementWallDirs.Length)]);
        }

        return spawnWalls;
    }

    /// <summary>
    /// Builds the concrete walls, doorway frames, posts and floor tiles of the visible edges.
    /// </summary>
    public static void BuildConcrete(Random rng, MapBuilder map, FineLayout layout, IReadOnlyDictionary<EdgeKey, EdgeKind> edges)
    {
        var l = layout.Lattice;
        var arches = edges.Where(p => p.Value == EdgeKind.Doorway).Select(p => p.Key).ToHashSet();
        foreach (var edge in edges.Keys)
        {
            float x, y;
            KnownObject id;
            int dir;
            if (edge.Axis == 'v' && IsEven(edge.A))
            {
                // Span this edge and the next with a wall piece, leaving arches open and framing the ones a single piece wide
                (x, y) = l.ToWorld(edge.A + 1, edge.B);
                if (CrossesArch(arches, 'v', edge.A, edge.B))
                {
                    AddFrame(rng, map, arches, 'v', edge.A, edge.B, x, y, 128);
                    continue;
                }

                id = KnownObject.MilitaryBaseWallSlash;
                dir = 128;
            }
            else if (edge.Axis == 'u' && IsEven(edge.B))
            {
                (x, y) = l.ToWorld(edge.A, edge.B + 1);
                if (CrossesArch(arches, 'u', edge.A, edge.B))
                {
                    AddFrame(rng, map, arches, 'u', edge.A, edge.B, x, y, 0);
                    continue;
                }

                id = KnownObject.MilitaryBaseWallBack;
                dir = 0;
            }
            else
            {
                continue;
            }

            if (layout.FakeDoors.ContainsKey(edge))
            {
                map.Add(KnownObject.MilitaryBaseDoorWall, x, y, 0, dir);
            }
            else
            {
                map.Add(id, x, y, 0, ConcreteWallDirs[rng.Next(ConcreteWallDirs.Length)]);
            }
        }

        foreach ((int a, int b) in FindPosts(edges))
        {
            if (IsEven(a) && IsEven(b))
            {
                (float x, float y) = l.ToWorld(a, b - 1);
                map.Add(KnownObject.MilitaryBaseCorner, x, y, 0, CornerDirs[rng.Next(CornerDirs.Length)]);
            }
        }

        // 503 tiles use their own 80 by 56 lattice and reach under the walls
        var centers = layout.Cells.Keys.Select(l.GetCenter).ToArray();
        var near = GetCellsWithin(layout.Cells.Keys, ConcreteFloorReach);
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
                if (near.ContainsKey(l.GetCell(x, y)))
                {
                    map.Add(KnownObject.MilitaryBaseFloor, x, y, 0, rng.Next(256));
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

    // Frame most arches one wall piece wide with walls on both sides
    private static void AddFrame(Random rng, MapBuilder map, HashSet<EdgeKey> arches, char axis, int a, int b, float x, float y, int dir)
    {
        if (arches.Contains(Along(0)) && arches.Contains(Along(1)) && !arches.Contains(Along(-1)) && !arches.Contains(Along(2)) && rng.NextSingle() < FramedShare)
        {
            map.Add(KnownObject.FenceEmitter, x, y, 0, dir);
        }

        return;

        EdgeKey Along(int offset) => axis == 'v' ? new EdgeKey('v', a + offset, b) : new EdgeKey('u', a, b + offset);
    }

    // Returns whether a wall piece on the given edge crosses an open arch
    private static bool CrossesArch(HashSet<EdgeKey> arches, char axis, int a, int b)
    {
        for (int offset = 0; offset <= 1; offset++)
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
    private static IEnumerable<(EdgeKey Piece, bool IsDouble)> GetArchPieces(IReadOnlyDictionary<EdgeKey, EdgeKind> edges)
    {
        var lines = new Dictionary<(char Axis, int Line), List<EdgeKey>>();
        foreach (var (key, kind) in edges)
        {
            if (kind != EdgeKind.Doorway)
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
    /// Returns the map position of a basement wall piece on an edge and whether it runs along '/'.
    /// </summary>
    public static (PointF Position, bool Slash) GetBasementPosition(Lattice l, EdgeKey k) =>
        k.Axis == 'v' ? (l.ToWorld(k.A + 0.25f, k.B + 0.25f), true) : (l.ToWorld(k.A - 0.25f, k.B + 0.75f), false);

    private static void ShadeCloset(Random rng, MapBuilder map, Lattice lattice, EdgeKey edge, IReadOnlySet<Cell> closets)
    {
        float side = closets.Contains(edge.Cells.First) ? -1 : 1;
        for (int shadow = rng.NextSingle() < ClosetShadowShare ? rng.Next(ClosetShadows.Min, ClosetShadows.Max + 1) : 0; shadow > 0; shadow--)
        {
            float depth = side * (0.05f + rng.NextSingle() * ClosetShadowDepth), along = rng.NextSingle();
            (float x, float y) = edge.Axis == 'v' ? lattice.ToWorld(edge.A + along, edge.B + depth) : lattice.ToWorld(edge.A + depth, edge.B + along);
            map.Add(KnownObject.EmbeddedShadow, x, y, 0, 0, unique: false);
        }
    }

    /// <summary>
    /// Returns the edge before an edge along its own direction.
    /// </summary>
    public static EdgeKey GetPreviousEdge(EdgeKey k) => k.Axis == 'v' ? new EdgeKey('v', k.A - 1, k.B) : new EdgeKey('u', k.A, k.B - 1);

    private static bool IsEven(int value) => (value & 1) == 0;

    private static bool HasMeetingWall(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, EdgeKey k)
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
    private static (int A, int B)[] FindPosts(IReadOnlyDictionary<EdgeKey, EdgeKind> edges)
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

    /// <summary>
    /// Adds roof tiles over the cells that are not shown, out to a margin around the layout.
    /// </summary>
    public static void BuildRoof(Random rng, WallSet wallSet, FineLayout layout, IReadOnlySet<Cell> shown, MapBuilder map)
    {
        int z = wallSet == WallSet.Basement ? BasementRoofZ : ConcreteRoofZ;
        var lattice = layout.Lattice;
        var centers = layout.Cells.Keys.Select(lattice.GetCenter).ToArray();
        var area = RectangleF.FromLTRB(centers.Min(center => center.X) - RoofMargin, centers.Min(center => center.Y) - RoofMargin,
            centers.Max(center => center.X) + RoofMargin, centers.Max(center => center.Y) + RoofMargin + z);
        
        var corners = new[] { lattice.GetCell(area.Left, area.Top), lattice.GetCell(area.Right, area.Top), lattice.GetCell(area.Left, area.Bottom), lattice.GetCell(area.Right, area.Bottom) };
        var hidden = Enumerable.Range(corners.Min(corner => corner.U), corners.Max(corner => corner.U) - corners.Min(corner => corner.U) + 1)
            .SelectMany(u => Enumerable.Range(corners.Min(corner => corner.V), corners.Max(corner => corner.V) - corners.Min(corner => corner.V) + 1).Select(v => new Cell(u, v)))
            .Where(cell => !shown.Contains(cell) && area.Contains(lattice.GetCenter(cell)))
            .ToArray();
        
        if (wallSet == WallSet.Basement)
        {
            AddTiles(rng, map, KnownObject.Earth, z, hidden.Select(cell => lattice.GetCenter(cell) + BasementRoofShift));
            return;
        }

        var steps = GetCellsWithin(shown, ConcreteEdgingSteps);
        var edging = hidden.Where(steps.ContainsKey).SelectMany(cell => ConcreteCellFill.Select(offset => lattice.GetCenter(cell) + offset));
        int left = (int)MathF.Floor(area.Left / EarthStepX);
        int right = (int)MathF.Ceiling(area.Right / EarthStepX);
        int top = (int)MathF.Floor(area.Top / EarthStepY);
        int bottom = (int)MathF.Ceiling(area.Bottom / EarthStepY);
        var far = Enumerable.Range(left, right - left + 1)
            .SelectMany(column => Enumerable.Range(top, bottom - top + 1).Where(row => ((column + row) & 1) == 1).Select(row => new PointF(EarthStepX * column, EarthStepY * row)))
            .Where(spot => !steps.TryGetValue(lattice.GetCell(spot.X, spot.Y), out int step) || step == ConcreteEdgingSteps);
        
        AddTiles(rng, map, KnownObject.Earth, z, edging.Concat(far));
    }

    private static Dictionary<Cell, int> GetCellsWithin(IEnumerable<Cell> cells, int reach)
    {
        var steps = cells.ToDictionary(cell => cell, _ => 0);
        var frontier = steps.Keys.ToArray();
        for (int step = 1; step <= reach; step++)
        {
            var next = new List<Cell>();
            foreach (var cell in frontier)
            {
                foreach ((int du, int dv) in Around)
                {
                    var neighbor = new Cell(cell.U + du, cell.V + dv);
                    if (steps.TryAdd(neighbor, step))
                    {
                        next.Add(neighbor);
                    }
                }
            }

            frontier = next.ToArray();
        }

        return steps;
    }

    private static void AddTiles(Random rng, MapBuilder map, KnownObject id, int z, IEnumerable<PointF> spots)
    {
        foreach (var spot in spots)
        {
            map.Add(id, spot.X, spot.Y, z, rng.Next(256), unique: false);
        }
    }
}

internal readonly record struct WallSpot(PointF Offset, float Z, byte Dir);

internal sealed record DoorFixture(KnownObject Id, float Share, Dictionary<int, WallSpot> Spots);
