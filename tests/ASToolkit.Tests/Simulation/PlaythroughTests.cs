using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Simulation;

public class PlaythroughTests
{
    [Fact]
    public void Run_PlaysTheFirstLabLevelToItsEndTheSameWayForASeed()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var table = GameTable.Load(Path.Combine(game, "Maps", "common_table.lgc"));
        var level = new LevelLoader(objects).Load("Level_02.map", Path.Combine(game, "Maps", "Level_02.map"));
        Assert.NotNull(level);
        var playthrough = new Playthrough(game, objects, table);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);

        var first = playthrough.Run(level.Analysis, loadout, 1, true);
        var second = playthrough.Run(level.Analysis, loadout, 1, true);

        Assert.True(first.Survived);
        Assert.Equal(1, first.Progress, 3);
        Assert.Equal(level.Analysis.Elements.Count(element => element.Kind == Core.Routes.ElementKind.Monster), first.Kills);
        Assert.Equal(first.Time, second.Time);
        Assert.Equal(first.Shots.Count, second.Shots.Count);
        Assert.NotEmpty(first.Frames);
    }
}
