using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using MathNet.Numerics.Distributions;

namespace ASToolkit.Core.Generation.Campaign;

/// <summary>
/// Provides the drawing of a whole generated campaign from a campaign model, giving each level a brief whose hit points and end money never
/// fall from one level to the next and whose weapons come in shop-price order.
/// </summary>
public sealed class CampaignPlanner
{
    private const float MinMonsterWeight = 0.01f;
    private const int CenterSteps = 30;
    private const float PixelsPerRate = 1_000_000;
    private const int FirstIndoorLevel = 2;
    private const int CannonLevelMin = 7;
    private const int CannonLevelMax = 8;
    private const int MinRooms = 4;
    private const int FirstLevelMostRooms = 12;
    private const int LabMostRooms = 28;
    private const int LockedLabMostRooms = 24;
    private const int UngatedLabMostRooms = 22;
    private const int LabLeastRooms = 19;
    private const int IntroLabLeastRooms = 9;
    private const int IntroLabMostRooms = 14;
    private const double MaxDeviations = 1.5;

    private const int FirstWarehouseLevel = 6;

    private static readonly LevelStyle[] OpeningGates = [LevelStyle.LockedDoors, LevelStyle.LockedDoors, LevelStyle.Blackout];

    private static readonly ItemCategory[] SharedCategories = [ItemCategory.Health, ItemCategory.Armor, ItemCategory.Implant, ItemCategory.Bonus];

    private readonly CampaignModel _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="CampaignPlanner"/> class.
    /// </summary>
    public CampaignPlanner(CampaignModel model) => _model = model;

    /// <summary>
    /// Draws the briefs of a campaign, its threat and end money never falling from one level to the next and its weapons coming in price order.
    /// </summary>
    public CampaignPlan Plan(Random rng)
    {
        int count = _model.LevelCount;
        var first = _model.FirstLevels[rng.Next(_model.FirstLevels.Count)];
        var buildings = DrawBuildings(rng);

        var wallSets = buildings
            .Select(ReferenceLevelSet.GetFloorSet)
            .ToArray();

        var gates = DrawGates(rng, wallSets);
        foreach (int level in Enumerable.Range(0, buildings.Length).Where(level => buildings[level] == Building.OfficeBlock))
        {
            gates[level] = LevelStyle.Blackout;
        }

        var wings = DrawLabWings(rng, buildings, gates);
        float[] logHitPoints = DrawRising(rng, _model.HitPoints.Value, Enumerable.Range(FirstIndoorLevel, count - 1).ToDictionary(level => level, level => MathF.Log(level)));
        float[] logEndMoney = DrawRising(rng, _model.EndMoney.Value, Enumerable.Range(1, count - 1).ToDictionary(level => level, level => MathF.Log(level)));
        int[] floors = new int[count + 1];
        int[] rooms = new int[count + 1];
        float[] spawnedShares = new float[count + 1];
        int[] closets = new int[count + 1];
        var counts = new Dictionary<ItemCategory, int>[count + 1];
        counts[1] = [];
        for (int level = FirstIndoorLevel; level <= count; level++)
        {
            floors[level] = (int)Math.Exp(DrawOnLine(rng, _model.FloorPixels.Value, level, wallSets[level]));
            rooms[level] = DrawRooms(rng, level, wallSets[level], floors[level]);
            if (level == FirstIndoorLevel && _model.Style == GenerationStyle.Campaign)
            {
                rooms[level] = Math.Min(rooms[level], FirstLevelMostRooms);
            }
            spawnedShares[level] = 1 / (1 + MathF.Exp(-Draw(rng, _model.SpawnedShare.Value, level)));
            float rates = floors[level] / PixelsPerRate;
            counts[level] = _model.PickupRates.ToDictionary(pair => pair.Key, pair => (int)MathF.Round(Math.Max(0, Draw(rng, pair.Value.Value, level)) * rates));
            closets[level] = wallSets[level] == WallSet.Basement ? (int)MathF.Round(Math.Max(0, Draw(rng, _model.SecretClosets.Value, level)) * rates) : 0;
        }

        int[] endMoney = Enumerable.Range(0, count + 1)
            .Select(level => level >= 1 && level < count ? (int)MathF.Round(MathF.Exp(logEndMoney[level])) : 0)
            .ToArray();

        int[] cash = Enumerable.Range(0, count + 1)
            .Select(level => level == 1 ? first.Money : level >= FirstIndoorLevel ? counts[level].GetValueOrDefault(ItemCategory.Money) * _model.CashValue : 0)
            .ToArray();

        var weaponLevels = DrawWeaponLevels(rng, endMoney, cash);
        int firstMonsters = _model.Monsters.Select(kind => kind.FirstLevel).DefaultIfEmpty(0).Min();
        var briefs = new List<LevelBrief>
        {
            new()
            {
                Level = 1,
                Building = Building.Grounds,
                WallSet = first.WallSet,
                GateStyle = first.GateStyle,
                IsIntro = false,
                HitPoints = first.HitPoints,
                SpawnedShare = 0,
                FloorPixels = 0,
                Rooms = 0,
                SecretClosets = 0,
                Arsenal = [],
                NewWeapons = [],
                Weapons = [],
                Pickups = new Dictionary<KnownObject, int>(),
                Monsters = new Dictionary<KnownObject, float>(),
                NewMonsters = [],
                Bosses = [],
                EndMoney = endMoney[1],
                MountedCannons = 0,
                DonorLevel = first.Name
            }
        };

        for (int level = FirstIndoorLevel; level <= count; level++)
        {
            var arsenal = _model.Weapons
                .Where(weapon => weaponLevels[weapon] <= level)
                .ToArray();

            var newWeapons = arsenal
                .Where(weapon => weaponLevels[weapon] == level)
                .ToArray();

            var older = arsenal
                .Except(newWeapons)
                .ToArray();

            int repeats = older.Length == 0 ? 0 : (int)MathF.Round(Math.Max(0, Draw(rng, _model.RepeatedWeapons.Value, level)));
            var weapons = newWeapons
                .Concat(Enumerable.Range(0, repeats).Select(_ => older[rng.Next(older.Length)]))
                .ToArray();

            var next = _model.Weapons.FirstOrDefault(weapon => weaponLevels[weapon] > level);
            int hitPoints = (int)MathF.Round(MathF.Exp(logHitPoints[level]));
            var monsters = GetMonsters(level);
            briefs.Add(new LevelBrief
            {
                Level = level,
                Building = buildings[level],
                WallSet = wallSets[level],
                GateStyle = gates[level],
                IsIntro = IsIntro(level, gates[level]),
                HitPoints = hitPoints,
                SpawnedShare = spawnedShares[level],
                FloorPixels = floors[level],
                Rooms = SizeLab(rooms[level], wallSets[level], IsIntro(level, gates[level]), gates[level]),
                SecretClosets = closets[level],
                Arsenal = arsenal,
                NewWeapons = newWeapons,
                Weapons = weapons,
                Pickups = DrawPickups(rng, level, counts[level], arsenal, next == default ? null : next),
                Monsters = monsters,
                NewMonsters = _model.Monsters.Where(kind => kind.FirstLevel == level && level > firstMonsters && monsters.ContainsKey(kind.Id)).Select(kind => kind.Id).ToArray(),
                Bosses = DrawBosses(rng, level),
                EndMoney = endMoney[level],
                SecondWing = wings[level],
                MountedCannons = _model.Style == GenerationStyle.Campaign ? buildings[level] == Building.Warehouse && level is CannonLevelMin or CannonLevelMax ? 1 : 0 : null,
                DonorLevel = ChooseDonor(wallSets[level], gates[level], hitPoints).Name
            });
        }

        return new CampaignPlan { Levels = briefs };
    }

    private Dictionary<KnownObject, float> GetMonsters(int level)
    {
        var line = _model.MonsterHitPoints.Value;
        var kinds = _model.Monsters
            .Where(kind => kind.FirstLevel <= level)
            .ToArray();

        float target = MathF.Exp(line.Evaluate(level));
        float low = kinds.Select(kind => MathF.Log(Math.Max(1, kind.HitPoints))).DefaultIfEmpty(0).Min() - 3 * line.Spread;
        float high = kinds.Select(kind => MathF.Log(Math.Max(1, kind.HitPoints))).DefaultIfEmpty(0).Max() + 3 * line.Spread;
        for (int step = 0; step < CenterSteps; step++)
        {
            float center = (low + high) / 2;
            var weighed = Weigh(center);
            if (weighed.Sum(pair => pair.Value * pair.Key.HitPoints) / Math.Max(1e-9f, weighed.Sum(pair => pair.Value)) < target)
            {
                low = center;
            }
            else
            {
                high = center;
            }
        }

        var weights = Weigh((low + high) / 2).ToDictionary(pair => pair.Key.Id, pair => pair.Value);

        float heaviest = weights.Values.DefaultIfEmpty(0).Max();
        return weights
            .Where(pair => pair.Value >= heaviest * MinMonsterWeight)
            .ToDictionary(pair => pair.Key, pair => pair.Value / heaviest);

        Dictionary<MonsterKind, float> Weigh(float center) => kinds.ToDictionary(kind => kind,
            kind => MathF.Exp(-MathF.Pow(MathF.Log(Math.Max(1, kind.HitPoints)) - center, 2) / (2 * line.Spread * line.Spread)));
    }

    private KnownObject[] DrawBosses(Random rng, int level)
    {
        var bosses = _model.Bosses
            .Where(kind => kind.FirstLevel <= level)
            .ToArray();

        return bosses.Length > 0 && rng.NextSingle() < _model.BossShare.Value ? [bosses.MaxBy(kind => kind.FirstLevel).Id] : [];
    }

    private int DrawRooms(Random rng, int level, WallSet wallSet, int floorPixels)
    {
        double rate = DrawOnLine(rng, _model.Rooms.Value, level, wallSet);
        if (_model.RoomRates.TryGetValue(wallSet, out var range))
        {
            rate = Math.Clamp(rate, range.Min, range.Max);
        }

        int rooms = (int)Math.Round(floorPixels * Math.Exp(rate) / PixelsPerRate);
        int most = wallSet == WallSet.Basement ? LabMostRooms : _model.MaxRooms.GetValueOrDefault(wallSet, rooms);
        return Math.Clamp(rooms, MinRooms, Math.Max(MinRooms, most));
    }

    private static int SizeLab(int rooms, WallSet wallSet, bool isIntro, LevelStyle gate) => wallSet != WallSet.Basement ? rooms
        : isIntro ? Math.Clamp(rooms, IntroLabLeastRooms, IntroLabMostRooms)
        : Math.Clamp(rooms, LabLeastRooms, gate switch
        {
            LevelStyle.LockedDoors => LockedLabMostRooms,
            LevelStyle.Ungated => UngatedLabMostRooms,
            _ => LabMostRooms
        });

    private static double DrawOnLine(Random rng, WallSetLine line, int level, WallSet wallSet) =>
        line.Evaluate(level, wallSet == WallSet.Concrete) + Math.Clamp(Normal.Sample(rng, 0, 1), -MaxDeviations, MaxDeviations) * line.Spread;

    private static float Draw(Random rng, LinearFit line, float x) => line.Spread > 0 ? (float)Normal.Sample(rng, line.Evaluate(x), line.Spread) : line.Evaluate(x);

    // Draw a value around the line at each point and hand the values out from the lowest in the order of the points
    private static float[] DrawRising(Random rng, LinearFit line, IReadOnlyDictionary<int, float> points)
    {
        float[] values = new float[points.Keys.Max() + 1];
        float[] drawn = points.Values
            .Select(x => Draw(rng, line, x))
            .Order()
            .ToArray();

        foreach ((int point, float value) in points.Keys.Order().Zip(drawn))
        {
            values[point] = value;
        }

        return values;
    }

    private static int Pick(Random rng, IReadOnlyList<float> weights)
    {
        float roll = rng.NextSingle() * weights.Sum();
        for (int index = 0; index < weights.Count; index++)
        {
            roll -= weights[index];
            if (roll < 0)
            {
                return index;
            }
        }

        return weights.Count - 1;
    }

    private static bool IsIntro(int level, LevelStyle gate) => level == FirstIndoorLevel && gate != LevelStyle.Ungated;

    // Gives each lab level a second wing of its own among those its gate builds, the levels with the fewest choices first
    private static LabWing?[] DrawLabWings(Random rng, Building[] buildings, LevelStyle[] gates)
    {
        var wings = new LabWing?[buildings.Length];

        var used = new HashSet<LabWing>();
        foreach (int level in Enumerable.Range(0, buildings.Length).Where(level => Choices(level).Count > 0).OrderBy(level => Choices(level).Count).ThenBy(level => level))
        {
            var free = Choices(level).Where(wing => !used.Contains(wing)).ToArray();
            if (free.Length > 0)
            {
                var wing = free[rng.Next(free.Length)];
                wings[level] = wing;
                used.Add(wing);
            }
        }

        return wings;

        IReadOnlyList<LabWing> Choices(int level) => buildings[level] == Building.UndergroundLab
            ? BuildingGrammar.GetSecondWings(gates[level], IsIntro(level, gates[level]))
            : [];
    }

    // Builds the campaign style's labs and warehouses at their fixed levels; in add-on style draws the buildings' shares around the fitted ones and
    // hands the levels out along them, moving a level to a required building that drew none
    private Building[] DrawBuildings(Random rng)
    {
        var buildings = new Building[_model.LevelCount + 1];
        buildings[1] = Building.Grounds;
        if (_model.Style == GenerationStyle.Campaign)
        {
            for (int number = FirstIndoorLevel; number <= _model.LevelCount; number++)
            {
                buildings[number] = number < FirstWarehouseLevel ? Building.UndergroundLab : Building.Warehouse;
            }

            return buildings;
        }

        var shares = _model.Buildings.Value;

        double[] drawn = shares
            .Select(share => Gamma.Sample(rng, share.Samples, 1))
            .ToArray();

        double total = drawn.Sum();
        double offset = rng.NextDouble();
        int indoor = _model.LevelCount - 1;
        int[] counts = new int[shares.Count];
        int index = 0;
        double reach = drawn[0] / total;
        for (int step = 0; step < indoor; step++)
        {
            double point = (step + offset) / indoor;
            while (point >= reach && index < shares.Count - 1)
            {
                index++;
                reach += drawn[index] / total;
            }

            counts[index]++;
        }

        for (int required = 0; required < shares.Count; required++)
        {
            if (shares[required].IsRequired && counts[required] == 0)
            {
                counts[Array.IndexOf(counts, counts.Max())]--;
                counts[required]++;
            }
        }

        int level = FirstIndoorLevel;
        for (int share = 0; share < shares.Count; share++)
        {
            for (int count = 0; count < counts[share]; count++)
            {
                buildings[level++] = shares[share].Building;
            }
        }

        return buildings;
    }

    // Draws each level's gate style from its wall set's and orders each wall set's levels the way the campaigns introduce them
    private LevelStyle[] DrawGates(Random rng, WallSet[] wallSets)
    {
        var gates = new LevelStyle[wallSets.Length];
        bool opens = _model.Style == GenerationStyle.Campaign && wallSets.Skip(FirstIndoorLevel).Take(OpeningGates.Length).All(wallSet => wallSet == WallSet.Basement);
        int past = opens ? FirstIndoorLevel + OpeningGates.Length : FirstIndoorLevel;
        if (opens)
        {
            OpeningGates.CopyTo(gates, FirstIndoorLevel);
        }

        foreach (var run in Enumerable.Range(past, wallSets.Length - past).GroupBy(level => wallSets[level]))
        {
            var shares = _model.Gates[run.Key].Value;
            int[] drawn = run
                .Select(_ => Pick(rng, shares.Select(share => share.Share).ToArray()))
                .Order()
                .ToArray();

            foreach ((int level, int index) in run.Zip(drawn))
            {
                gates[level] = shares[index].GateStyle;
            }
        }

        return gates;
    }

    // Hand the levels drawn around the line out in price order, none before the one before it nor before the money paid and picked up
    // reaches the fitted share of its price
    private Dictionary<KnownObject, int> DrawWeaponLevels(Random rng, int[] endMoney, int[] cash)
    {
        int count = _model.LevelCount;
        var levels = new Dictionary<KnownObject, int>();
        float[] drawn = DrawRising(rng, _model.WeaponLevels.Value, Enumerable.Range(1, _model.Weapons.Count).ToDictionary(rank => rank, rank => (float)rank));
        int previous = FirstIndoorLevel;
        for (int rank = 1; rank <= _model.Weapons.Count; rank++)
        {
            var weapon = _model.Weapons[rank - 1];
            int affordable = FirstIndoorLevel;
            int earned = endMoney[1] + cash[1];
            while (affordable <= count && earned < _model.Affordability.Value * _model.WeaponPrices[weapon])
            {
                earned += endMoney[affordable] + cash[affordable];
                affordable++;
            }

            previous = Math.Max(Math.Max(previous, (int)MathF.Round(drawn[rank])), affordable);
            levels[weapon] = previous;
        }

        return levels;
    }

    private Dictionary<KnownObject, int> DrawPickups(Random rng, int level, Dictionary<ItemCategory, int> counts, KnownObject[] arsenal, KnownObject? next)
    {
        var pickups = new Dictionary<KnownObject, int>();
        if (counts.GetValueOrDefault(ItemCategory.Money) > 0)
        {
            pickups[KnownObject.Money] = counts[ItemCategory.Money];
        }

        foreach (var category in SharedCategories)
        {
            var ids = _model.PickupShares.Keys
                .Where(id => id.PickupCategory == category)
                .ToArray();

            float[] shares = ids
                .Select(id => Math.Max(0, _model.PickupShares[id].Value.Evaluate(level)))
                .ToArray();

            if (shares.Sum() <= 0)
            {
                continue;
            }

            for (int index = 0; index < counts.GetValueOrDefault(category); index++)
            {
                var id = ids[Pick(rng, shares)];
                pickups[id] = pickups.GetValueOrDefault(id) + 1;
            }
        }

        // Age 0 is the weapon that comes next and age 1 the newest weapon
        var byAge = (next is null ? [] : new[] { next.Value })
            .Concat(arsenal.Reverse())
            .ToArray();

        var ages = _model.AmmoAges.Value;
        float[] weights = Enumerable.Range(0, byAge.Length)
            .Select(index => (next is null ? index + 1 : index) < ages.Count ? ages[next is null ? index + 1 : index] : 0)
            .ToArray();

        if (weights.Sum() > 0)
        {
            for (int index = 0; index < counts.GetValueOrDefault(ItemCategory.Ammo); index++)
            {
                var ammo = byAge[Pick(rng, weights)].WeaponAmmo!.Value;
                pickups[ammo] = pickups.GetValueOrDefault(ammo) + 1;
            }
        }

        return pickups;
    }

    // Takes the original of the level's wall set nearest in hit points, of the same gate style when there is one
    private DonorCandidate ChooseDonor(WallSet wallSet, LevelStyle gate, int hitPoints) => _model.Donors
        .Where(donor => donor.WallSet == wallSet)
        .OrderBy(donor => donor.GateStyle == gate ? 0 : 1)
        .ThenBy(donor => MathF.Abs(MathF.Log(Math.Max(1, donor.HitPoints)) - MathF.Log(Math.Max(1, hitPoints))))
        .ThenBy(donor => donor.Name, StringComparer.Ordinal)
        .First();
}
