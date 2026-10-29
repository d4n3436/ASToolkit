using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Provides the blood density, room signatures and battle aftermath of a level's decor.
/// </summary>
public static class RoomDecorator
{
    private const float BloodPerCell = 0.05f;
    private const float CorpsePerCell = 0.002f;
    private const int MinRoomCells = 20;
    private const int PlantSource = -3;
    private static readonly (int Du, int Dv)[] Adjacent = [(0, 0), (-1, 0), (1, 0), (0, -1), (0, 1)];

    private static readonly KnownObject[] BloodIds = [KnownObject.SmallBloodPool, KnownObject.BloodPool, KnownObject.VerticalBloodPool, KnownObject.LargeBloodPool];
    private static readonly KnownObject[] CorpseIds = [KnownObject.ScientistCorpse, KnownObject.SoldierCorpse];

    private sealed record Family(KnownObject[] Pieces, KnownObject[] Detect);

    /// <summary>
    /// Thins the blood and corpses, adds pieces of one decor family to each room and leaves blood and a corpse in the last two stations.
    /// </summary>
    public static void Decorate(LevelTheme theme, LayoutPlan plan, FineLayout layout, List<PlacedDecor> decor,
        IReadOnlySet<Cell> floor, IReadOnlySet<Cell> keepClear, DecorHeightTable themeDecor, FacingTable facings)
    {
        var random = CreateRandom(theme, plan);
        var taken = new HashSet<Cell>();
        foreach (var piece in decor)
        {
            taken.Add(GetCell(piece));
        }

        int index = 0;

        var blood = decor
            .Where(piece => BloodIds.Contains(piece.Id))
            .ToArray();
        
        int bloodCap = (int)(floor.Count * BloodPerCell);
        if (blood.Length > bloodCap)
        {
            var drop = new HashSet<PlacedDecor>();
            while (drop.Count < blood.Length - bloodCap)
            {
                drop.Add(blood[random.Next(blood.Length)]);
            }

            foreach (var cell in blood.Where(drop.Contains).Select(GetCell).Distinct())
            {
                taken.Remove(cell);
            }

            decor.RemoveAll(drop.Contains);
        }

        var corpses = decor
            .Where(piece => CorpseIds.Contains(piece.Id))
            .ToArray();
        
        int corpseCap = (int)(floor.Count * CorpsePerCell);
        if (corpses.Length > corpseCap)
        {
            var drop = new HashSet<PlacedDecor>();
            while (drop.Count < corpses.Length - corpseCap)
            {
                drop.Add(corpses[random.Next(corpses.Length)]);
            }

            decor.RemoveAll(drop.Contains);
        }

        // Give each room a cluster of pieces of the family its decor shows, or of the next family in turn
        var families = GetFamilies(theme)
            .OrderBy(_ => random.Next())
            .ToArray();
        
        int schedule = 0;
        var rooms = plan.Regions
            .Where(r => r.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing
                && r.Id != plan.Stations[0].Id && r.Id != plan.Stations[^1].Id && r.Id != plan.KeyRoom?.Id)
            .Where(r => layout.GetRegionCells(r.Id).Count >= MinRoomCells)
            .ToArray();
        
        foreach (var region in rooms)
        {
            var cells = layout.GetRegionCells(region.Id);
            var family = DetectFamily(decor, cells, families) ?? families[(schedule++) % families.Length];
            int pieces = Math.Clamp(cells.Count / 80, 1, 8);
            var free = cells.Where(cell => IsUsable(cell) && !taken.Contains(cell)).ToList();
            if (free.Count == 0)
            {
                continue;
            }

            var anchor = free[random.Next(free.Count)];
            for (int pieceIndex = 0; pieceIndex < pieces; pieceIndex++)
            {
                if (free.Count == 0)
                {
                    break;
                }

                int best = 0;
                for (int candidate = 1; candidate < free.Count; candidate++)
                {
                    float bestDistance = Math.Abs(free[best].U - anchor.U) + Math.Abs(free[best].V - anchor.V);
                    float distance = Math.Abs(free[candidate].U - anchor.U) + Math.Abs(free[candidate].V - anchor.V) + random.NextSingle() * 0.5f;
                    if (distance < bestDistance)
                    {
                        best = candidate;
                    }
                }

                Add(family.Pieces[random.Next(family.Pieces.Length)], free[best]);
                free.RemoveAt(best);
            }
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

            if (station == denIndex && spots.Length > bloodCount)
            {
                Add(theme == LevelTheme.Lab ? KnownObject.ScientistCorpse : KnownObject.SoldierCorpse, spots[bloodCount]);
            }
        }

        return;

        bool IsUsable(Cell cell) => floor.Contains(cell) && !layout.IsWalled(cell) && !keepClear.Contains(cell);

        void Add(KnownObject id, Cell cell)
        {
            if (!themeDecor.Contains(id))
            {
                return;
            }

            byte dir = facings.PickFacing(random, id, layout.Edges, cell.U + 0.5f, cell.V + 0.5f);
            byte[] payload = id == KnownObject.Crate ? new byte[12] : [];
            decor.Add(new PlacedDecor(id, cell.U + 0.5f, cell.V + 0.5f, themeDecor.GetMiddleHeight(id), dir, payload, (PlantSource, index++), null));
            taken.Add(cell);
        }
    }

    /// <summary>
    /// Puts a table under every computer that has none next to it.
    /// </summary>
    public static void PlaceComputerTables(LevelTheme theme, LayoutPlan plan, FineLayout layout, List<PlacedDecor> decor,
        DecorHeightTable themeDecor, FacingTable facings)
    {
        var random = CreateRandom(theme, plan);
        foreach (var computer in decor.Where(piece => piece.Id is KnownObject.Computer or KnownObject.ComputerDead).ToArray())
        {
            var cell = GetCell(computer);
            if (HasAdjacent(decor, KnownObject.Table, cell) || !themeDecor.Contains(KnownObject.Table))
            {
                continue;
            }

            byte dir = facings.PickFacing(random, KnownObject.Table, layout.Edges, cell.U + 0.5f, cell.V + 0.5f);
            decor.Add(new PlacedDecor(KnownObject.Table, cell.U + 0.5f, cell.V + 0.5f, themeDecor.GetMiddleHeight(KnownObject.Table), dir, [], (PlantSource, decor.Count), null));
        }
    }

    /// <summary>
    /// Returns the decor without the computers that have no table next to them.
    /// </summary>
    public static PlacedDecor[] RemoveComputersOffTables(IReadOnlyList<PlacedDecor> decor)
    {
        var tables = decor
            .Where(piece => piece.Id == KnownObject.Table)
            .ToList();

        return decor
            .Where(piece => piece.Id is not (KnownObject.Computer or KnownObject.ComputerDead) || HasAdjacent(tables, KnownObject.Table, GetCell(piece)))
            .ToArray();
    }

    // Seed a stream of its own from the layout
    private static Random CreateRandom(LevelTheme theme, LayoutPlan plan)
    {
        int seed = (int)theme;
        foreach (var cell in plan.Occupancy.Keys)
        {
            seed = unchecked(seed * 31 + (cell.U * 73856093) ^ (cell.V * 19349663));
        }

        return new Random(seed);
    }

    private static Cell GetCell(PlacedDecor piece) => new((int)MathF.Floor(piece.U), (int)MathF.Floor(piece.V));

    private static bool HasAdjacent(List<PlacedDecor> decor, KnownObject id, Cell center)
    {
        foreach ((int du, int dv) in Adjacent)
        {
            var cell = new Cell(center.U + du, center.V + dv);
            if (decor.Any(piece => piece.Id == id && GetCell(piece) == cell))
            {
                return true;
            }
        }

        return false;
    }

    private static Family? DetectFamily(List<PlacedDecor> decor, IReadOnlyList<Cell> cells, Family[] families)
    {
        var cellSet = cells.ToHashSet();
        int best = 0;
        Family? bestFamily = null;
        foreach (var family in families)
        {
            int count = 0;
            foreach (var piece in decor)
            {
                if (cellSet.Contains(GetCell(piece)) && family.Detect.Contains(piece.Id))
                {
                    count++;
                }
            }

            if (count > best)
            {
                best = count;
                bestFamily = family;
            }
        }

        return bestFamily;
    }

    private static Family[] GetFamilies(LevelTheme theme) => theme == LevelTheme.Lab ?
        [
            new Family([KnownObject.Table, KnownObject.Chair, KnownObject.ComputerDead, KnownObject.Cabinet, KnownObject.Chair, KnownObject.ScatteredPaper, KnownObject.Plant], [KnownObject.Table, KnownObject.Chair, KnownObject.Cabinet]),
            new Family([KnownObject.TestTubes, KnownObject.Cylinder, KnownObject.Computer, KnownObject.Cabinet, KnownObject.WallInscriptions, KnownObject.MonitorDead], [KnownObject.TestTubes, KnownObject.Cylinder]),
            new Family([KnownObject.Barrel, KnownObject.Shelves, KnownObject.Crate, KnownObject.Cylinder, KnownObject.WaterCylinder], [KnownObject.Barrel, KnownObject.Shelves, KnownObject.Crate])
        ] :
        [
            new Family([KnownObject.Crate, KnownObject.Barrel, KnownObject.Shelves, KnownObject.Cylinder], [KnownObject.Crate, KnownObject.Barrel, KnownObject.Shelves]),
            new Family([KnownObject.Crate, KnownObject.Cylinder, KnownObject.RadioactiveBarrel, KnownObject.Shelves], [KnownObject.RadioactiveBarrel, KnownObject.Crate]),
            new Family([KnownObject.Table, KnownObject.Chair, KnownObject.ComputerDead, KnownObject.Cabinet], [KnownObject.Table, KnownObject.Chair, KnownObject.Cabinet])
        ];
}
