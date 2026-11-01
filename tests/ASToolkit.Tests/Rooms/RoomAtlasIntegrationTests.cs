using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;
using Xunit;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Tests.Rooms;

[Trait("Category", "Integration")]
public class RoomAtlasIntegrationTests
{
    private const int ArenaBays = 10;

    private static readonly Lazy<ObjectTable> Objects = new(() => ObjectTable.Load(Path.Combine(TestGameFolder.RequireFolder(), "objects.res")));

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

    [Fact]
    public void Read_ArenaLevel_TypesItsBaysAsArenas()
    {
        var (atlas, _) = ReadLevel("Level_10.map");

        Assert.Equal(ArenaBays, atlas.Rooms.Count(room => room.Type == RoomType.Arena));
    }

    [Theory]
    [InlineData("ADDON/Level_04.map")]
    [InlineData("ADDON2/Level_05.map")]
    public void Read_LabWallsOnMilitaryFloor_IsAMilitarySetting(string level)
    {
        var (_, survey) = ReadLevel(level);

        Assert.Equal(LevelTheme.Lab, survey.Theme);
        Assert.Equal(LevelTheme.Military, survey.Setting);
    }

    [Theory]
    [InlineData(RoomType.Office, "table+chair+computer")]
    [InlineData(RoomType.Office, "cabinet")]
    [InlineData(RoomType.ComputerRoom, "bank")]
    [InlineData(RoomType.ComputerRoom, "console")]
    [InlineData(RoomType.MachineRoom, "machine")]
    [InlineData(RoomType.Depot, "container")]
    public void Measure_CampaignLabRooms_RequiresTheDefiningPart(RoomType type, string kind)
    {
        var levels = new[] { "Level_02.map", "Level_03.map", "Level_04.map", "Level_05.map" }.Select(ReadLevel).ToArray();

        var recipe = RoomRecipe.Measure(levels.SelectMany(level => level.Atlas.Rooms), levels[0].Survey.Lattice).Single(recipe => recipe.Type == type);

        Assert.Contains(recipe.Parts, part => part.Kind == kind && part.IsRequired);
    }

    private static (RoomAtlas Atlas, LevelSurvey Survey) ReadLevel(string level)
    {
        string maps = Path.Combine(TestGameFolder.RequireFolder(), "Maps");
        var map = MapResourceBuilder.Load(Path.Combine(maps, level));
        var script = LevelScriptReader.Read(Path.Combine(maps, Path.ChangeExtension(level, ".lgc")));
        var analysis = RouteAnalyzer.Analyze(level, map, script, Objects.Value)!;
        var survey = LevelSurveyor.Survey(map, script, analysis, Objects.Value);
        return (RoomAtlas.Read(survey, analysis.Grid.Edges, map.Sprites, Objects.Value), survey);
    }
}
