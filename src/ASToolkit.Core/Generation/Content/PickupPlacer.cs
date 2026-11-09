using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Content;

internal sealed class PickupPlacer
{
    private const int PileRoomTries = 4;
    private const int ClosetTries = 3;
    private const int PileSpotTries = 12;
    private const float PileSpotStart = 0.05f;
    private const float PileSpotSpread = 0.9f;
    private const float PileClearance = 0.15f;
    private static readonly ItemCategory[] Categories = [ItemCategory.Weapon, ItemCategory.Ammo, ItemCategory.Armor, ItemCategory.Health, ItemCategory.Money, ItemCategory.Implant, ItemCategory.Bonus];
    private static readonly PointF[] PileOffsets = [new(0, 0), new(14, 6), new(-12, 8), new(4, -10), new(-6, -4), new(16, -6), new(-16, 0), new(8, 12)];

    private readonly PlacementContext _ctx;
    private readonly EconomyModel _model;
    private readonly HashSet<int> _onTour;
    private readonly PlanRegion[] _rooms;
    private readonly float[] _weights;

    public PickupPlacer(PlacementContext ctx, EconomyModel model)
    {
        _ctx = ctx;
        _model = model;
        _onTour = ctx.Plan.Stations
            .Select(station => ctx.GetRegionCells(station).Where(ctx.Distance.ContainsKey).DefaultIfEmpty().MinBy(cell => ctx.Distance.GetValueOrDefault(cell)))
            .SelectMany(ctx.TraceWalk)
            .Where(ctx.Layout.Cells.ContainsKey)
            .Select(cell => ctx.Layout.Cells[cell])
            .Concat(ctx.Plan.Stations.Select(station => station.Id))
            .ToHashSet();

        _rooms = ctx.Plan.Regions
            .Where(region => region.Kind is not (RegionKind.SecretCloset or RegionKind.SpawnCloset or RegionKind.Pen) && ctx.GetRegionCells(region).Any(ctx.Distance.ContainsKey))
            .ToArray();

        _weights = _rooms.Select(room => ctx.GetRegionCells(room).Count * (_onTour.Contains(room.Id) ? 1 : model.OffTourWeight.Value)).ToArray();
    }

    public void Place(LevelBudget budget)
    {
        var items = budget.Items
            .Where(pair => pair.Key != KnownObject.Dynamite)
            .OrderBy(pair => pair.Key)
            .SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value))
            .ToList();

        var quotas = items
            .Concat(budget.Weapons)
            .CountBy(item => item.PickupCategory!.Value)
            .ToDictionary(pair => pair.Key, pair => (int)MathF.Round(pair.Value * _model.CrateShares[pair.Key].Value));

        foreach (var weapon in budget.Weapons)
        {
            if (!_ctx.Brief.NewWeapons.Contains(weapon) || !HideWeapon(weapon, items))
            {
                items.Add(weapon);
            }
        }

        var closets = _ctx.Plan.Regions.Where(region => region.Kind == RegionKind.SecretCloset
            || region.Kind == RegionKind.SpawnCloset && _ctx.Rng.NextSingle() < _model.StockedSpawnClosets.Value).ToArray();
        foreach (var closet in closets)
        {
            StockCloset(closet, items);
        }

        FillCrates(items, quotas);
        foreach (var category in Categories)
        {
            var heap = items.Where(item => item.PickupCategory == category).ToArray();
            _ctx.Rng.Shuffle(heap);
            for (int taken = 0; taken < heap.Length;)
            {
                int size = Math.Min(heap.Length - taken, Draw(_model.PileSizes.Value));
                LayPile(heap.AsSpan(taken, size).ToArray(), _rooms, _weights);
                taken += size;
            }
        }
    }

    // Lays a new weapon off the walks to the objectives or in a secret closet, else past the start,
    private bool HideWeapon(KnownObject weapon, List<KnownObject> items)
    {
        var hiding = _ctx.Plan.Regions
            .Where(region => region.Kind == RegionKind.SecretCloset || region.Kind is not (RegionKind.SpawnCloset or RegionKind.Pen) && !_onTour.Contains(region.Id))
            .ToArray();

        var others = _rooms.Where(room => room != _ctx.Plan.Stations[0] && !hiding.Contains(room)).ToArray();
        _ctx.Rng.Shuffle(hiding);
        _ctx.Rng.Shuffle(others);
        int ammo = items.IndexOf(weapon.WeaponAmmo!.Value);
        foreach (var region in hiding.Concat(others))
        {
            var cells = _ctx.GetFreeCells(region);
            var walk = cells.Length == 0 ? [] : _ctx.TraceWalk(cells.MinBy(cell => _ctx.Distance[cell])).ToArray();
            var earlier = walk
                .Skip(walk.Length / 2)
                .Where(cell => _ctx.Layout.Cells.GetValueOrDefault(cell, -1) != region.Id && _ctx.IsFree(cell))
                .ToArray();

            var open = cells.Where(cell => IsOpenFor(weapon, cell)).ToArray();
            if (open.Length == 0 || ammo >= 0 && earlier.Length == 0)
            {
                continue;
            }

            var before = walk
                .Where(cell => _ctx.Layout.Cells.GetValueOrDefault(cell, -1) != region.Id && _ctx.IsFree(cell))
                .ToArray();

            if (ammo >= 0)
            {
                if ((_ctx.Place(earlier, items[ammo], dir: 0) ?? _ctx.Place(before, items[ammo], dir: 0)) is null)
                {
                    continue;
                }

                items.RemoveAt(ammo);
                ammo = -1;
            }

            if (_ctx.Place(open, weapon, dir: 0) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOpenFor(KnownObject id, Cell cell)
    {
        var spot = _ctx.FindSpot(cell);
        return spot is not null && _ctx.Occupancy.Fits(id, spot.Value.X, spot.Value.Y, _ctx.GetRestingHeight(id));
    }

    private void StockCloset(PlanRegion closet, List<KnownObject> items)
    {
        var cells = _ctx.GetRegionCells(closet);
        int size = Draw(_model.ClosetSizes.Value);
        for (int placed = 0, tries = 0; placed < size && tries < size * ClosetTries; tries++)
        {
            var category = Draw(_model.ClosetItems.Value);
            int[] matching = Enumerable.Range(0, items.Count).Where(index => items[index].PickupCategory == category).ToArray();
            if (matching.Length == 0)
            {
                matching = Enumerable.Range(0, items.Count).Where(index => items[index] == KnownObject.Money).ToArray();
            }

            if (matching.Length == 0)
            {
                matching = Enumerable.Range(0, items.Count).Where(index => !items[index].IsWeapon).ToArray();
            }

            if (matching.Length == 0)
            {
                return;
            }

            int chosen = matching[_ctx.Rng.Next(matching.Length)];
            if (_ctx.Place(cells, items[chosen], dir: 0, take: false) is null)
                continue;
            
            items.RemoveAt(chosen);
            placed++;
        }
    }

    private void FillCrates(List<KnownObject> items, Dictionary<ItemCategory, int> quotas)
    {
        int[] crates = Enumerable.Range(0, _ctx.Map.Sprites.Count)
            .Where(index => _ctx.Map.Sprites[index].Id == KnownObject.Crate && KnownObject.GetCrateItem(_ctx.Map.Sprites[index].Payload) == 0)
            .ToArray();

        _ctx.Rng.Shuffle(crates);
        var loot = new List<int>();
        foreach (var category in Categories)
        {
            int[] ofCategory = Enumerable.Range(0, items.Count).Where(index => items[index].PickupCategory == category).ToArray();
            _ctx.Rng.Shuffle(ofCategory);
            loot.AddRange(ofCategory.Take(quotas.GetValueOrDefault(category)));
        }

        int[] chosen = loot.ToArray();
        _ctx.Rng.Shuffle(chosen);
        chosen = chosen.Take(crates.Length).OrderDescending().ToArray();
        for (int next = 0; next < chosen.Length; next++)
        {
            _ctx.Map.Sprites[crates[next]] = _ctx.Map.Sprites[crates[next]] with { Payload = KnownObject.CreateCratePayload(items[chosen[next]]) };
            items.RemoveAt(chosen[next]);
        }
    }

    private bool LayPile(KnownObject[] heap, PlanRegion[] rooms, float[] weights)
    {
        var spots = _model.PileSpots[heap[0].PickupCategory!.Value].Value;
        var spot = Draw(spots);
        for (int tries = 0; tries < PileRoomTries; tries++)
        {
            if (PlacePile(rooms[DrawWeighted(weights)], heap, spot))
            {
                return true;
            }
        }

        return Enumerable.Range(0, rooms.Length).OrderByDescending(index => weights[index]).Any(index => PlacePile(rooms[index], heap, null));
    }

    private bool PlacePile(PlanRegion room, KnownObject[] heap, PartSpot? spot)
    {
        var cells = _ctx.GetRegionCells(room)
            .Where(c => !_ctx.Taken.Contains(c) && _ctx.Distance.ContainsKey(c) && (spot is null || GetSpot(c) == spot))
            .Select(c => (Cell: c, Point: FindPileSpot(c)))
            .Where(p => p.Point is not null && _ctx.IsVisible(p.Point.Value.X, p.Point.Value.Y) && _ctx.Occupancy.Fits(heap[0], p.Point.Value.X, p.Point.Value.Y, 0))
            .ToArray();

        if (cells.Length == 0)
        {
            return false;
        }

        var (cell, point) = cells[_ctx.Rng.Next(cells.Length)];
        var origin = point!.Value;
        var offsets = PileOffsets
            .Where(o => IsClearOfWalls(origin.X + o.X, origin.Y + o.Y) && _ctx.IsVisible(origin.X + o.X, origin.Y + o.Y) && _ctx.Occupancy.Fits(heap[0], origin.X + o.X, origin.Y + o.Y, 0)
                        && (spot is null or PartSpot.Center || _ctx.Layout.Lattice.GetCell(origin.X + o.X, origin.Y + o.Y) == cell))
            .DefaultIfEmpty(PointF.Empty)
            .ToArray();

        for (int index = 0; index < heap.Length; index++)
        {
            var offset = offsets[index % offsets.Length];
            _ctx.Map.Add(heap[index], origin.X + offset.X, origin.Y + offset.Y, _ctx.GetRestingHeight(heap[index]), 0, unique: false);
            _ctx.Occupancy.Add(heap[index], origin.X + offset.X, origin.Y + offset.Y, _ctx.GetRestingHeight(heap[index]));
            _ctx.ShadePickup(heap[index], origin.X + offset.X, origin.Y + offset.Y);
        }

        _ctx.Taken.Add(cell);
        return true;
    }

    private PointF? FindPileSpot(Cell cell)
    {
        for (int index = 0; index < PileSpotTries; index++)
        {
            var point = _ctx.Layout.Lattice.ToWorld(cell.U + PileSpotStart + PileSpotSpread * _ctx.Rng.NextSingle(), cell.V + PileSpotStart + PileSpotSpread * _ctx.Rng.NextSingle());
            if (IsClearOfWalls(point.X, point.Y))
            {
                return point;
            }
        }

        return null;
    }

    private bool IsClearOfWalls(float x, float y)
    {
        (float u, float v) = _ctx.Layout.Lattice.ToLattice(x, y);
        return _ctx.Layout.Edges.MeasureClearance(u, v) >= PileClearance;
    }

    private PartSpot GetSpot(Cell cell)
    {
        var sides = _ctx.Layout.Edges.GetWalledSides(cell, _ctx.Layout.Cells.ContainsKey, static kind => kind.IsClosed);
        return sides.IsCorner ? PartSpot.Corner : sides != WallSides.None ? PartSpot.Wall : PartSpot.Center;
    }

    private T Draw<T>(IReadOnlyList<T> sample) => sample[_ctx.Rng.Next(sample.Count)];

    private int DrawWeighted(float[] weights)
    {
        float roll = _ctx.Rng.NextSingle() * weights.Sum();
        for (int index = 0; index < weights.Length; index++)
        {
            roll -= weights[index];
            if (roll < 0)
            {
                return index;
            }
        }

        return weights.Length - 1;
    }
}
