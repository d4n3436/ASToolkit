using ASToolkit.Core.Formats;
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
    private const float PixelsPerRate = 1_000_000;
    private const int FirstIndoorLevel = 2;
    private const int MinRooms = 4;
    private const double MaxDeviations = 1.5;

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
        var themes = DrawThemes(rng);
        var gates = DrawGates(rng, themes);
        float[] logHitPoints = DrawRising(rng, _model.HitPoints.Value, Enumerable.Range(FirstIndoorLevel, count - 1).ToDictionary(level => level, level => (float)level));
        float[] logEndMoney = DrawRising(rng, _model.EndMoney.Value, Enumerable.Range(1, count - 1).ToDictionary(level => level, level => MathF.Log(level)));
        int[] floors = new int[count + 1];
        int[] rooms = new int[count + 1];
        float[] spawnedShares = new float[count + 1];
        int[] closets = new int[count + 1];
        var counts = new Dictionary<ItemCategory, int>[count + 1];
        counts[1] = [];
        for (int level = FirstIndoorLevel; level <= count; level++)
        {
            floors[level] = (int)Math.Exp(DrawOnLine(rng, _model.FloorPixels.Value, level, themes[level]));
            rooms[level] = DrawRooms(rng, level, themes[level], floors[level]);
            spawnedShares[level] = 1 / (1 + MathF.Exp(-Draw(rng, _model.SpawnedShare.Value, level)));
            float rates = floors[level] / PixelsPerRate;
            counts[level] = _model.PickupRates.ToDictionary(pair => pair.Key, pair => (int)MathF.Round(Math.Max(0, Draw(rng, pair.Value.Value, level)) * rates));
            closets[level] = themes[level] == LevelTheme.Lab ? (int)MathF.Round(Math.Max(0, Draw(rng, _model.SecretClosets.Value, level)) * rates) : 0;
        }

        int[] endMoney = Enumerable.Range(0, count + 1)
            .Select(level => level >= 1 && level < count ? (int)MathF.Round(MathF.Exp(logEndMoney[level])) : 0)
            .ToArray();

        int[] cash = Enumerable.Range(0, count + 1)
            .Select(level => level == 1 ? first.Money : level >= FirstIndoorLevel ? counts[level].GetValueOrDefault(ItemCategory.Money) * _model.CashValue : 0)
            .ToArray();

        var weaponLevels = DrawWeaponLevels(rng, endMoney, cash);
        var briefs = new List<LevelBrief>
        {
            new()
            {
                Level = 1,
                Theme = first.Theme,
                GateStyle = first.GateStyle,
                HitPoints = first.HitPoints,
                SpawnedShare = 0,
                FloorPixels = 0,
                Rooms = 0,
                SecretClosets = 0,
                Arsenal = [],
                NewWeapons = [],
                Weapons = [],
                Pickups = new Dictionary<KnownObject, int>(),
                EndMoney = endMoney[1],
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
            briefs.Add(new LevelBrief
            {
                Level = level,
                Theme = themes[level],
                GateStyle = gates[level],
                HitPoints = hitPoints,
                SpawnedShare = spawnedShares[level],
                FloorPixels = floors[level],
                Rooms = rooms[level],
                SecretClosets = closets[level],
                Arsenal = arsenal,
                NewWeapons = newWeapons,
                Weapons = weapons,
                Pickups = DrawPickups(rng, level, counts[level], arsenal, next == default ? null : next),
                EndMoney = endMoney[level],
                DonorLevel = ChooseDonor(themes[level], gates[level], hitPoints).Name
            });
        }

        return new CampaignPlan { Levels = briefs };
    }

    private int DrawRooms(Random rng, int level, LevelTheme theme, int floorPixels)
    {
        double rate = DrawOnLine(rng, _model.Rooms.Value, level, theme);
        if (_model.RoomRates.TryGetValue(theme, out var range))
        {
            rate = Math.Clamp(rate, range.Min, range.Max);
        }

        int rooms = (int)Math.Round(floorPixels * Math.Exp(rate) / PixelsPerRate);
        return Math.Clamp(rooms, MinRooms, Math.Max(MinRooms, _model.MaxRooms.GetValueOrDefault(theme, rooms)));
    }

    private static double DrawOnLine(Random rng, ThemeLine line, int level, LevelTheme theme) =>
        line.Evaluate(level, theme == LevelTheme.Military) + Math.Clamp(Normal.Sample(rng, 0, 1), -MaxDeviations, MaxDeviations) * line.Spread;

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

    // Put the lab levels first, as many as the lab share gives give or take one, with at least one level of each theme the campaigns show
    private LevelTheme[] DrawThemes(Random rng)
    {
        int count = _model.LevelCount;
        int indoor = count - 1;
        bool hasLab = _model.Gates.ContainsKey(LevelTheme.Lab);
        bool hasMilitary = _model.Gates.ContainsKey(LevelTheme.Military);
        int labs = (int)MathF.Round(_model.LabShare.Value * indoor) + rng.Next(-1, 2);
        labs = Math.Clamp(labs, hasMilitary ? (hasLab ? 1 : 0) : indoor, hasLab ? (hasMilitary ? indoor - 1 : indoor) : 0);
        return Enumerable.Range(0, count + 1)
            .Select(level => level < FirstIndoorLevel ? LevelTheme.Outdoor : level < FirstIndoorLevel + labs ? LevelTheme.Lab : LevelTheme.Military)
            .ToArray();
    }

    // Draw each level's gate style from its theme's and order each theme's levels the way the campaigns introduce them
    private LevelStyle[] DrawGates(Random rng, LevelTheme[] themes)
    {
        var gates = new LevelStyle[themes.Length];
        foreach (var run in Enumerable.Range(FirstIndoorLevel, themes.Length - FirstIndoorLevel).GroupBy(level => themes[level]))
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

    // Take the original of the level's wall theme nearest in hit points, of the same gate style when there is one
    private DonorCandidate ChooseDonor(LevelTheme theme, LevelStyle gate, int hitPoints) => _model.Donors
        .Where(donor => donor.Theme == theme)
        .OrderBy(donor => donor.GateStyle == gate ? 0 : 1)
        .ThenBy(donor => MathF.Abs(MathF.Log(Math.Max(1, donor.HitPoints)) - MathF.Log(Math.Max(1, hitPoints))))
        .ThenBy(donor => donor.Name, StringComparer.Ordinal)
        .First();
}
