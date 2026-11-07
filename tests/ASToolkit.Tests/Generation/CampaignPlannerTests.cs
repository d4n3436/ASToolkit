using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Generation;

public class CampaignPlannerTests
{
    private const int Seeds = 200;

    private static readonly KnownObject[] Weapons =
    [
        KnownObject.Shotgun, KnownObject.GrenadeLauncher, KnownObject.Minigun, KnownObject.RocketLauncher,
        KnownObject.FreezeRifle, KnownObject.PlasmaRifle, KnownObject.FlameThrower, KnownObject.MagmaMinigun
    ];

    private static readonly int[] Prices = [200, 350, 1000, 2500, 4800, 8000, 15800, 25400];

    [Fact]
    public void Plan_SameSeed_DrawsTheSameBriefs()
    {
        var planner = new CampaignPlanner(CreateModel());

        var first = planner.Plan(new Random(7)).Levels;
        var second = planner.Plan(new Random(7)).Levels;

        Assert.Equal(first.Select(Describe), second.Select(Describe));
    }

    [Fact]
    public void Plan_Seeds_NeverLowerHitPointsOrEndMoney()
    {
        var planner = new CampaignPlanner(CreateModel());
        foreach (var levels in Enumerable.Range(1, Seeds).Select(seed => planner.Plan(new Random(seed)).Levels))
        {
            var indoor = levels.Skip(1).ToArray();
            Assert.All(indoor.Zip(indoor.Skip(1)), pair => Assert.True(pair.Second.HitPoints >= pair.First.HitPoints));
            var paid = levels.SkipLast(1).ToArray();
            Assert.All(paid.Zip(paid.Skip(1)), pair => Assert.True(pair.Second.EndMoney >= pair.First.EndMoney));
            Assert.Equal(0, levels[^1].EndMoney);
        }
    }

    [Fact]
    public void Plan_Seeds_KeepRoomsWithinTheFittedRatesAndMaximum()
    {
        var model = CreateModel();
        var planner = new CampaignPlanner(model);
        foreach (var brief in Enumerable.Range(1, Seeds).SelectMany(seed => planner.Plan(new Random(seed)).Levels.Skip(1)))
        {
            var range = model.RoomRates[brief.WallSet];
            double rooms = brief.FloorPixels / 1_000_000.0;

            int most = brief.WallSet == WallSet.Basement ? 28 : model.MaxRooms[brief.WallSet];
            Assert.InRange(brief.Rooms, 4, Math.Max(4, most));
            if (brief.WallSet == WallSet.Basement)
            {
                Assert.InRange(brief.Rooms, brief.IsIntro ? 9 : 19, brief.IsIntro ? 14 : brief.GateStyle switch { LevelStyle.LockedDoors => 24, LevelStyle.Ungated => 22, _ => 28 });
                continue;
            }

            Assert.InRange(brief.Rooms, Math.Min(Math.Floor(rooms * Math.Exp(range.Min)), most), Math.Max(4, Math.Ceiling(rooms * Math.Exp(range.Max))));
        }
    }

    [Fact]
    public void Plan_Seeds_OpenWithTheLabGatesThenPutBuildingsAndGatesInTheirOrder()
    {
        var model = CreateModel();
        var planner = new CampaignPlanner(model);
        var buildingOrder = model.Buildings.Value
            .Select(share => share.Building)
            .ToArray();

        var basementOrder = new[] { LevelStyle.LockedDoors, LevelStyle.Blackout, LevelStyle.Ungated };
        foreach (var levels in Enumerable.Range(1, Seeds).Select(seed => planner.Plan(new Random(seed)).Levels))
        {
            Assert.Equal(Building.Grounds, levels[0].Building);

            int[] buildings = levels
                .Skip(1)
                .Select(brief => Array.IndexOf(buildingOrder, brief.Building))
                .ToArray();

            Assert.Equal(buildings.Order(), buildings);
            Assert.Contains(Building.UndergroundLab, levels.Select(brief => brief.Building));
            Assert.Contains(Building.Warehouse, levels.Select(brief => brief.Building));
            Assert.All(levels, brief => Assert.Equal(ReferenceLevelSet.GetFloorSet(brief.Building), brief.WallSet));

            Assert.Equal([LevelStyle.LockedDoors, LevelStyle.LockedDoors, LevelStyle.Blackout], levels.Skip(1).Take(3).Select(brief => brief.GateStyle));
            Assert.All(levels.Skip(1).Take(3), brief => Assert.Equal(Building.UndergroundLab, brief.Building));
            Assert.Equal([true, false, false], levels.Skip(1).Take(3).Select(brief => brief.IsIntro));

            int[] basementGates = levels
                .Skip(4)
                .Where(brief => brief.WallSet == WallSet.Basement)
                .Select(brief => Array.IndexOf(basementOrder, brief.GateStyle))
                .ToArray();

            Assert.Equal(basementGates.Order(), basementGates);
        }
    }

    [Fact]
    public void Plan_Seeds_BringWeaponsInPriceOrderOnceTheyAreAffordable()
    {
        var model = CreateModel();
        var planner = new CampaignPlanner(model);
        foreach (var levels in Enumerable.Range(1, Seeds).Select(seed => planner.Plan(new Random(seed)).Levels))
        {
            var brought = levels
                .SelectMany(brief => brief.NewWeapons)
                .ToArray();

            Assert.Equal(Weapons.Take(brought.Length), brought);
            int earned = 0;
            foreach (var brief in levels)
            {
                Assert.All(brief.NewWeapons, weapon => Assert.True(earned >= model.Affordability.Value * model.WeaponPrices[weapon]));
                Assert.All(brief.Weapons, weapon => Assert.Contains(weapon, brief.Arsenal));
                Assert.All(brief.Pickups.Keys.Where(id => id.IsAmmo), ammo => Assert.Contains(ammo, Weapons.Take(brief.Arsenal.Count + 1).Select(weapon => weapon.WeaponAmmo!.Value)));
                int cash = brief.Level == 1 ? model.FirstLevels.Single(level => level.Name == brief.DonorLevel).Money : brief.Pickups.GetValueOrDefault(KnownObject.Money) * model.CashValue;
                earned += brief.EndMoney + cash;
            }
        }
    }

    [Fact]
    public void Plan_Seeds_TakeDonorsOfTheBriefsWallSet()
    {
        var model = CreateModel();
        var planner = new CampaignPlanner(model);
        foreach (var levels in Enumerable.Range(1, Seeds).Select(seed => planner.Plan(new Random(seed)).Levels))
        {
            Assert.Equal("Level_01.map", levels[0].DonorLevel);
            Assert.All(levels.Skip(1), brief => Assert.Equal(brief.WallSet, model.Donors.Single(donor => donor.Name == brief.DonorLevel).WallSet));
            Assert.All(levels.Skip(1), brief => Assert.Equal(brief.GateStyle, model.Donors.Single(donor => donor.Name == brief.DonorLevel).GateStyle));
        }
    }

    [Fact]
    public void Plan_Seeds_StandMountedCannonsOnWarehouseLevelsSevenAndEightOnly()
    {
        var planner = new CampaignPlanner(CreateModel());
        foreach (var levels in Enumerable.Range(1, Seeds).Select(seed => planner.Plan(new Random(seed)).Levels))
        {
            Assert.All(levels, brief => Assert.Equal(brief is { Building: Building.Warehouse, Level: 7 or 8 } ? 1 : 0, brief.MountedCannons));
        }
    }

    [Fact]
    public void Plan_Seeds_BuildLabsOnLevelsTwoToFiveWarehousesAfterAndNeverRepeatASecondWing()
    {
        var planner = new CampaignPlanner(CreateModel());
        foreach (var levels in Enumerable.Range(1, Seeds).Select(seed => planner.Plan(new Random(seed)).Levels))
        {
            Assert.All(levels.Where(brief => brief.Level >= 2), brief => Assert.Equal(brief.Level < 6 ? Building.UndergroundLab : Building.Warehouse, brief.Building));
            var wings = levels
                .Where(brief => brief.Building == Building.UndergroundLab)
                .Select(brief => brief.SecondWing)
                .ToArray();

            Assert.All(wings, wing => Assert.NotNull(wing));
            Assert.Equal(wings.Length, wings.Distinct().Count());
        }
    }

    private static string Describe(LevelBrief brief) =>
        $"{brief.Level} {brief.Building} {brief.WallSet} {brief.GateStyle} {brief.HitPoints} {brief.SpawnedShare} {brief.FloorPixels} {brief.SecretClosets} {string.Join(",", brief.Weapons)} "
        + $"{string.Join(",", brief.Pickups.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"))} {brief.EndMoney} {brief.DonorLevel}";

    private static FittedValue<LinearFit> Line(float intercept, float slope, float spread) => new(new LinearFit(intercept, slope, spread), 9, IsBackedOff: false);

    private static CampaignModel CreateModel() => new()
    {
        Style = GenerationStyle.Campaign,
        LevelCount = 10,
        CashValue = 50,
        HitPoints = Line(4.5f, 0.96f, 0.53f),
        SpawnedShare = Line(-3.4f, 0.76f, 1.4f),
        FloorPixels = new FittedValue<WallSetLine>(new WallSetLine(13.9, 0.35, -1.1, 0.23), 9, IsBackedOff: false),
        Rooms = new FittedValue<WallSetLine>(new WallSetLine(2.0, -0.1, -0.5, 0.2), 9, IsBackedOff: false),
        RoomRates = new Dictionary<WallSet, ValueRange> { [WallSet.Basement] = new(1.2, 2.2), [WallSet.Concrete] = new(0.3, 1.6) },
        MaxRooms = new Dictionary<WallSet, int> { [WallSet.Basement] = 38, [WallSet.Concrete] = 29 },
        EndMoney = Line(4.2f, 0.75f, 0.45f),
        Buildings = new FittedValue<IReadOnlyList<BuildingShare>>(
        [
            new BuildingShare(Building.UndergroundLab, 0.5f, 0.19f, 4, IsRequired: true),
            new BuildingShare(Building.Warehouse, 0.5f, 0.69f, 4, IsRequired: true)
        ], 8, IsBackedOff: false),
        Gates = new Dictionary<WallSet, FittedValue<IReadOnlyList<GateShare>>>
        {
            [WallSet.Basement] = new([new GateShare(LevelStyle.LockedDoors, 0.5f), new GateShare(LevelStyle.Blackout, 0.25f), new GateShare(LevelStyle.Ungated, 0.25f)], 4, IsBackedOff: false),
            [WallSet.Concrete] = new([new GateShare(LevelStyle.Fenced, 1)], 5, IsBackedOff: false)
        },
        Weapons = Weapons,
        WeaponPrices = Weapons.Zip(Prices).ToDictionary(pair => pair.First, pair => pair.Second),
        WeaponLevels = Line(0.64f, 1.02f, 1.38f),
        Affordability = new FittedValue<float>(0.5f, 8, IsBackedOff: false),
        RepeatedWeapons = Line(0.3f, 0.1f, 1),
        PickupRates = new Dictionary<ItemCategory, FittedValue<LinearFit>>
        {
            [ItemCategory.Health] = Line(5.5f, -0.36f, 1.3f),
            [ItemCategory.Armor] = Line(0.5f, 0.03f, 0.2f),
            [ItemCategory.Ammo] = Line(8, -0.41f, 3),
            [ItemCategory.Money] = Line(12.5f, -1.4f, 1.6f)
        },
        PickupShares = new Dictionary<KnownObject, FittedValue<LinearFit>>
        {
            [KnownObject.Health] = Line(1.1f, -0.12f, 0.1f),
            [KnownObject.BigHealth] = Line(0, 0.08f, 0.1f),
            [KnownObject.GreenArmor] = Line(1, -0.1f, 0.1f),
            [KnownObject.RedArmor] = Line(-0.5f, 0.1f, 0.1f)
        },
        AmmoAges = new FittedValue<IReadOnlyList<float>>([0.1f, 0.3f, 0.1f, 0.15f, 0.2f, 0.05f, 0.1f], 258, IsBackedOff: false),
        SecretClosets = new FittedValue<LinearFit>(new LinearFit(1.4f, 0, 0.4f), 4, IsBackedOff: true),
        Donors =
        [
            new DonorCandidate("Level_02.map", WallSet.Basement, LevelStyle.LockedDoors, 336, 950),
            new DonorCandidate("Level_04.map", WallSet.Basement, LevelStyle.Blackout, 6526, 1300),
            new DonorCandidate("Level_05.map", WallSet.Basement, LevelStyle.Ungated, 21261, 2550),
            new DonorCandidate("Level_08.map", WallSet.Concrete, LevelStyle.Fenced, 197861, 0)
        ],
        FirstLevels = [new DonorCandidate("Level_01.map", WallSet.Outdoor, LevelStyle.Ungated, 0, 300)]
    };
}
