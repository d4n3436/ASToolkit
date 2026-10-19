using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using Xunit;

namespace ASToolkit.Tests.Levels;

public class LevelExtractorTests
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
            Assert.Equal(expected.Theme == "mil" ? LevelTheme.Military : LevelTheme.Lab, model.Theme);
            Assert.True(expected.CoarseCells == model.Cells.Count, $"{expected.Name} cells {model.Cells.Count}, expected {expected.CoarseCells}");
            Assert.True(expected.CoarseEdges == model.Edges.Count, $"{expected.Name} edges {model.Edges.Count}, expected {expected.CoarseEdges}");
        }
    }
}
