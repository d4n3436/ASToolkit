using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Reference;

[Trait("Category", "Integration")]
public class CampaignFitterIntegrationTests
{
    private const int Seeds = 100;

    [Fact]
    public void Fit_Campaign_TakesOnlyCampaignLevels()
    {
        var model = Fit(GenerationStyle.Campaign);

        Assert.Equal(10, model.LevelCount);
        Assert.Equal(9, model.HitPoints.Samples);
        Assert.Equal(["Level_01.map"], model.FirstLevels.Select(level => level.Name));
        Assert.All(model.Donors, donor => Assert.StartsWith("Level_", donor.Name));
        Assert.Equal([LevelStyle.Fenced], model.Gates[WallSet.Concrete].Value.Select(gate => gate.GateStyle));
        Assert.Equal([LevelStyle.LockedDoors, LevelStyle.Blackout, LevelStyle.Ungated], model.Gates[WallSet.Basement].Value.Select(gate => gate.GateStyle));
    }

    [Fact]
    public void Fit_Campaign_KeepsAddOnOnlyMonstersOutAndBossesApart()
    {
        var campaign = Fit(GenerationStyle.Campaign);
        var addOn = Fit(GenerationStyle.AddOn);

        Assert.DoesNotContain(campaign.Monsters, kind => kind.Id is KnownObject.Bat or KnownObject.TankMonster);
        Assert.Contains(addOn.Monsters, kind => kind.Id == KnownObject.Bat);
        Assert.Empty(campaign.Monsters.Select(kind => kind.Id).Intersect(campaign.Bosses.Select(kind => kind.Id)));
        Assert.True(campaign.MonsterHitPoints.Value.Slope > 0);
    }

    [Fact]
    public void GetFloorSet_Originals_MatchesTheFloorSetOfEveryOriginalOfTheBuilding()
    {
        var references = TestGameFolder.References;

        Assert.All(references.LevelNames, name => Assert.Equal(references.GetSurvey(name).FloorSet, ReferenceLevelSet.GetFloorSet(ReferenceLevelSet.GetBuilding(name))));
    }

    [Fact]
    public void Fit_BothStyles_DrawNoPrisonAndTheCampaignOnlyItsOwnBuildings()
    {
        var campaign = Fit(GenerationStyle.Campaign).Buildings.Value;
        var addOn = Fit(GenerationStyle.AddOn).Buildings.Value;

        Assert.Equal([Building.UndergroundLab, Building.Warehouse], campaign.Select(share => share.Building));
        Assert.All(campaign, share => Assert.True(share.IsRequired));
        Assert.Equal(Enum.GetValues<Building>().Except([Building.Grounds, Building.Prison]).Order(), addOn.Select(share => share.Building).Order());
        Assert.All(addOn, share => Assert.False(share.IsRequired));
    }

    [Fact]
    public void Plan_AddOnStyle_IsShorterAndHarderThanTheCampaign()
    {
        var campaign = Fit(GenerationStyle.Campaign);
        var addOn = Fit(GenerationStyle.AddOn);
        var campaignPlans = Plan(campaign);
        var addOnPlans = Plan(addOn);

        Assert.Equal(5, addOn.LevelCount);
        Assert.All(addOnPlans, levels => Assert.Equal(5, levels.Count));
        for (int index = 1; index < addOn.LevelCount; index++)
        {
            Assert.True(Median(addOnPlans, index, brief => brief.HitPoints) > Median(campaignPlans, index, brief => brief.HitPoints), $"hit points of level {index + 1}");
            Assert.True(Median(addOnPlans, index - 1, brief => brief.EndMoney) > Median(campaignPlans, index - 1, brief => brief.EndMoney), $"end money of level {index}");
        }
    }

    private static List<IReadOnlyList<LevelBrief>> Plan(CampaignModel model)
    {
        var planner = new CampaignPlanner(model);
        return Enumerable.Range(1, Seeds).Select(seed => planner.Plan(new Random(seed)).Levels).ToList();
    }

    private static int Median(List<IReadOnlyList<LevelBrief>> plans, int index, Func<LevelBrief, int> value) => plans.Select(levels => value(levels[index])).Order().ElementAt(plans.Count / 2);

    private static CampaignModel Fit(GenerationStyle style)
    {
        string game = TestGameFolder.RequireFolder();
        return new CampaignFitter(GameTable.Load(Path.Combine(game, "Maps", "common_table.lgc")), TestGameFolder.Objects).Fit(TestGameFolder.References, style, []);
    }
}
