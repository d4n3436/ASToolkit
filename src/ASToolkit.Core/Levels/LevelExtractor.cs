using System.Drawing;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the conversion of a map into a <see cref="LevelModel"/>.
/// </summary>
public static class LevelExtractor
{
    private const float CoarseSnap = 0.35f;
    private const float FineSnap = 0.45f;
    private static readonly Dictionary<EdgeKind, int> Rank = new() { [EdgeKind.Wall] = 0, [EdgeKind.ShutDoor] = 1, [EdgeKind.Doorway] = 2, [EdgeKind.BrokenWall] = 2, [EdgeKind.SecretWall] = 2, [EdgeKind.Door] = 3 };

    /// <summary>
    /// Builds the model of a map, or returns null when the map has no floor.
    /// </summary>
    public static LevelModel? Extract(string name, MapResourceBuilder map, ObjectTable objects)
    {
        var sp = OrderSprites(map.Sprites).ToList();
        if (sp.Any(s => s.Id is KnownObject.Grass or KnownObject.VolumetricGrass or KnownObject.FlatGrass))
        {
            return ExtractOutdoor(name, sp, map.Size, objects);
        }

        int military = sp.Count(s => KnownObject.MilitaryWalls.Contains(s.Id));
        int lab = sp.Count(s => s.Id is KnownObject.LabWallSlash or KnownObject.LabWallBack or KnownObject.LabWall2Slash or KnownObject.LabWall2Back);
        bool isMilitary = military > lab;
        var cells = new HashSet<Cell>();
        var edges = new Dictionary<EdgeKey, EdgeKind>();
        var lattice = isMilitary ? ExtractMilitary(sp, cells, edges) : ExtractLab(sp, cells, edges);
        if (lattice is null)
        {
            return null;
        }

        // Fill the floor holes the originals leave under wall cabinets
        var ring = new HashSet<Cell>();
        foreach (var cell in cells)
        {
            foreach (var (n, k) in EdgeKey.GetNeighbors(cell))
            {
                if (!cells.Contains(n) && !edges.ContainsKey(k))
                {
                    ring.Add(n);
                }
            }
        }

        foreach (var cell in ring)
        {
            if (EdgeKey.GetNeighbors(cell).HasEdge(edges))
            {
                cells.Add(cell);
            }
        }

        return new LevelModel
        {
            Name = name,
            Theme = isMilitary ? LevelTheme.Military : LevelTheme.Lab,
            Lattice = lattice,
            Cells = cells,
            Edges = edges,
            Objects = ExtractDecor(sp, lattice, objects),
            Sprites = sp
        };
    }

    /// <summary>
    /// Returns sprites ordered by ID, row, column, height, facing and army.
    /// </summary>
    public static IEnumerable<MergedMapSprite> OrderSprites(IEnumerable<MergedMapSprite> sprites) => sprites
        .OrderBy(sprite => (int)sprite.Id)
        .ThenBy(sprite => MathF.Round(sprite.Position.Y * 16))
        .ThenBy(sprite => MathF.Round(sprite.Position.X * 16))
        .ThenBy(sprite => sprite.Position.Z)
        .ThenBy(sprite => sprite.Dir)
        .ThenBy(sprite => sprite.Army);

    /// <summary>
    /// Returns the lattice of a military level's walls and the edges its wall, door and fence pieces cover, as the extractor reads them, or
    /// null when it has no walls.
    /// </summary>
    public static (Lattice Lattice, Dictionary<EdgeKey, EdgeKind> Edges)? ReadMilitaryEdges(IEnumerable<MergedMapSprite> sprites)
    {
        var edges = new Dictionary<EdgeKey, EdgeKind>();
        var lattice = ReadMilitaryWalls(OrderSprites(sprites).ToList(), edges);
        return lattice is null ? null : (lattice, edges);
    }

    /// <summary>
    /// Returns the cell edges that the wall, door and frame pieces of a lab cover, with their kinds.
    /// </summary>
    public static Dictionary<EdgeKey, EdgeKind> ReadLabEdges(Lattice lattice, IEnumerable<MergedMapSprite> sprites, bool outdoor) =>
        ReadLabEdges(lattice, sprites, outdoor, 1);

    /// <summary>
    /// Returns the edges of the half-step lattice that the wall, door and frame pieces of a lab cover, with their kinds.
    /// </summary>
    public static Dictionary<EdgeKey, EdgeKind> ReadFineLabEdges(Lattice lattice, IEnumerable<MergedMapSprite> sprites, bool outdoor) =>
        ReadLabEdges(lattice, sprites, outdoor, 2);

    private static Dictionary<EdgeKey, EdgeKind> ReadLabEdges(Lattice lattice, IEnumerable<MergedMapSprite> sprites, bool outdoor, int scale)
    {
        var edges = new Dictionary<EdgeKey, EdgeKind>();
        var frames = new HashSet<EdgeKey>();
        var doors = new HashSet<EdgeKey>();
        foreach (var sprite in sprites)
        {
            if (!KnownObject.LabWalls.Contains(sprite.Id) || sprite.Id is KnownObject.LabBeam or KnownObject.LabBeam2)
            {
                continue;
            }

            var keys = GetLabEdges(lattice, sprite.Id, sprite.Position.X, sprite.Position.Y, sprite.Facing, outdoor, scale);
            switch (sprite.Id)
            {
                case KnownObject.SmallDoorWall or KnownObject.DoubleDoorWall or KnownObject.SmallDoorWall2 or KnownObject.DoubleDoorWall2:
                    frames.UnionWith(keys);
                    Put(edges, keys, EdgeKind.Wall);
                    break;
                case KnownObject.SmallDoor or KnownObject.DoubleDoor:
                    doors.UnionWith(keys);
                    Put(edges, keys, EdgeKind.Door);
                    break;
                case KnownObject.DoubleDoorFrame or KnownObject.SmallDoorFrame:
                    doors.UnionWith(keys);
                    Put(edges, keys, sprite.Position.Z <= -40 ? EdgeKind.Doorway : EdgeKind.ShutDoor);
                    break;
                case KnownObject.BreakableWall or KnownObject.BreakableWall2 or KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack or KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole:
                    Put(edges, keys, EdgeKind.SecretWall);
                    break;
                case KnownObject.BrokenWall or KnownObject.BrokenWall2:
                    Put(edges, keys, EdgeKind.BrokenWall);
                    break;
                default:
                    Put(edges, keys, EdgeKind.Wall);
                    break;
            }
        }

        frames.ExceptWith(doors);
        foreach (var frame in frames)
        {
            if (edges.TryGetValue(frame, out EdgeKind kind) && kind == EdgeKind.Wall)
            {
                edges[frame] = EdgeKind.Doorway;
            }
        }

        return edges;
    }

    private static float GetMiss(float t) => Math.Abs(t - MathF.Round(t));

    private static void Put(Dictionary<EdgeKey, EdgeKind> edges, IReadOnlyList<EdgeKey> keys, EdgeKind kind)
    {
        foreach (var key in keys)
        {
            int current = edges.TryGetValue(key, out EdgeKind existing) ? Rank[existing] : -1;
            if (Rank[kind] >= current)
            {
                edges[key] = kind;
            }
        }
    }

    // Off the coarse grid, take a door's axis from its dir (0 is '/', 64 or 128 is '\') and a wall's from its ID, then round it to the lattice
    // of the scale
    private static EdgeKey[] GetLabEdges(Lattice lattice, KnownObject id, float x, float y, byte dir, bool outdoor, int scale)
    {
        (float u, float v) = lattice.ToLattice(x, y);
        float slashV = 2 * (v - 0.25f);
        float backU = 2 * (u + 0.25f);
        float dv = Math.Abs(slashV - MathF.Round(slashV));
        float du = Math.Abs(backU - MathF.Round(backU));
        bool door = id is KnownObject.SmallDoor or KnownObject.SmallDoorWall or KnownObject.DoubleDoor or KnownObject.DoubleDoorWall or KnownObject.DoubleDoorFrame or KnownObject.SmallDoorFrame
            or KnownObject.SmallDoorWall2 or KnownObject.DoubleDoorWall2;
        bool slash;
        // Trust the piece ID of plain walls, and of every piece outdoors
        bool plain = id is KnownObject.LabWallSlash or KnownObject.LabWallBack or KnownObject.LabWall2Slash or KnownObject.LabWall2Back;
        if ((outdoor || plain) && (KnownObject.LabSlash.Contains(id) || KnownObject.LabBack.Contains(id)))
        {
            slash = KnownObject.LabSlash.Contains(id);
        }
        else if (!(outdoor && door) && Lattice.IsNearInteger(u - 0.25f) && Lattice.IsNearInteger(v - 0.25f))
        {
            slash = true;
        }
        else if (!(outdoor && door) && Lattice.IsNearInteger(u + 0.25f) && Lattice.IsNearInteger(v - 0.75f))
        {
            slash = false;
        }
        else if (door)
        {
            slash = dir switch
            {
                < 16 or >= 240 => true,
                >= 48 and < 144 => false,
                _ => GetMiss(u - 0.25f) + GetMiss(v - 0.25f) < GetMiss(u + 0.25f) + GetMiss(v - 0.75f)
            };
        }
        else
        {
            slash = KnownObject.LabSlash.Contains(id) || (!KnownObject.LabBack.Contains(id) && dv <= du);
        }

        float tolerance = scale == 1 ? CoarseSnap : FineSnap;
        int count = scale * (KnownObject.LabDouble.Contains(id) ? 2 : 1);
        float line = slash ? scale * (v - 0.25f) : scale * (u + 0.25f);
        if (Math.Abs(line - MathF.Round(line)) > tolerance)
        {
            return [];
        }

        var keys = new EdgeKey[count];
        int fixedLine = (int)MathF.Round(line);
        int start = (int)MathF.Round(slash ? scale * (u - 0.25f) : scale * (v - 0.75f));
        for (int index = 0; index < count; index++)
        {
            int along = start + scale - count + index;
            keys[index] = slash ? new EdgeKey('v', along, fixedLine) : new EdgeKey('u', fixedLine, along);
        }

        return keys;
    }

    private static int CountLabFit(List<MergedMapSprite> sp, Lattice lattice)
    {
        int n = 0;
        foreach (var sprite in sp)
        {
            bool slash = KnownObject.LabSlash.Contains(sprite.Id);
            if (!slash && !KnownObject.LabBack.Contains(sprite.Id))
            {
                continue;
            }

            (float u, float v) = lattice.ToLattice(sprite.Position.X, sprite.Position.Y);
            bool fits = slash
                ? Lattice.IsNearInteger(u - 0.25f) && Lattice.IsNearInteger(v - 0.25f)
                : Lattice.IsNearInteger(u + 0.25f) && Lattice.IsNearInteger(v - 0.75f);
            if (fits)
            {
                n++;
            }
        }

        return n;
    }

    private static Lattice? ExtractLab(List<MergedMapSprite> sp, HashSet<Cell> cells, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var floor = sp.Where(s => s.Id is KnownObject.BasementFloor or KnownObject.BasementFloorAlt).ToList();
        var big = sp.Where(s => s.Id == KnownObject.MilitaryFloor).ToList();
        if (big.Count * 16 > floor.Count * 4)
        {
            floor.Clear();
        }
        else
        {
            big.Clear();
        }

        if (floor.Count == 0 && big.Count == 0)
        {
            return null;
        }

        Lattice? best = null;
        int bestFit = -1;
        if (floor.Count > 0)
        {
            // Let the walls pick between the two tile phases that fit the floor
            var probe = new Lattice(Lattice.LabStep.X, Lattice.LabStep.Y, floor[0].Position.X - 40, floor[0].Position.Y);
            var phases = new List<((int, int) Key, int Count, MergedMapSprite First)>();
            var index = new Dictionary<(int, int), int>();
            foreach (var floorSprite in floor)
            {
                (float u, float v) = probe.ToLattice(floorSprite.Position.X, floorSprite.Position.Y);
                var key = ((int)MathF.Round((u - MathF.Floor(u)) * 8) % 8, (int)MathF.Round((v - MathF.Floor(v)) * 8) % 8);
                if (index.TryGetValue(key, out int i))
                {
                    phases[i] = (key, phases[i].Count + 1, phases[i].First);
                }
                else
                {
                    index[key] = phases.Count;
                    phases.Add((key, 1, floorSprite));
                }
            }

            foreach (var phase in phases.OrderByDescending(p => p.Count).Take(6))
            {
                foreach (int dx in new[] { 40, 0 })
                {
                    var candidate = new Lattice(Lattice.LabStep.X, Lattice.LabStep.Y, phase.First.Position.X - dx, phase.First.Position.Y);
                    int fit = CountLabFit(sp, candidate);
                    if (fit > bestFit)
                    {
                        best = candidate;
                        bestFit = fit;
                    }
                }
            }

            foreach (var floorSprite in floor)
            {
                cells.Add(best!.GetCell(floorSprite.Position.X, floorSprite.Position.Y));
            }
        }
        else
        {
            best = FitLabLattice(sp);
            if (best is null)
            {
                return null;
            }

            foreach (var tile in big)
            {
                (float u0, float v0) = best.ToLattice(tile.Position.X, tile.Position.Y);
                for (int u = (int)MathF.Floor(u0) - 3; u < (int)MathF.Floor(u0) + 4; u++)
                {
                    for (int v = (int)MathF.Floor(v0) - 3; v < (int)MathF.Floor(v0) + 4; v++)
                    {
                        (float x, float y) = best.ToWorld(u + 0.5f, v + 0.5f);
                        if (Math.Abs(x - tile.Position.X) / 80 + Math.Abs(y - tile.Position.Y) / 56 <= 1.0f)
                        {
                            cells.Add(new Cell(u, v));
                        }
                    }
                }
            }
        }

        foreach ((var key, EdgeKind kind) in ReadLabEdges(best!, sp, outdoor: false))
        {
            edges[key] = kind;
        }

        return best;
    }

    private static Lattice? FitLabLattice(List<MergedMapSprite> sp)
    {
        Lattice? best = null;
        int bestFit = -1;
        foreach (var wall in sp.Where(s => KnownObject.LabWalls.Contains(s.Id) && s.Id is not (KnownObject.LabBeam or KnownObject.LabBeam2)).Take(40))
        {
            foreach ((int dx, int dy) in new[] { (20, 0), (20, 28), (-20, 0), (-20, 28) })
            {
                var candidate = new Lattice(Lattice.LabStep.X, Lattice.LabStep.Y, wall.Position.X - dx, wall.Position.Y - dy);
                int fit = CountLabFit(sp, candidate);
                if (fit > bestFit)
                {
                    best = candidate;
                    bestFit = fit;
                }
            }
        }

        return best;
    }

    private static LevelModel? ExtractOutdoor(string name, List<MergedMapSprite> sp, SizeF size, ObjectTable objects)
    {
        var lattice = FitLabLattice(sp);
        if (lattice is null)
        {
            return null;
        }

        var corners = new[] { lattice.ToLattice(0, 0), lattice.ToLattice(size.Width, 0), lattice.ToLattice(0, size.Height), lattice.ToLattice(size.Width, size.Height) };
        var cells = new HashSet<Cell>();
        for (int u = (int)MathF.Floor(corners.Min(c => c.U)); u <= (int)MathF.Ceiling(corners.Max(c => c.U)); u++)
        {
            for (int v = (int)MathF.Floor(corners.Min(c => c.V)); v <= (int)MathF.Ceiling(corners.Max(c => c.V)); v++)
            {
                (float x, float y) = lattice.ToWorld(u + 0.5f, v + 0.5f);
                if (x >= 0 && x <= size.Width && y >= 0 && y <= size.Height)
                {
                    cells.Add(new Cell(u, v));
                }
            }
        }

        return new LevelModel
        {
            Name = name,
            Theme = LevelTheme.Outdoor,
            Lattice = lattice,
            Cells = cells,
            Edges = ReadLabEdges(lattice, sp, outdoor: true),
            Objects = ExtractDecor(sp, lattice, objects),
            Sprites = sp
        };
    }

    private static Lattice? ExtractMilitary(List<MergedMapSprite> sp, HashSet<Cell> cells, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var lattice = ReadMilitaryWalls(sp, edges);
        if (lattice is not null)
        {
            AddMilitaryFloor(sp, lattice, cells);
        }

        return lattice;
    }

    private static Lattice? ReadMilitaryWalls(List<MergedMapSprite> sp, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var slashWalls = sp.Where(s => s.Id == KnownObject.MilitaryWallSlash).ToArray();
        if (slashWalls.Length == 0)
        {
            return null;
        }

        var probe = new Lattice(Lattice.MilitaryStep.X, Lattice.MilitaryStep.Y, slashWalls[0].Position.X, slashWalls[0].Position.Y);
        var firsts = new List<MergedMapSprite>();
        var seen = new HashSet<(int, int)>();
        foreach (var wall in slashWalls)
        {
            (float u, float v) = probe.ToLattice(wall.Position.X, wall.Position.Y);
            if (seen.Add(((int)MathF.Round((u - MathF.Floor(u)) * 8) % 8, (int)MathF.Round((v - MathF.Floor(v)) * 8) % 8)))
            {
                firsts.Add(wall);
            }
        }

        Lattice? best = null;
        int bestFit = -1;
        foreach (var first in firsts.Take(8))
        {
            var candidate = new Lattice(Lattice.MilitaryStep.X, Lattice.MilitaryStep.Y, first.Position.X, first.Position.Y);
            int fit = sp.Count(s => s.Id is KnownObject.MilitaryWallBack or KnownObject.MilitaryWallSlash && IsOnVertex(candidate, s));
            if (fit > bestFit)
            {
                best = candidate;
                bestFit = fit;
            }
        }

        var lattice = best!;
        foreach (var sprite in sp)
        {
            if (sprite.Id is not (KnownObject.MilitaryWallBack or KnownObject.MilitaryWallSlash or KnownObject.FenceEmitter or KnownObject.MilitaryDoorWall) || !IsOnVertex(lattice, sprite))
            {
                continue;
            }

            (float u, float v) = lattice.ToLattice(sprite.Position.X, sprite.Position.Y);
            int a = (int)MathF.Round(u);
            int b = (int)MathF.Round(v);
            bool slash = sprite.Id == KnownObject.MilitaryWallSlash || (sprite.Id is KnownObject.FenceEmitter or KnownObject.MilitaryDoorWall && sprite.Facing is >= 64 and < 192);
            EdgeKind kind = sprite.Id == KnownObject.MilitaryDoorWall ? EdgeKind.ShutDoor : EdgeKind.Wall;
            if (slash)
            {
                edges[new EdgeKey('v', a - 1, b)] = kind;
                edges[new EdgeKey('v', a, b)] = kind;
            }
            else
            {
                edges[new EdgeKey('u', a, b - 1)] = kind;
                edges[new EdgeKey('u', a, b)] = kind;
            }
        }

        return lattice;
    }

    private static void AddMilitaryFloor(List<MergedMapSprite> sp, Lattice lattice, HashSet<Cell> cells)
    {
        var tiles = sp.Where(s => s.Id == KnownObject.MilitaryFloor).ToArray();
        if (tiles.Length == 0)
        {
            return;
        }

        var tileSet = tiles.Select(t => ((long)MathF.Round(t.Position.X), (long)MathF.Round(t.Position.Y))).ToHashSet();
        var us = tiles.Select(t => lattice.ToLattice(t.Position.X, t.Position.Y)).ToArray();
        int uMin = (int)MathF.Floor(us.Min(p => p.U)) - 3;
        int uMax = (int)MathF.Ceiling(us.Max(p => p.U)) + 3;
        int vMin = (int)MathF.Floor(us.Min(p => p.V)) - 3;
        int vMax = (int)MathF.Ceiling(us.Max(p => p.V)) + 3;
        float tx0 = tiles[0].Position.X;
        float ty0 = tiles[0].Position.Y;
        for (int u = uMin; u < uMax; u++)
        {
            for (int v = vMin; v < vMax; v++)
            {
                (float x, float y) = lattice.ToWorld(u + 0.5f, v + 0.5f);
                float p = (x - tx0) / 80;
                float q = (y - ty0) / 56;
                float ta = (p - q) / 2;
                float tb = (p + q) / 2;
                bool covered = false;
                foreach (float roundedA in new[] { MathF.Floor(ta), MathF.Ceiling(ta) })
                {
                    foreach (float roundedB in new[] { MathF.Floor(tb), MathF.Ceiling(tb) })
                    {
                        float cx = tx0 + 80 * (roundedA + roundedB);
                        float cy = ty0 + 56 * (roundedB - roundedA);
                        if (tileSet.Contains(((long)MathF.Round(cx), (long)MathF.Round(cy))) && Math.Abs(x - cx) / 80 + Math.Abs(y - cy) / 56 <= 1.0f)
                        {
                            covered = true;
                        }
                    }
                }

                if (covered)
                {
                    cells.Add(new Cell(u, v));
                }
            }
        }
    }

    private static bool IsOnVertex(Lattice lattice, MergedMapSprite s)
    {
        (float u, float v) = lattice.ToLattice(s.Position.X, s.Position.Y);
        return Lattice.IsNearInteger(u) && Lattice.IsNearInteger(v);
    }

    private static DecorObject[] ExtractDecor(List<MergedMapSprite> sp, Lattice lattice, ObjectTable objects)
    {
        var decor = sp.Where(s => s.Id.IsDecor).ToArray();
        var result = new List<DecorObject>(decor.Length);
        for (int decorObjectIndex = 0; decorObjectIndex < decor.Length; decorObjectIndex++)
        {
            var s = decor[decorObjectIndex];
            int? support = objects.FindSupport(decor, decorObjectIndex);
            (float u, float v) = lattice.ToLattice(s.Position.X, s.Position.Y);
            result.Add(new DecorObject(s.Id, u, v, s.Position.Z, s.Facing, s.Payload, support));
        }

        return result.ToArray();
    }
}
