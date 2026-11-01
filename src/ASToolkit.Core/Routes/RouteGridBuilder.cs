using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Provides the construction of a <see cref="RouteGrid"/> from a level model.
/// </summary>
public static class RouteGridBuilder
{
    private const float ChainLinkReach = 120;
    private const int ChainLinkEdges = 4;
    private const float DiagonalTolerance = 8;

    private static readonly Dictionary<EdgeKind, int> Rank = new() { [EdgeKind.Wall] = 0, [EdgeKind.ShutDoor] = 1, [EdgeKind.Doorway] = 2, [EdgeKind.BrokenWall] = 2, [EdgeKind.SecretWall] = 2, [EdgeKind.Door] = 3 };

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

        if (model.Theme == LevelTheme.Outdoor)
        {
            AddChainLinkEdges(fine, edges, model.Sprites);
        }

        var blocked = SolidObjectMapper.FindBlockedCells(fine, cells, SolidObjectMapper.GetCollisionBoxes(model.Sprites, objects));
        cells.ExceptWith(blocked);
        return new RouteGrid { Lattice = fine, Cells = cells, Edges = edges, FenceEdges = fenceEdges, BlockedCells = blocked };
    }

    // Wall off 4 fine edges per chain-link piece, along the diagonal to its nearest fence neighbor
    private static void AddChainLinkEdges(Lattice fine, Dictionary<EdgeKey, EdgeKind> edges, IReadOnlyList<MergedMapSprite> sprites)
    {
        var fences = sprites.Where(sprite => sprite.Id is KnownObject.LatticeWall or KnownObject.LatticeFence or KnownObject.TornLattice).ToArray();
        var walls = sprites.Where(sprite => KnownObject.LabWalls.Contains(sprite.Id)).ToArray();
        foreach (var fence in fences)
        {
            if (fence.Id == KnownObject.TornLattice)
            {
                continue;
            }

            var neighbor = FindNearestOnDiagonal(fence, fences, ChainLinkReach) ?? FindNearestOnDiagonal(fence, walls, float.PositiveInfinity);
            if (neighbor is null)
            {
                continue;
            }

            bool slash = (neighbor.Position.X - fence.Position.X) * (neighbor.Position.Y - fence.Position.Y) < 0;
            (float u, float v) = fine.ToLattice(fence.Position.X, fence.Position.Y);
            for (int index = 0; index < ChainLinkEdges; index++)
            {
                var key = slash
                    ? new EdgeKey('v', (int)MathF.Round(u - ChainLinkEdges / 2f) + index, (int)MathF.Round(v))
                    : new EdgeKey('u', (int)MathF.Round(u), (int)MathF.Round(v - ChainLinkEdges / 2f) + index);
                edges.TryAdd(key, EdgeKind.Wall);
            }
        }
    }

    private static MergedMapSprite? FindNearestOnDiagonal(MergedMapSprite from, IEnumerable<MergedMapSprite> sprites, float reach)
    {
        MergedMapSprite? nearest = null;
        float best = reach;
        foreach (var sprite in sprites)
        {
            float dx = sprite.Position.X - from.Position.X;
            float dy = sprite.Position.Y - from.Position.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            bool diagonal = MathF.Abs(MathF.Abs(dy) * LevelExtractor.LabStep.X - MathF.Abs(dx) * LevelExtractor.LabStep.Y) < DiagonalTolerance * distance;
            if (diagonal && distance > 1 && distance < best)
            {
                nearest = sprite;
                best = distance;
            }
        }

        return nearest;
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

    private static Dictionary<EdgeKey, EdgeKind> BuildMilitaryEdges(LevelModel model, HashSet<EdgeKey> fenceEdges)
    {
        var coarse = new Dictionary<EdgeKey, EdgeKind>(model.Edges);
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
                coarse[key] = EdgeKind.FenceFrame;
                if (holdsFence)
                {
                    coarseFences.Add(key);
                }
            }
        }

        var edges = new Dictionary<EdgeKey, EdgeKind>();
        foreach ((var edge, EdgeKind kind) in coarse)
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

    private static Dictionary<EdgeKey, EdgeKind> BuildLabEdges(LevelModel model)
    {
        var edges = new Dictionary<EdgeKey, EdgeKind>();
        var frames = new HashSet<EdgeKey>();
        var doors = new HashSet<EdgeKey>();
        foreach (var sprite in model.Sprites)
        {
            if (!KnownObject.LabWalls.Contains(sprite.Id) || sprite.Id is KnownObject.LabBeam or KnownObject.LabBeam2)
            {
                continue;
            }

            var keys = GetFineLabEdges(model.Lattice, sprite.Id, sprite.Position.X, sprite.Position.Y, sprite.Facing, model.Theme == LevelTheme.Outdoor);
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

    // Off the coarse grid, take a door's axis from its dir (0 is '/', 64 or 128 is '\') and a wall's from its ID
    private static EdgeKey[] GetFineLabEdges(Lattice lattice, KnownObject id, float x, float y, byte dir, bool outdoor)
    {
        (float u, float v) = lattice.ToLattice(x, y);
        float slashU = 2 * (u - 0.25f);
        float slashV = 2 * (v - 0.25f);
        float backU = 2 * (u + 0.25f);
        float backV = 2 * (v - 0.75f);
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
