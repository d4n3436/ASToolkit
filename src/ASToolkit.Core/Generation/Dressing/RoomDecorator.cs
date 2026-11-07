using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Provides the blood density and battle aftermath of a level's decor and the stripes in front of its missiles.
/// </summary>
public static class RoomDecorator
{
    private const int BloodSource = -5;
    private const int PlantSource = -3;
    private const float BloodReach = 40;
    private const float MissileReach = 120;
    private const int GibCount = 3;
    private const float MissileMarkReach = 90;
    private const float WallSignHeight = 60;
    private const int MarkSource = -6;

    /// <summary>
    /// Thins the blood and the corpses to their rates per floor cell, leaves blood in the last two stations with a corpse and gibs in the
    /// last one, and lays every corpse on blood.
    /// </summary>
    public static void Decorate(WallSet wallSet, LayoutPlan plan, FineLayout layout, List<PlacedDecor> decor,
        IReadOnlySet<Cell> floor, IReadOnlySet<Cell> keepClear, LevelOccupancy occupancy, DecorHeightTable wallSetDecor, FacingTable facings, float corpseRate, float bloodRate)
    {
        var random = CreateRandom(wallSet, plan);
        var taken = new HashSet<Cell>();
        foreach (var piece in decor)
        {
            taken.Add(GetCell(piece));
        }

        int index = 0;
        var corpsesBefore = decor.Where(piece => piece.Id.IsCorpse).ToArray();
        var blood = decor
            .Where(piece => piece.Id.IsBlood && !corpsesBefore.Any(corpse => IsNear(layout.Lattice, piece, corpse, BloodReach)))
            .ToArray();
        
        int bloodCap = (int)(floor.Count * bloodRate);
        if (blood.Length > bloodCap)
        {
            var drop = blood.OrderBy(_ => random.Next()).Take(blood.Length - bloodCap).ToHashSet();

            foreach (var cell in blood.Where(drop.Contains).Select(GetCell).Distinct())
            {
                taken.Remove(cell);
            }

            decor.RemoveAll(drop.Contains);
        }

        var corpses = decor
            .Where(piece => piece.Id.IsCorpse)
            .ToArray();
        
        int corpseCap = (int)(floor.Count * corpseRate);
        if (corpses.Length > corpseCap)
        {
            var drop = corpses.OrderBy(_ => random.Next()).Take(corpses.Length - corpseCap).ToHashSet();

            decor.RemoveAll(drop.Contains);
        }

        var supported = Dresser.KeepSupported(decor);
        if (supported.Length < decor.Count)
        {
            decor.Clear();
            decor.AddRange(supported);
        }

        // Leave blood in the last two stations and a corpse in the last one
        int denIndex = plan.Stations.Count - 1;
        for (int station = Math.Max(0, denIndex - 1); station <= denIndex; station++)
        {
            var cells = layout.GetRegionCells(plan.Stations[station].Id);
            var spots = cells
                .Where(cell => IsUsable(cell) && !taken.Contains(cell))
                .OrderBy(_ => random.NextSingle())
                .ToArray();
            
            int bloodCount = Math.Min(spots.Length, spots.Length >= 6 ? 3 : 2);
            for (int spot = 0; spot < bloodCount; spot++)
            {
                Add(KnownObject.SmallBloodPool, spots[spot]);
            }

            if (station == denIndex && spots.Length > 0)
            {
                Add(wallSet == WallSet.Basement ? KnownObject.ScientistCorpse : KnownObject.SoldierCorpse, spots[0]);
                for (int gib = 1; gib <= GibCount && gib < spots.Length; gib++)
                {
                    Add(KnownObject.FlyingGibs, spots[gib]);
                }
            }
        }

        // Lay every corpse on blood
        foreach (var corpse in decor.Where(piece => piece.Id.IsCorpse).ToArray())
        {
            if (!decor.Any(piece => piece.Id.IsBlood && IsNear(layout.Lattice, piece, corpse, BloodReach)) && wallSetDecor.Contains(KnownObject.SmallBloodPool))
            {
                decor.Add(new PlacedDecor(KnownObject.SmallBloodPool, corpse.U, corpse.V, wallSetDecor.GetMiddleHeight(KnownObject.SmallBloodPool), 0, [], (BloodSource, index++), null, SpriteSource.Model));
            }
        }

        return;

        bool IsUsable(Cell cell) => floor.Contains(cell) && !layout.IsWalled(cell) && !keepClear.Contains(cell);

        void Add(KnownObject id, Cell cell)
        {
            if (!wallSetDecor.Contains(id))
            {
                return;
            }

            byte dir = facings.PickFacing(random, id, layout.Edges, cell.U + 0.5f, cell.V + 0.5f);
            var wall = layout.Edges.FindNearestWall(cell.U + 0.5f, cell.V + 0.5f);
            if (!facings.Fits(id, dir, wall) || !facings.IsByWall(id, wall)
                || id.IsWallMounted && !facings.Hangs(id, dir, wallSetDecor.GetMiddleHeight(id), wall, layout.Edges, cell.U + 0.5f, cell.V + 0.5f))
            {
                return;
            }

            (float x, float y) = layout.Lattice.ToWorld(cell.U + 0.5f, cell.V + 0.5f);
            float z = wallSetDecor.GetMiddleHeight(id);
            if (!occupancy.Fits(id, x, y, z))
            {
                return;
            }

            occupancy.Add(id, x, y, z);
            byte[] payload = id == KnownObject.Crate ? KnownObject.CreateEmptyCratePayload() : [];
            decor.Add(new PlacedDecor(id, cell.U + 0.5f, cell.V + 0.5f, z, dir, payload, (PlantSource, index++), null, SpriteSource.Model));
            taken.Add(cell);
        }
    }

    /// <summary>
    /// Lays the floor stripes of an original missile three with the same facing in front of missile threes that have none, as often as
    /// the originals do, where every stripe lands on open floor.
    /// </summary>
    public static void MarkMissiles(Random rng, List<PlacedDecor> decor, IReadOnlyDictionary<byte, List<IReadOnlyList<PropPlacement>>> marks, float share,
        IReadOnlySet<Cell> floor, IReadOnlySet<Cell> keepClear, Lattice lattice)
    {
        int index = 0;
        var missiles = decor.Where(piece => piece.Id == KnownObject.Missile).ToList();
        var seen = new HashSet<PlacedDecor>(ReferenceEqualityComparer.Instance);
        foreach (var middle in missiles.Where(piece => missiles.Count(other => !ReferenceEquals(other, piece) && IsNear(lattice, piece, other, MissileReach)) == 2).ToArray())
        {
            var three = missiles.Where(other => IsNear(lattice, middle, other, MissileReach)).ToArray();
            if (three.Any(seen.Contains))
            {
                continue;
            }

            seen.UnionWith(three);
            if (decor.Any(piece => piece.Id == KnownObject.WallInscriptions && three.Any(missile => IsNear(lattice, piece, missile, MissileMarkReach)))
                || rng.NextSingle() >= share || !marks.TryGetValue(three[0].Dir, out var scenes))
            {
                continue;
            }

            var center = three.MinBy(piece => three.Sum(other => Distance(lattice, piece, other)))!;
            (float x, float y) = lattice.ToWorld(center.U, center.V);
            var stripes = scenes[rng.Next(scenes.Count)]
                .Select(prop => (Prop: prop, Position: lattice.ToLattice(x + prop.Dx, y + prop.Dy)))
                .ToArray();
            if (stripes.All(stripe => floor.Contains(GetCell(stripe.Position.U, stripe.Position.V)) && !keepClear.Contains(GetCell(stripe.Position.U, stripe.Position.V))))
            {
                decor.AddRange(stripes.Select(stripe => new PlacedDecor(stripe.Prop.Id, stripe.Position.U, stripe.Position.V, stripe.Prop.Z, stripe.Prop.Dir, [], (MarkSource, index++), null, SpriteSource.CopiedScene)));
            }
        }
    }

    /// <summary>
    /// Returns whether an object at a height is one of the floor stripes laid in front of missiles.
    /// </summary>
    public static bool IsMissileMark(KnownObject id, float z) => id == KnownObject.WallInscriptions && z < WallSignHeight;

    // Seed a stream of its own from the layout
    private static Random CreateRandom(WallSet wallSet, LayoutPlan plan)
    {
        int seed = (int)wallSet;
        foreach (var cell in plan.Occupancy.Keys)
        {
            seed = unchecked(seed * 31 + (cell.U * 73856093) ^ (cell.V * 19349663));
        }

        return new Random(seed);
    }

    private static Cell GetCell(PlacedDecor piece) => GetCell(piece.U, piece.V);

    private static Cell GetCell(float u, float v) => new((int)MathF.Floor(u), (int)MathF.Floor(v));

    private static float Distance(Lattice lattice, PlacedDecor piece, PlacedDecor other)
    {
        (float x, float y) = lattice.ToWorld(piece.U, piece.V);
        (float ox, float oy) = lattice.ToWorld(other.U, other.V);
        return MathF.Sqrt((x - ox) * (x - ox) + (y - oy) * (y - oy));
    }

    private static bool IsNear(Lattice lattice, PlacedDecor piece, PlacedDecor other, float reach)
    {
        (float x, float y) = lattice.ToWorld(piece.U, piece.V);
        (float ox, float oy) = lattice.ToWorld(other.U, other.V);
        return (x - ox) * (x - ox) + (y - oy) * (y - oy) <= reach * reach;
    }
}
