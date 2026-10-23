using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the conversion of a map into a <see cref="LevelModel"/>.
/// </summary>
public static class LevelExtractor
{
    /// <summary>
    /// Represents the lab lattice step.
    /// </summary>
    public static readonly (double X, double Y) LabStep = (40, 28);

    /// <summary>
    /// Represents the military fine lattice step.
    /// </summary>
    public static readonly (double X, double Y) MilitaryStep = (45, 32);

    /// <summary>
    /// Builds the model of a map, or returns null when the map has no lab or military floor.
    /// </summary>
    public static LevelModel? Extract(string name, MapResourceBuilder map, ObjectTable objects)
    {
        var sp = map.Sprites;
        int military = sp.Count(s => KnownObject.MilitaryWalls.Contains(s.Id));
        int lab = sp.Count(s => s.Id is KnownObject.LabWallSlash or KnownObject.LabWallBack or KnownObject.LabWall2Slash or KnownObject.LabWall2Back);
        bool isMilitary = military > lab;
        var cells = new HashSet<Cell>();
        var edges = new Dictionary<EdgeKey, char>();
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
    /// Returns the edges a lab wall piece covers. A piece off the lattice covers none, and double doors and frames cover two.
    /// </summary>
    public static IEnumerable<EdgeKey> GetLabEdges(Lattice lattice, KnownObject id, double x, double y)
    {
        var key = GetLabEdge(lattice, id, x, y);
        if (key is null)
        {
            yield break;
        }

        var k = key.Value;
        yield return k;
        if (KnownObject.LabDouble.Contains(id))
        {
            yield return k.Axis == 'v' ? new EdgeKey('v', k.A - 1, k.B) : new EdgeKey('u', k.A, k.B - 1);
        }
    }

    /// <summary>
    /// Returns the edge of a lab wall piece. Pieces placed off the lattice snap to the nearest edge of their direction.
    /// </summary>
    public static EdgeKey? GetLabEdge(Lattice lattice, KnownObject id, double x, double y)
    {
        (double u, double v) = lattice.ToLattice(x, y);
        if (Lattice.IsNearInteger(u - .25) && Lattice.IsNearInteger(v - .25))
        {
            return new EdgeKey('v', (int)Math.Round(u - .25), (int)Math.Round(v - .25));
        }

        if (Lattice.IsNearInteger(u + .25) && Lattice.IsNearInteger(v - .75))
        {
            return new EdgeKey('u', (int)Math.Round(u + .25), (int)Math.Round(v - .75));
        }

        double dv = Math.Abs(v - .25 - Math.Round(v - .25));
        double du = Math.Abs(u + .25 - Math.Round(u + .25));
        if (KnownObject.LabSlash.Contains(id) || (!KnownObject.LabBack.Contains(id) && dv <= du))
        {
            return dv < .35 ? new EdgeKey('v', (int)Math.Round(u - .25), (int)Math.Round(v - .25)) : null;
        }

        return du < .35 ? new EdgeKey('u', (int)Math.Round(u + .25), (int)Math.Round(v - .75)) : null;
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

            (double u, double v) = lattice.ToLattice(sprite.Position.X, sprite.Position.Y);
            bool fits = slash
                ? Lattice.IsNearInteger(u - .25) && Lattice.IsNearInteger(v - .25)
                : Lattice.IsNearInteger(u + .25) && Lattice.IsNearInteger(v - .75);
            if (fits)
            {
                n++;
            }
        }

        return n;
    }

    private static Lattice? ExtractLab(List<MergedMapSprite> sp, HashSet<Cell> cells, Dictionary<EdgeKey, char> edges)
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
            var probe = new Lattice(LabStep.X, LabStep.Y, floor[0].Position.X - 40, floor[0].Position.Y);
            var phases = new List<((int, int) Key, int Count, MergedMapSprite First)>();
            var index = new Dictionary<(int, int), int>();
            foreach (var floorSprite in floor)
            {
                (double u, double v) = probe.ToLattice(floorSprite.Position.X, floorSprite.Position.Y);
                var key = ((int)Math.Round((u - Math.Floor(u)) * 8) % 8, (int)Math.Round((v - Math.Floor(v)) * 8) % 8);
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
                    var candidate = new Lattice(LabStep.X, LabStep.Y, phase.First.Position.X - dx, phase.First.Position.Y);
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
            foreach (var wall in sp.Where(s => KnownObject.LabWalls.Contains(s.Id) && s.Id is not (KnownObject.LabBeam or KnownObject.LabBeam2)).Take(40))
            {
                foreach ((int dx, int dy) in new[] { (20, 0), (20, 28), (-20, 0), (-20, 28) })
                {
                    var candidate = new Lattice(LabStep.X, LabStep.Y, wall.Position.X - dx, wall.Position.Y - dy);
                    int fit = CountLabFit(sp, candidate);
                    if (fit > bestFit)
                    {
                        best = candidate;
                        bestFit = fit;
                    }
                }
            }

            if (best is null)
            {
                return null;
            }

            foreach (var tile in big)
            {
                (double u0, double v0) = best.ToLattice(tile.Position.X, tile.Position.Y);
                for (int u = (int)Math.Floor(u0) - 3; u < (int)Math.Floor(u0) + 4; u++)
                {
                    for (int v = (int)Math.Floor(v0) - 3; v < (int)Math.Floor(v0) + 4; v++)
                    {
                        (double x, double y) = best.ToWorld(u + .5, v + .5);
                        if (Math.Abs(x - tile.Position.X) / 80 + Math.Abs(y - tile.Position.Y) / 56 <= 1.0)
                        {
                            cells.Add(new Cell(u, v));
                        }
                    }
                }
            }
        }

        foreach (var sprite in sp)
        {
            if (!KnownObject.LabWalls.Contains(sprite.Id) || sprite.Id is KnownObject.LabBeam or KnownObject.LabBeam2)
            {
                continue;
            }

            char kind = sprite.Id switch
            {
                KnownObject.SmallDoor or KnownObject.DoubleDoor => 'D',
                KnownObject.DoubleDoorFrame or KnownObject.SmallDoorFrame => 'F',
                _ => 'W'
            };
            
            foreach (var key in GetLabEdges(best!, sprite.Id, sprite.Position.X, sprite.Position.Y))
            {
                bool has = edges.TryGetValue(key, out char current);
                if (kind == 'D' || !has || (current == 'W' && kind == 'F'))
                {
                    edges[key] = has && current == 'D' ? 'D' : kind;
                }
            }
        }

        return best;
    }

    private static Lattice? ExtractMilitary(List<MergedMapSprite> sp, HashSet<Cell> cells, Dictionary<EdgeKey, char> edges)
    {
        var slashWalls = sp.Where(s => s.Id == KnownObject.MilitaryWallSlash).ToArray();
        if (slashWalls.Length == 0)
        {
            return null;
        }

        var probe = new Lattice(MilitaryStep.X, MilitaryStep.Y, slashWalls[0].Position.X, slashWalls[0].Position.Y);
        var firsts = new List<MergedMapSprite>();
        var seen = new HashSet<(int, int)>();
        foreach (var wall in slashWalls)
        {
            (double u, double v) = probe.ToLattice(wall.Position.X, wall.Position.Y);
            if (seen.Add(((int)Math.Round((u - Math.Floor(u)) * 8) % 8, (int)Math.Round((v - Math.Floor(v)) * 8) % 8)))
            {
                firsts.Add(wall);
            }
        }

        Lattice? best = null;
        int bestFit = -1;
        foreach (var first in firsts.Take(8))
        {
            var candidate = new Lattice(MilitaryStep.X, MilitaryStep.Y, first.Position.X, first.Position.Y);
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

            (double u, double v) = lattice.ToLattice(sprite.Position.X, sprite.Position.Y);
            int a = (int)Math.Round(u);
            int b = (int)Math.Round(v);
            bool slash = sprite.Id == KnownObject.MilitaryWallSlash || (sprite.Id is KnownObject.FenceEmitter or KnownObject.MilitaryDoorWall && sprite.Facing is >= 64 and < 192);
            char kind = sprite.Id == KnownObject.MilitaryDoorWall ? 'F' : 'W';
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

        var tiles = sp.Where(s => s.Id == KnownObject.MilitaryFloor).ToArray();
        if (tiles.Length == 0)
        {
            return lattice;
        }

        var tileSet = tiles.Select(t => ((long)Math.Round(t.Position.X), (long)Math.Round(t.Position.Y))).ToHashSet();
        var us = tiles.Select(t => lattice.ToLattice(t.Position.X, t.Position.Y)).ToArray();
        int uMin = (int)Math.Floor(us.Min(p => p.U)) - 3;
        int uMax = (int)Math.Ceiling(us.Max(p => p.U)) + 3;
        int vMin = (int)Math.Floor(us.Min(p => p.V)) - 3;
        int vMax = (int)Math.Ceiling(us.Max(p => p.V)) + 3;
        double tx0 = tiles[0].Position.X;
        double ty0 = tiles[0].Position.Y;
        for (int u = uMin; u < uMax; u++)
        {
            for (int v = vMin; v < vMax; v++)
            {
                (double x, double y) = lattice.ToWorld(u + .5, v + .5);
                double p = (x - tx0) / 80;
                double q = (y - ty0) / 56;
                double ta = (p - q) / 2;
                double tb = (p + q) / 2;
                bool covered = false;
                foreach (double roundedA in new[] { Math.Floor(ta), Math.Ceiling(ta) })
                {
                    foreach (double roundedB in new[] { Math.Floor(tb), Math.Ceiling(tb) })
                    {
                        double cx = tx0 + 80 * (roundedA + roundedB);
                        double cy = ty0 + 56 * (roundedB - roundedA);
                        if (tileSet.Contains(((long)Math.Round(cx), (long)Math.Round(cy))) && Math.Abs(x - cx) / 80 + Math.Abs(y - cy) / 56 <= 1.0)
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

        return lattice;
    }

    private static bool IsOnVertex(Lattice lattice, MergedMapSprite s)
    {
        (double u, double v) = lattice.ToLattice(s.Position.X, s.Position.Y);
        return Lattice.IsNearInteger(u) && Lattice.IsNearInteger(v);
    }

    private static DecorObject[] ExtractDecor(List<MergedMapSprite> sp, Lattice lattice, ObjectTable objects)
    {
        var decor = sp.Where(s => s.Id.IsDecor).ToArray();
        var result = new List<DecorObject>(decor.Length);
        for (int decorObjectIndex = 0; decorObjectIndex < decor.Length; decorObjectIndex++)
        {
            var s = decor[decorObjectIndex];
            int? support = null;
            if (s.Position.Z > 2)
            {
                int bestIndex = -1;
                double bestDistance = double.MaxValue;
                for (int innerDecorObjectIndex = 0; innerDecorObjectIndex < decor.Length; innerDecorObjectIndex++)
                {
                    var t = decor[innerDecorObjectIndex];
                    if (innerDecorObjectIndex == decorObjectIndex || t.Position.Z >= s.Position.Z - 2)
                    {
                        continue;
                    }

                    objects.TryFind(t.Id, out var obj);
                    double ex = obj.Size.X;
                    double ey = obj.Size.Y;
                    double ez = obj.Size.Z;
                    if (ez < 5 || Math.Abs(t.Position.X - s.Position.X) > ex + 6 || Math.Abs(t.Position.Y - s.Position.Y) > ey + 6)
                    {
                        continue;
                    }

                    double d = Math.Abs(t.Position.X - s.Position.X) + Math.Abs(t.Position.Y - s.Position.Y);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestIndex = innerDecorObjectIndex;
                    }
                }

                support = bestIndex;
            }

            (double u, double v) = lattice.ToLattice(s.Position.X, s.Position.Y);
            result.Add(new DecorObject(s.Id, u, v, s.Position.Z, s.Facing, s.Payload, support));
        }

        return result.ToArray();
    }
}
