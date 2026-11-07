using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using Xunit;

namespace ASToolkit.Tests.Levels;

[Trait("Category", "Integration")]
public class LevelExtractorIntegrationTests
{
    [Fact]
    public void Extract_OriginalLevels_MatchTheFixtureModel()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        foreach (var expected in FixtureReader.Read<List<RouteFixture>>("routes.json"))
        {
            var model = LevelExtractor.Extract(expected.Name, MapResourceBuilder.Load(Path.Combine(game, "Maps", expected.Name)), objects);
            Assert.NotNull(model);
            Assert.Equal(expected.Theme == "mil" ? WallSet.Concrete : WallSet.Basement, model.WallSet);
            Assert.True(expected.CoarseCells == model.Cells.Count, $"{expected.Name} cells {model.Cells.Count}, expected {expected.CoarseCells}");
            Assert.True(expected.CoarseEdges == model.Edges.Count, $"{expected.Name} edges {model.Edges.Count}, expected {expected.CoarseEdges}");
        }
    }
}
