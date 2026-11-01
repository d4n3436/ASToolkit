using ASToolkit.Core.Design;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Design;

[Trait("Category", "Integration")]
public class RoomGraphIntegrationTests
{
    private static readonly Lazy<ObjectTable> Objects = new(() => ObjectTable.Load(Path.Combine(TestGameFolder.RequireFolder(), "objects.res")));

    [Theory]
    [InlineData("Level_02.map", 11, 1, 6, 1, 2, 0)]
    [InlineData("Level_03.map", 37, 8, 7, 1, 9, 0)]
    [InlineData("Level_04.map", 28, 6, 6, 1, 7, 0)]
    [InlineData("Level_05.map", 22, 2, 5, 2, 6, null)]
    [InlineData("ADDON/Level_02.map", 27, 3, 5, 2, 7, 1)]
    [InlineData("ADDON2/Level_03.map", 19, 4, 8, 0, 9, null)]
    public void Build_LabOriginal_MeasuresRoomsCyclesHubAndSections(string level, int rooms, int cycles, int hubDoors, int hubHops, int largestBranch, int? reentries)
    {
        var graph = BuildGraph(level);

        Assert.Equal(rooms, graph.Rooms);
        Assert.Equal(cycles, graph.Cycles);
        Assert.Equal(hubDoors, graph.HubDoors);
        Assert.Equal(hubHops, graph.HubHops);
        Assert.Equal(largestBranch, graph.LargestBranchRooms);
        Assert.Equal(reentries, graph.SectionReentries);
    }

    [Fact]
    public void Build_Level04_KeepsObjectivesNearTheMainBodyAndAmbushesOnTheTour()
    {
        var graph = BuildGraph("Level_04.map");

        Assert.Equal([0, 0, 1, 1, 1], graph.ObjectiveDepths);
        Assert.InRange(graph.MainBodyShare, 0.75f, 0.77f);
        Assert.Equal(0, graph.AbandonedAmbushShare);
        Assert.Equal(13, graph.LootRooms);
    }

    private static RoomGraph BuildGraph(string level)
    {
        string path = Path.Combine(TestGameFolder.RequireFolder(), "Maps", level);
        var map = MapResourceBuilder.Load(path);
        var script = LevelScriptReader.Read(Path.ChangeExtension(path, ".lgc"));
        var analysis = RouteAnalyzer.Analyze(level, map, script, Objects.Value);
        Assert.NotNull(analysis);
        return RoomGraph.Build(LevelSurveyor.Survey(map, script, analysis, Objects.Value));
    }
}
