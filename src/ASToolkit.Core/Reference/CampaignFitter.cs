using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the fitting of a campaign model to a style's original campaigns, measuring each level's threat, size, gate, weapons, pickups and
/// end money and fitting a line or a set of shares to each over the levels.
/// </summary>
public sealed class CampaignFitter
{
    private const float MinSpawnedShare = 0.02f;
    private const float PixelsPerRate = 1_000_000;
    private const int FirstWeaponSlot = 2;

    private static readonly KnownObject[] ExcludedMonsters = [KnownObject.Diver];

    private static readonly ItemCategory[] RatedCategories = [ItemCategory.Health, ItemCategory.Armor, ItemCategory.Ammo, ItemCategory.Money, ItemCategory.Implant, ItemCategory.Bonus];
    private static readonly ItemCategory[] SharedCategories = [ItemCategory.Health, ItemCategory.Armor, ItemCategory.Implant, ItemCategory.Bonus];
    private static readonly (string Folder, string Money)[] Campaign = [("", "LevelMoney")];
    private static readonly (string Folder, string Money)[] AddOns = [("ADDON/", "LevelMoneyAddon"), ("ADDON2/", "LevelMoneyAddon2")];

    private readonly GameTable _table;
    private readonly ObjectTable _objects;

    /// <summary>
    /// Initializes a new instance of the <see cref="CampaignFitter"/> class.
    /// </summary>
    /// <param name="table">The game's shop and money tables.</param>
    /// <param name="objects">The game's object table.</param>
    public CampaignFitter(GameTable table, ObjectTable objects)
    {
        _table = table;
        _objects = objects;
    }

    /// <summary>
    /// Fits the campaign model of a style to its campaigns, leaving the excluded levels out.
    /// </summary>
    public CampaignModel Fit(ReferenceLevelSet references, GenerationStyle style, IReadOnlyCollection<string> excluded)
    {
        int levelCount = style.LevelCount;
        var campaigns = (style == GenerationStyle.AddOn ? AddOns : Campaign)
            .Select(campaign => new CampaignMeasurer(_table.GetArray(campaign.Money)).Measure(Enumerable.Range(1, levelCount)
                .Select(number => references.GetSurvey(campaign.Folder + ReferenceLevelSet.GetCampaignName(number)))
                .ToArray()))
            .ToArray();

        var weaponPrices = _table.GetArray("WeaponPrice");
        var weapons = Enumerable.Range((int)KnownObject.Shotgun, KnownObject.MagmaMinigun - KnownObject.Shotgun + 1)
            .Cast<KnownObject>()
            .OrderBy(weapon => weaponPrices[GetSlot(weapon)])
            .ToArray();

        var kept = campaigns
            .SelectMany(levels => levels)
            .Where(level => !excluded.Contains(level.Map))
            .ToArray();

        var indoor = kept
            .Where(level => level.Level > 1)
            .ToArray();

        var basements = indoor
            .Where(level => references.GetSurvey(level.Map).WallSet == WallSet.Basement)
            .ToArray();

        // Fit room counts on buildings only, without the arena level
        string arena = ReferenceLevelSet.GetCampaignName(10);
        var buildings = indoor
            .Where(level => level.Map != arena)
            .ToArray();

        var fights = buildings
            .Select(level => (level.Level, Encounters: references.GetSurvey(level.Map).Encounters))
            .ToArray();

        var bosses = FitFirstLevels(fights.Select(level => (level.Level, Ids: level.Encounters
            .Where(encounter => encounter.Kind == EncounterKind.Boss)
            .Select(encounter => encounter.Monsters[0].Id))));

        var monsters = FitFirstLevels(fights.Select(level => (level.Level, Ids: GetMonsters(level.Encounters, bosses).Select(count => count.Id))));
        return new CampaignModel
        {
            Style = style,
            LevelCount = levelCount,
            CashValue = CampaignMeasurer.CashValue,
            HitPoints = LinearFit.Fit(indoor.Select(level => (X: MathF.Log(level.Level), Y: MathF.Log(Math.Max(1, level.HitPoints)))).ToArray()),
            SpawnedShare = LinearFit.Fit(indoor.Select(level => (X: (float)level.Level, Y: GetLogOdds(level.SpawnedShare))).ToArray()),
            FloorPixels = FitWallSetLine(indoor, indoor.Select(level => Math.Log(level.FloorPixels)).ToArray()),
            Rooms = FitRooms(references, buildings),
            RoomRates = buildings
                .GroupBy(level => level.FloorSet)
                .ToDictionary(group => group.Key, group => new ValueRange(group.Min(level => GetRoomRate(references, level)), group.Max(level => GetRoomRate(references, level)))),
            MaxRooms = buildings
                .GroupBy(level => level.FloorSet)
                .ToDictionary(group => group.Key, group => group.Max(level => CountRooms(references, level))),
            EndMoney = LinearFit.Fit(campaigns.SelectMany(levels => levels).Where(level => level.Level < levelCount).Select(level => (X: MathF.Log(level.Level), Y: MathF.Log(level.EndMoney))).ToArray()),
            Buildings = FitBuildings(style, excluded),
            Gates = FitGates(indoor),
            Weapons = weapons,
            WeaponPrices = weapons.ToDictionary(weapon => weapon, weapon => weaponPrices[GetSlot(weapon)]),
            WeaponLevels = LinearFit.Fit(FitWeaponLevels(campaigns, weapons, excluded)),
            Affordability = FitAffordability(campaigns, weaponPrices, excluded),
            RepeatedWeapons = LinearFit.Fit(indoor.Select(level => (X: (float)level.Level, Y: (float)(level.Items.Where(item => item.Key.IsWeapon).Sum(item => item.Value) - level.NewWeapons.Count))).ToArray()),
            PickupRates = RatedCategories.ToDictionary(category => category, category => LinearFit.Fit(indoor
                .Select(level => (X: (float)level.Level, Y: level.Pickups.GetValueOrDefault(category) * PixelsPerRate / level.FloorPixels))
                .ToArray())),
            PickupShares = FitPickupShares(indoor),
            AmmoAges = FitAmmoAges(campaigns, weapons, excluded),
            Monsters = monsters,
            MonsterHitPoints = FitMonsterHitPoints(fights, bosses),
            Bosses = bosses,
            BossShare = new FittedValue<float>(fights.Count(level => level.Encounters.Any(encounter => encounter.Kind == EncounterKind.Boss)) / (float)Math.Max(1, fights.Length),
                fights.Length, IsBackedOff: false),
            DynamiteSites = indoor
                .GroupBy(level => references.GetSurvey(level.Map).Style)
                .ToDictionary(group => group.Key, group => LinearFit.Fit(group
                    .Select(level => (X: (float)level.Level, Y: references.GetLevel(level.Map).Analysis.Elements.Count(element => element.Kind == ElementKind.DynamiteSite) * PixelsPerRate / level.FloorPixels))
                    .ToArray())),
            SecretClosets = LinearFit.Fit(basements.Select(level => (X: (float)level.Level, Y: references.GetLevel(level.Map).Measures.SecretClosets.Count * PixelsPerRate / level.FloorPixels)).ToArray()),
            Donors = Enumerable.Range(2, 9)
                .Select(ReferenceLevelSet.GetCampaignName)
                .Where(name => !excluded.Contains(name))
                .Select(name => CreateDonor(references.GetLevel(name)))
                .ToArray(),
            FirstLevels = (style == GenerationStyle.AddOn ? AddOns : Campaign)
                .Select(campaign => CreateDonor(references.GetLevel(campaign.Folder + ReferenceLevelSet.GetCampaignName(1))))
                .ToArray()
        };
    }

    private MonsterKind[] FitFirstLevels(IEnumerable<(int Level, IEnumerable<KnownObject> Ids)> levels) => levels
        .SelectMany(level => level.Ids.Select(id => (Id: id, level.Level)))
        .GroupBy(pair => pair.Id)
        .Select(group => new MonsterKind(group.Key, group.Min(pair => pair.Level), GetHitPoints(group.Key)))
        .OrderBy(kind => kind.FirstLevel)
        .ThenBy(kind => kind.Id)
        .ToArray();

    private static MonsterCount[] GetMonsters(IReadOnlyList<LevelEncounter> encounters, MonsterKind[] bosses) => encounters
        .Where(encounter => encounter.Kind is EncounterKind.PlacedGroup or EncounterKind.Ambush)
        .SelectMany(encounter => encounter.Monsters)
        .Where(count => !count.Id.IsEndlessGenerator && !ExcludedMonsters.Contains(count.Id) && bosses.All(boss => boss.Id != count.Id))
        .GroupBy(count => count.Id)
        .Select(group => new MonsterCount(group.Key, group.Sum(count => count.Count)))
        .ToArray();

    private FittedValue<LinearFit> FitMonsterHitPoints(IReadOnlyList<(int Level, IReadOnlyList<LevelEncounter> Encounters)> levels, MonsterKind[] bosses)
    {
        var counted = levels
            .Select(level => (level.Level, Monsters: GetMonsters(level.Encounters, bosses).Select(count => (HitPoints: Math.Max(1, GetHitPoints(count.Id)), count.Count)).ToArray()))
            .Where(level => level.Monsters.Sum(monster => monster.Count) > 0)
            .ToArray();

        var line = LinearFit.Fit(counted
            .Select(level => (X: (float)level.Level, Y: MathF.Log(level.Monsters.Sum(monster => (float)monster.HitPoints * monster.Count) / level.Monsters.Sum(monster => monster.Count))))
            .ToArray());

        double squares = 0;
        foreach (var level in counted)
        {
            double mean = level.Monsters.Sum(monster => Math.Log(monster.HitPoints) * monster.Count) / level.Monsters.Sum(monster => monster.Count);
            squares += level.Monsters.Sum(monster => monster.Count * Math.Pow(Math.Log(monster.HitPoints) - mean, 2));
        }

        float spread = (float)Math.Sqrt(squares / Math.Max(1, counted.Sum(level => level.Monsters.Sum(monster => monster.Count))));
        return line with { Value = line.Value with { Spread = spread } };
    }

    private int GetHitPoints(KnownObject id) => _objects.TryFind(id, out var found) ? found.MaxHp : 0;

    // Fits ln rooms per million floor pixels to the level number with a concrete term
    private static FittedValue<WallSetLine> FitRooms(ReferenceLevelSet references, IReadOnlyList<CampaignMeasurement> levels) =>
        FitWallSetLine(levels, levels.Select(level => GetRoomRate(references, level)).ToArray());

    private static double GetRoomRate(ReferenceLevelSet references, CampaignMeasurement level) =>
        Math.Log(CountRooms(references, level) / (double)level.FloorPixels * PixelsPerRate);

    // Counts the regular rooms the generator builds, never halls, corridors, passages or closets
    private static int CountRooms(ReferenceLevelSet references, CampaignMeasurement level) => references.GetLevel(level.Map).Atlas.CountRegularRooms();

    // Fits a level quantity to the level number with a concrete term, flat below the minimum sample count
    private static FittedValue<WallSetLine> FitWallSetLine(IReadOnlyList<CampaignMeasurement> levels, double[] ys)
    {
        double[][] xs = levels.Select(level => (double[])[level.Level, level.FloorSet == WallSet.Concrete ? 1 : 0]).ToArray();
        if (levels.Count < LinearFit.MinSamples)
        {
            double mean = ys.DefaultIfEmpty(0).Average();
            double spread = ys.Length < 2 ? 0 : Math.Sqrt(ys.Sum(y => (y - mean) * (y - mean)) / (ys.Length - 1));
            return new FittedValue<WallSetLine>(new WallSetLine(mean, 0, 0, spread), levels.Count, IsBackedOff: true);
        }

        double[] coefficients;
        if (xs.Any(x => x[1] > 0) && xs.Any(x => x[1] < 1))
        {
            coefficients = MathNet.Numerics.Fit.MultiDim(xs, ys, intercept: true);
        }
        else
        {
            (double intercept, double slope) = MathNet.Numerics.Fit.Line(xs.Select(x => x[0]).ToArray(), ys);
            coefficients = [intercept, slope, 0];
        }

        double squares = ys.Select((y, index) => Math.Pow(y - coefficients[0] - coefficients[1] * xs[index][0] - coefficients[2] * xs[index][1], 2)).Sum();
        return new FittedValue<WallSetLine>(new WallSetLine(coefficients[0], coefficients[1], coefficients[2], Math.Sqrt(squares / Math.Max(1, ys.Length - 3))), levels.Count, IsBackedOff: false);
    }

    // Fits the drawn buildings to the levels after the first of the campaign, in add-on style of both add-ons too, ordered by where their levels
    // lie in their campaigns
    private static FittedValue<IReadOnlyList<BuildingShare>> FitBuildings(GenerationStyle style, IReadOnlyCollection<string> excluded)
    {
        var campaigns = style == GenerationStyle.AddOn ? Campaign.Concat(AddOns) : Campaign;

        var levels = campaigns
            .SelectMany(campaign =>
            {
                int count = campaign.Folder.Length == 0 ? GenerationStyle.Campaign.LevelCount : GenerationStyle.AddOn.LevelCount;
                return Enumerable.Range(2, count - 1).Select(number => (Name: campaign.Folder + ReferenceLevelSet.GetCampaignName(number), Position: (number - 2) / (float)(count - 2)));
            })
            .Where(level => !excluded.Contains(level.Name) && ReferenceLevelSet.GetBuilding(level.Name) != Building.Prison)
            .ToArray();

        var buildings = levels
            .GroupBy(level => ReferenceLevelSet.GetBuilding(level.Name))
            .Select(group => new BuildingShare(group.Key, group.Count() / (float)levels.Length, group.Average(level => level.Position), group.Count(), style == GenerationStyle.Campaign))
            .OrderBy(building => building.Position)
            .ThenBy(building => building.Building)
            .ToArray();

        return new FittedValue<IReadOnlyList<BuildingShare>>(buildings, levels.Length, IsBackedOff: false);
    }

    private static int GetSlot(KnownObject weapon) => weapon - KnownObject.Shotgun + FirstWeaponSlot;

    private static float GetLogOdds(float share)
    {
        float clamped = Math.Clamp(share, MinSpawnedShare, 1 - MinSpawnedShare);
        return MathF.Log(clamped / (1 - clamped));
    }

    private static DonorCandidate CreateDonor(ReferenceLevel level) =>
        new(level.Name, level.Survey.WallSet, level.Survey.Style, level.Measures.HitPoints, CampaignMeasurer.CashValue * level.Survey.Items.Count(item => item.Id == KnownObject.Money));

    // Orders each wall set's gate styles by the first level that shows them, the commoner first on one level
    private static Dictionary<WallSet, FittedValue<IReadOnlyList<GateShare>>> FitGates(IReadOnlyList<CampaignMeasurement> levels) => levels
        .GroupBy(level => level.FloorSet)
        .ToDictionary(wallSet => wallSet.Key, wallSet => new FittedValue<IReadOnlyList<GateShare>>(wallSet
            .GroupBy(level => level.GateStyle)
            .OrderBy(gate => gate.Min(level => level.Level))
            .ThenByDescending(gate => gate.Count())
            .Select(gate => new GateShare(gate.Key, gate.Count() / (float)wallSet.Count()))
            .ToArray(), wallSet.Count(), IsBackedOff: false));

    private static (float X, float Y)[] FitWeaponLevels(IReadOnlyList<CampaignMeasurement[]> campaigns, KnownObject[] weapons, IReadOnlyCollection<string> excluded) => campaigns
        .SelectMany(levels => levels)
        .Where(level => !excluded.Contains(level.Map))
        .SelectMany(level => level.NewWeapons.Select(weapon => (X: (float)(Array.IndexOf(weapons, weapon) + 1), Y: (float)level.Level)))
        .ToArray();

    // Measure how much of each new weapon's price the money paid and picked up on the levels before its own comes to
    private static FittedValue<float> FitAffordability(IReadOnlyList<CampaignMeasurement[]> campaigns, IReadOnlyList<int> weaponPrices, IReadOnlyCollection<string> excluded)
    {
        var shares = new List<float>();
        foreach (var levels in campaigns)
        {
            int earned = 0;
            foreach (var level in levels)
            {
                if (level.Level > 1 && !excluded.Contains(level.Map))
                {
                    shares.AddRange(level.NewWeapons.Select(weapon => earned / (float)weaponPrices[GetSlot(weapon)]));
                }

                earned += level.EndMoney + level.Money;
            }
        }

        return new FittedValue<float>(shares.DefaultIfEmpty(0).Min(), shares.Count, IsBackedOff: false);
    }

    private static Dictionary<KnownObject, FittedValue<LinearFit>> FitPickupShares(IReadOnlyList<CampaignMeasurement> levels) => levels
        .SelectMany(level => level.Items.Keys)
        .Where(id => SharedCategories.Contains(id.PickupCategory.GetValueOrDefault(ItemCategory.Weapon)))
        .Distinct()
        .Order()
        .ToDictionary(id => id, id => LinearFit.Fit(levels
            .Where(level => level.Pickups.GetValueOrDefault(id.PickupCategory!.Value) > 0)
            .Select(level => (X: (float)level.Level, Y: level.Items.GetValueOrDefault(id) / (float)level.Pickups[id.PickupCategory!.Value]))
            .ToArray()));

    // Count each level's ammunition by the age of its weapon in the price order, next to the newest weapon the campaign has brought so far
    private static FittedValue<IReadOnlyList<float>> FitAmmoAges(IReadOnlyList<CampaignMeasurement[]> campaigns, KnownObject[] weapons, IReadOnlyCollection<string> excluded)
    {
        var counts = new List<int>();
        int total = 0;
        foreach (var levels in campaigns)
        {
            int newest = -1;
            foreach (var level in levels)
            {
                newest = level.Weapons.Select(weapon => Array.IndexOf(weapons, weapon)).Append(newest).Max();
                if (level.Level == 1 || excluded.Contains(level.Map))
                {
                    continue;
                }

                foreach ((var id, int count) in level.Items.Where(item => item.Key.IsAmmo))
                {
                    int age = Math.Max(-1, newest - Array.IndexOf(weapons, id - KnownObject.ShotgunAmmo + KnownObject.Shotgun)) + 1;
                    while (counts.Count <= age)
                    {
                        counts.Add(0);
                    }

                    counts[age] += count;
                    total += count;
                }
            }
        }

        return new FittedValue<IReadOnlyList<float>>(counts.Select(count => count / (float)Math.Max(1, total)).ToArray(), total, IsBackedOff: false);
    }
}
