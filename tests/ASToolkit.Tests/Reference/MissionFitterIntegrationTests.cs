using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Reference;

[Trait("Category", "Integration")]
public class MissionFitterIntegrationTests
{
    private const int Seeds = 50;

    private static readonly Lazy<ReferenceLevelSet> References = new(() =>
    {
        string game = TestGameFolder.RequireFolder();
        return ReferenceLevelSet.Load(game, ObjectTable.Load(Path.Combine(game, "objects.res")));
    });

    [Theory]
    [InlineData("Level_02.map", 2)]
    [InlineData("Level_03.map", 2)]
    [InlineData("Level_04.map", 3)]
    [InlineData("Level_05.map", 1)]
    public void Draw_CampaignFit_DrawsTheOriginalsPhaseCount(string level, int phases)
    {
        var survey = References.Value.GetSurvey(level);
        var grammar = new MissionGrammar(FitCampaign());

        Assert.Equal(phases, new ProgressionMeasurer(survey).Measure().Phases.Count);
        for (int seed = 0; seed < Seeds; seed++)
        {
            Assert.Equal(phases, grammar.Draw(new Random(seed), CreateBrief(survey), GetObjectives(survey)).Progression.Phases.Count);
        }
    }

    [Fact]
    public void Fit_Campaign_KeepsAddOnFormsOut()
    {
        var campaign = FitCampaign();
        var grammar = new MissionGrammar(campaign);
        var survey = References.Value.GetSurvey("Level_04.map");
        int largest = campaign.Structures[LevelTheme.Lab].EnteredCounts.Value.Max();

        Assert.Equal(1, campaign.LastReturn[LevelStyle.Blackout].Value);
        Assert.True(largest < 8, $"largest entered count {largest}");
        for (int seed = 0; seed < Seeds; seed++)
        {
            Assert.True(grammar.Draw(new Random(seed), CreateBrief(survey), GetObjectives(survey)).Progression.Phases[^1].IsReturnPass);
        }
    }

    private static MissionModel FitCampaign() => new MissionFitter(GenerationStyle.Campaign).Fit(StyleFitter.GetFittingLevels(GenerationStyle.Campaign).Select(References.Value.GetSurvey).ToArray());

    private static MissionObjectives GetObjectives(LevelSurvey survey)
    {
        int generatorRooms = survey.Landmarks
            .Where(landmark => landmark.Kind == LandmarkKind.Generator)
            .Select(landmark => landmark.Place)
            .Distinct()
            .Count();

        return new MissionObjectives(survey.Landmarks.Count(landmark => landmark.Kind == LandmarkKind.DynamiteSite), generatorRooms);
    }

    private static LevelBrief CreateBrief(LevelSurvey survey) => new()
    {
        Level = int.Parse(survey.Name.AsSpan(survey.Name.LastIndexOf('_') + 1, 2)),
        Theme = survey.Setting,
        GateStyle = survey.Style,
        HitPoints = 0,
        SpawnedShare = 0.3f,
        FloorPixels = survey.Places.Sum(place => place.AreaPixels),
        Rooms = DoorGraph.Create(survey).Rooms.Count,
        SecretClosets = 0,
        Arsenal = [],
        NewWeapons = [],
        Weapons = [],
        Pickups = new Dictionary<KnownObject, int>(),
        EndMoney = 0,
        DonorLevel = survey.Name
    };
}
