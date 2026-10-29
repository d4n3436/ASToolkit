using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Provides the construction of a <see cref="RouteGrid"/> from a level model.
/// </summary>
public static class RouteGridBuilder
{
    private static readonly Dictionary<char, int> Rank = new() { ['W'] = 0, ['F'] = 1, ['O'] = 2, ['B'] = 2, ['S'] = 2, ['D'] = 3 };

    /// <summary>
    /// Builds the routing grid of a level.
    /// </summary>
    public static RouteGrid Build(LevelModel model, ObjectTable objects)
    {
        var fine = model.Lattice.Halve();
        var cells = SplitCells(model.Cells);
        var fenceEdges = new HashSet<EdgeKey>();
        
        var edges = model.Theme == LevelTheme.Military
            ? BuildMilitaryEdges(model, fenceEdges)
            : BuildLabEdges(model);

        var blocked = SolidObjectMapper.FindBlockedCells(fine, cells, SolidObjectMapper.GetCollisionBoxes(model.Sprites, objects));
        cells.ExceptWith(blocked);
        return new RouteGrid { Lattice = fine, Cells = cells, Edges = edges, FenceEdges = fenceEdges, BlockedCells = blocked };
    }

    private static HashSet<Cell> SplitCells(IEnumerable<Cell> cells)
    {
        var fine = new HashSet<Cell>();
        foreach (var cell in cells)
        {
            for (int index = 0; index < 2; index++)
            {
                for (int innerIndex = 0; innerIndex < 2; innerIndex++)
                {
                    fine.Add(new Cell(2 * cell.U + index, 2 * cell.V + innerIndex));
                }
            }
        }

        return fine;
    }

    private static EdgeKey[] SplitEdge(EdgeKey k) => k.Axis == 'v'
        ? [new EdgeKey('v', 2 * k.A, 2 * k.B), new EdgeKey('v', 2 * k.A + 1, 2 * k.B)]
        : [new EdgeKey('u', 2 * k.A, 2 * k.B), new EdgeKey('u', 2 * k.A, 2 * k.B + 1)];

    private static Dictionary<EdgeKey, char> BuildMilitaryEdges(LevelModel model, HashSet<EdgeKey> fenceEdges)
    {
        var coarse = new Dictionary<EdgeKey, char>(model.Edges);
        var coarseFences = new HashSet<EdgeKey>();
        var fences = model.Sprites.Where(s => s.Id == KnownObject.Fence).ToArray();
        foreach (var sprite in model.Sprites)
        {
            if (sprite.Id != KnownObject.FenceEmitter)
            {
                continue;
            }

            (float u, float v) = model.Lattice.ToLattice(sprite.Position.X, sprite.Position.Y);
            if (!Lattice.IsNearInteger(u) || !Lattice.IsNearInteger(v))
            {
                continue;
            }

            int a = (int)MathF.Round(u);
            int b = (int)MathF.Round(v);
            EdgeKey[] keys = sprite.Facing is >= 64 and < 192
                ? [new EdgeKey('v', a - 1, b), new EdgeKey('v', a, b)]
                : [new EdgeKey('u', a, b - 1), new EdgeKey('u', a, b)];
            bool holdsFence = fences.Any(f => MathF.Sqrt((f.Position.X - sprite.Position.X) * (f.Position.X - sprite.Position.X) + (f.Position.Y - sprite.Position.Y) * (f.Position.Y - sprite.Position.Y)) < 40);
            foreach (var key in keys)
            {
                coarse[key] = 'G';
                if (holdsFence)
                {
                    coarseFences.Add(key);
                }
            }
        }

        var edges = new Dictionary<EdgeKey, char>();
        foreach ((var edge, char kind) in coarse)
        {
            foreach (var fineEdge in SplitEdge(edge))
            {
                edges[fineEdge] = kind;
            }
        }

        foreach (var fence in coarseFences)
        {
            fenceEdges.UnionWith(SplitEdge(fence));
        }

        return edges;
    }

    private static Dictionary<EdgeKey, char> BuildLabEdges(LevelModel model)
    {
        var edges = new Dictionary<EdgeKey, char>();
        var frames = new HashSet<EdgeKey>();
        var doors = new HashSet<EdgeKey>();
        foreach (var sprite in model.Sprites)
        {
            if (!KnownObject.LabWalls.Contains(sprite.Id) || sprite.Id is KnownObject.LabBeam or KnownObject.LabBeam2)
            {
                continue;
            }

            var keys = GetFineLabEdges(model.Lattice, sprite.Id, sprite.Position.X, sprite.Position.Y, sprite.Facing);
            switch (sprite.Id)
            {
                case KnownObject.SmallDoorWall or KnownObject.DoubleDoorWall or KnownObject.SmallDoorWall2 or KnownObject.DoubleDoorWall2:
                    frames.UnionWith(keys);
                    Put(edges, keys, 'W');
                    break;
                case KnownObject.SmallDoor or KnownObject.DoubleDoor:
                    doors.UnionWith(keys);
                    Put(edges, keys, 'D');
                    break;
                case KnownObject.DoubleDoorFrame or KnownObject.SmallDoorFrame:
                    doors.UnionWith(keys);
                    Put(edges, keys, sprite.Position.Z <= -40 ? 'O' : 'F');
                    break;
                case KnownObject.BreakableWall or KnownObject.BreakableWall2 or KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack or KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole:
                    Put(edges, keys, 'S');
                    break;
                case KnownObject.BrokenWall or KnownObject.BrokenWall2:
                    Put(edges, keys, 'B');
                    break;
                default:
                    Put(edges, keys, 'W');
                    break;
            }
        }

        frames.ExceptWith(doors);
        foreach (var frame in frames)
        {
            if (edges.TryGetValue(frame, out char kind) && kind == 'W')
            {
                edges[frame] = 'O';
            }
        }

        return edges;
    }

    private static float GetMiss(float t) => Math.Abs(t - MathF.Round(t));

    private static void Put(Dictionary<EdgeKey, char> edges, IReadOnlyList<EdgeKey> keys, char kind)
    {
        foreach (var key in keys)
        {
            int current = edges.TryGetValue(key, out char existing) ? Rank[existing] : -1;
            if (Rank[kind] >= current)
            {
                edges[key] = kind;
            }
        }
    }

    // Off the coarse grid, take a door's axis from its dir (0 is '/', 64 or 128 is '\') and a wall's from its ID
    private static EdgeKey[] GetFineLabEdges(Lattice lattice, KnownObject id, float x, float y, byte dir)
    {
        (float u, float v) = lattice.ToLattice(x, y);
        float slashU = 2 * (u - 0.25f);
        float slashV = 2 * (v - 0.25f);
        float backU = 2 * (u + 0.25f);
        float backV = 2 * (v - 0.75f);
        float dv = Math.Abs(slashV - MathF.Round(slashV));
        float du = Math.Abs(backU - MathF.Round(backU));
        bool slash;
        if (Lattice.IsNearInteger(u - 0.25f) && Lattice.IsNearInteger(v - 0.25f))
        {
            slash = true;
        }
        else if (Lattice.IsNearInteger(u + 0.25f) && Lattice.IsNearInteger(v - 0.75f))
        {
            slash = false;
        }
        else if (id is KnownObject.SmallDoor or KnownObject.SmallDoorWall or KnownObject.DoubleDoor or KnownObject.DoubleDoorWall or KnownObject.DoubleDoorFrame or KnownObject.SmallDoorFrame or KnownObject.SmallDoorWall2 or KnownObject.DoubleDoorWall2)
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

        int n = KnownObject.LabDouble.Contains(id) ? 4 : 2;
        var keys = new List<EdgeKey>(n);
        if (slash)
        {
            if (dv > 0.45f)
            {
                return keys.ToArray();
            }

            int u0 = (int)MathF.Round(slashU);
            int fixedV = (int)MathF.Round(slashV);
            for (int index = 2 - n; index < 2; index++)
            {
                keys.Add(new EdgeKey('v', u0 + index, fixedV));
            }
        }
        else
        {
            if (du > 0.45f)
            {
                return keys.ToArray();
            }

            int fixedU = (int)MathF.Round(backU);
            int v0 = (int)MathF.Round(backV);
            for (int index = 2 - n; index < 2; index++)
            {
                keys.Add(new EdgeKey('u', fixedU, v0 + index));
            }
        }

        return keys.ToArray();
    }
}
