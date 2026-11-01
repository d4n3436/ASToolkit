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
            : LevelExtractor.ReadFineLabEdges(model.Lattice, model.Sprites, model.Theme == LevelTheme.Outdoor);

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
            bool diagonal = MathF.Abs(MathF.Abs(dy) * Lattice.LabStep.X - MathF.Abs(dx) * Lattice.LabStep.Y) < DiagonalTolerance * distance;
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

}
