using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Survey;

[Trait("Category", "Integration")]
public class ProgressionMeasurerIntegrationTests
{
    private static readonly Lazy<LevelLoader> Loader = new(() => new LevelLoader(ObjectTable.Load(Path.Combine(TestGameFolder.RequireFolder(), "objects.res"))));

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
    [InlineData("Level_02.map", new[] { 5, 6 }, new[] { false, false })]
    [InlineData("Level_03.map", new[] { 9, 29 }, new[] { false, false })]
    [InlineData("Level_04.map", new[] { 12, 10, 6 }, new[] { false, false, true })]
    [InlineData("Level_05.map", new[] { 22 }, new[] { false })]
    [InlineData("Level_10.map", new[] { 7, 3, 19 }, new[] { false, false, true })]
    [InlineData("ADDON/Level_02.map", new[] { 3, 7, 16 }, new[] { false, false, false })]
    [InlineData("ADDON2/Level_04.map", new[] { 5, 3 }, new[] { false, false })]
    public void Measure_Original_SplitsTheLevelIntoItsStates(string level, int[] rooms, bool[] returnPasses)
    {
        var progression = new ProgressionMeasurer(Load(level)).Measure();

        Assert.Equal(rooms, progression.Phases.Select(phase => phase.Places.Count));
        Assert.Equal(returnPasses, progression.Phases.Select(phase => phase.IsReturnPass));
    }

    [Fact]
    public void Measure_Level04_ClosesTheDoorBehindTheBlackoutTrigger()
    {
        var progression = new ProgressionMeasurer(Load("Level_04.map")).Measure();

        Assert.Equal([new LevelSeal(SealKind.DoorsClose, 3, 3, 2, 1)], progression.Seals);
    }

    [Fact]
    public void Measure_Level10_BuildsRubbleBetweenTheFirstYardAndRoom5()
    {
        var progression = new ProgressionMeasurer(Load("Level_10.map")).Measure();

        Assert.Equal([new LevelSeal(SealKind.Rubble, 7, 6, 5, 1)], progression.Seals);
    }

    [Theory]
    [MemberData(nameof(OriginalLevels))]
    public void Measure_Original_PutsEveryWalkedPlaceInOnePhaseAndSealsOnlyLevels4And10(string level)
    {
        var survey = Load(level);
        var progression = new ProgressionMeasurer(survey).Measure();

        int[] phased = progression.Phases
            .SelectMany(phase => phase.Places)
            .ToArray();

        Assert.Equal(phased.Length, phased.Distinct().Count());
        Assert.Equal(DoorGraph.Create(survey).Rooms.Order(), phased.Order());
        Assert.All(progression.Phases, phase => Assert.Equal(phase.Entrance, phase.Path[0]));
        Assert.Equal(level is "Level_04.map" or "Level_10.map", progression.Seals.Count > 0);
    }

    private static LevelSurvey Load(string level)
    {
        var loaded = Loader.Value.Load(level, Path.Combine(TestGameFolder.RequireFolder(), "Maps", level));
        Assert.NotNull(loaded);
        return loaded.Survey;
    }
}
