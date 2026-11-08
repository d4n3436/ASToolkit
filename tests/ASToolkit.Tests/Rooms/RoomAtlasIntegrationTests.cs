using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Rooms;

[Trait("Category", "Integration")]
public class RoomAtlasIntegrationTests
{
    private const int ArenaBays = 10;

    public static TheoryData<string> OriginalLevels()
    {
        var levels = new TheoryData<string>();
        for (int number = 2; number <= 10; number++)
        {
            levels.Add($"Level_{number:00}.map");
        }

        for (int number = 2; number <= 5; number++)
        {
            levels.Add($"ADDON/Level_{number:00}.map");
            levels.Add($"ADDON2/Level_{number:00}.map");
        }

        return levels;
    }

    [Theory]
    [MemberData(nameof(OriginalLevels))]
    public void Read_OriginalLevel_PlacesEveryTypeReadByEyeOnARoom(string level)
    {
        var (atlas, _) = ReadLevel(level);

        Assert.All(atlas.Rooms.Where(room => room.Type != room.ProposedType || room.Side is not null), room => Assert.True(room.IsRoom));
    }

    [Theory]
    [MemberData(nameof(OriginalLevels))]
    public void Read_OriginalLevel_ReadsEveryRoomIntoOneArea(string level)
    {
        var (atlas, survey) = ReadLevel(level);

        int[] read = atlas.Areas
            .SelectMany(area => area.Places)
            .ToArray();

        Assert.Equal(read.Length, read.Distinct().Count());
        Assert.Empty(survey.Places.Where(place => place.Zone != GateZone.Secret).Select(place => place.Id).Except(read));
        Assert.All(atlas.Areas, area => Assert.NotEmpty(area.Places));
    }

    [Fact]
    public void Read_ArenaLevel_TypesItsBaysAsArenas()
    {
        var (atlas, _) = ReadLevel("Level_10.map");

        Assert.Equal(ArenaBays, atlas.Rooms.Count(room => room.Type == RoomType.Arena));
    }

    [Theory]
    [InlineData("ADDON/Level_04.map")]
    [InlineData("ADDON2/Level_05.map")]
    public void Read_BasementWallsOnMilitaryBaseFloor_HasTheConcreteFloorSet(string level)
    {
        var (_, survey) = ReadLevel(level);

        Assert.Equal(WallSet.Basement, survey.WallSet);
        Assert.Equal(WallSet.Concrete, survey.FloorSet);
    }

    private static (RoomAtlas Atlas, LevelSurvey Survey) ReadLevel(string level)
    {
        var reference = TestGameFolder.References.GetLevel(level);
        return (reference.Atlas, reference.Survey);
    }
}
