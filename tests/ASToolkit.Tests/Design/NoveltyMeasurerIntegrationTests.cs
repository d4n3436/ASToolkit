using ASToolkit.Core.Design;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using Xunit;

namespace ASToolkit.Tests.Design;

[Trait("Category", "Integration")]
public class NoveltyMeasurerIntegrationTests
{
    [Fact]
    public void CountCopiedPieces_LevelAgainstItself_FindsItsGroupsUnderAnotherNameOnly()
    {
        string game = TestGameFolder.RequireFolder();
        var level = new LevelLoader(ObjectTable.Load(Path.Combine(game, "objects.res"))).Load("Level_03.map", Path.Combine(game, "Maps", "Level_03.map"));
        Assert.NotNull(level);
        var novelty = new NoveltyMeasurer([("Level_03.map", level.Model, level.Survey)]);

        var copy = novelty.CountCopiedPieces("copy", level.Model);
        var own = novelty.CountCopiedPieces("Level_03.map", level.Model);

        Assert.True(copy.Value >= 0.5f, $"{copy.Count} of {copy.Total}");
        Assert.Equal(0, own.Count);
        Assert.Equal("Level_03.map", novelty.FindEqualDoorGraph("copy", level.Survey));
        Assert.Null(novelty.FindEqualDoorGraph("Level_03.map", level.Survey));
    }
}
