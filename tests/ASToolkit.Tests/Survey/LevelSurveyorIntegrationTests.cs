using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Survey;

[Trait("Category", "Integration")]
public class LevelSurveyorIntegrationTests
{
    private const int MinOpenPlanPlacePixels = 40000;
    private const int PlayableFloorPixels = 10_000_000;

    public static TheoryData<string> OriginalLevels()
    {
        var levels = new TheoryData<string>();
        for (int number = 1; number <= 10; number++)
        {
            levels.Add($"Level_{number:00}.map");
        }

        for (int number = 1; number <= 5; number++)
        {
            levels.Add($"ADDON/Level_{number:00}.map");
            levels.Add($"ADDON2/Level_{number:00}.map");
        }

        return levels;
    }

    [Theory]
    [MemberData(nameof(OriginalLevels))]
    public void Survey_OriginalLevel_NumbersPlacesAndConnectsThem(string level)
    {
        var survey = SurveyLevel(level);

        Assert.NotEmpty(survey.Places);
        Assert.Equal(Enumerable.Range(1, survey.Places.Count), survey.Places.Select(place => place.Id));
        Assert.Equal(survey.Places.Count, survey.Places.Select(place => place.Id).Distinct().Count());
        Assert.All(survey.Connections, connection =>
        {
            Assert.InRange(connection.A, 1, survey.Places.Count);
            Assert.InRange(connection.B, 1, survey.Places.Count);
            Assert.NotEqual(connection.A, connection.B);
        });
        Assert.All(survey.Encounters, encounter => Assert.InRange(encounter.Place, 1, survey.Places.Count));
        Assert.Contains(survey.Landmarks, landmark => landmark.Kind == LandmarkKind.RouteEnd);
    }

    [Fact]
    public void Survey_BasementLevelWithDoors_SplitsRoomsAtDoorsAndDoorways()
    {
        var survey = SurveyLevel("ADDON/Level_02.map");

        Assert.True(survey.Places.Count >= 20, $"{survey.Places.Count} places");
        Assert.Contains(survey.Connections, connection => connection.Kind == ConnectionKind.Doorway);
        Assert.Contains(survey.Connections, connection => connection.Kind == ConnectionKind.LockedDoor);
    }

    [Theory]
    [InlineData("Level_06.map")]
    [InlineData("Level_07.map")]
    [InlineData("Level_08.map")]
    [InlineData("Level_09.map")]
    [InlineData("Level_10.map")]
    [InlineData("ADDON/Level_04.map")]
    [InlineData("ADDON/Level_05.map")]
    [InlineData("ADDON2/Level_04.map")]
    public void Survey_OpenPlanLevel_MergesPocketsIntoThePlaceTheyOpenOn(string level)
    {
        var survey = SurveyLevel(level);

        Assert.All(survey.Places, place => Assert.True(place.AreaPixels >= MinOpenPlanPlacePixels, $"place {place.Id} is {place.AreaPixels} px²"));
    }

    [Fact]
    public void Survey_AddOnLevelWithFloorBeyondTheWalls_LeavesThatFloorOut()
    {
        var survey = SurveyLevel("ADDON2/Level_04.map");

        Assert.True(survey.Signature.FloorPixels < PlayableFloorPixels, $"{survey.Signature.FloorPixels} px²");
        Assert.Equal(8, survey.Places.Count);
    }

    private static LevelSurvey SurveyLevel(string level) => TestGameFolder.References.GetSurvey(level);
}
