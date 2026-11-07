using ASToolkit.Core.Design;
using Xunit;

namespace ASToolkit.Tests.Design;

[Trait("Category", "Integration")]
public class RoomGraphIntegrationTests
{
    [Theory]
    [InlineData("Level_02.map", 11, 1, 6, 1, 2, 0)]
    [InlineData("Level_03.map", 38, 6, 7, 1, 9, 0)]
    [InlineData("Level_04.map", 28, 2, 5, 1, 7, 0)]
    [InlineData("Level_05.map", 22, 1, 5, 2, 6, null)]
    [InlineData("ADDON/Level_02.map", 26, 3, 5, 2, 7, 1)]
    [InlineData("ADDON2/Level_03.map", 19, 4, 8, 0, 9, null)]
    public void Build_BasementOriginal_MeasuresRoomsCyclesHubAndSections(string level, int rooms, int cycles, int hubDoors, int hubHops, int largestBranch, int? reentries)
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

        Assert.Equal([0, 0, 1, 2, 2], graph.ObjectiveDepths);
        Assert.InRange(graph.MainBodyShare, 0.68f, 0.70f);
        Assert.Equal(0, graph.AbandonedAmbushShare);
        Assert.Equal(13, graph.LootRooms);
    }

    private static RoomGraph BuildGraph(string level) => RoomGraph.Build(TestGameFolder.References.GetSurvey(level));
}
