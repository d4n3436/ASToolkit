using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Simulation;

public class DemoReplayTests
{
    [Theory]
    [InlineData("addon2-level1-walk", "ADDON2", "Level_01.map")]
    [InlineData("level02-walk", "", "Level_02.map")]
    [InlineData("level03-walk", "", "Level_03.map")]
    public void ReplayDemo_RecordedWalk_MatchesTheGameExactly(string fixture, string folder, string map)
    {
        string game = TestGameFolder.RequireFolder();
        var demo = new DemoResource(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".dem")));
        var samples = FixtureReader.Read<Sample[]>(fixture + ".json");
        string name = string.IsNullOrEmpty(folder) ? map : folder + "/" + map;
        var level = new LevelLoader(TestGameFolder.Objects).Load(name, Path.Combine(game, "Maps", folder, map))!.Analysis;
        var points = new Playthrough(game, TestGameFolder.Objects, TestGameFolder.Table).ReplayDemo(level, demo);
        var stateBeforeFrame = new Dictionary<int, DemoReplayPoint>();
        for (int index = 1; index < points.Count; index++)
        {
            stateBeforeFrame.Add(points[index].Time, points[index - 1]);
        }

        int compared = 0;
        foreach (var sample in samples)
        {
            if (!stateBeforeFrame.TryGetValue(sample.Time, out var state))
                continue;

            Assert.Equal(sample.X, state.Position.X);
            Assert.Equal(sample.Y, state.Position.Y);
            Assert.Equal(sample.Speed, state.Speed);
            compared++;
        }

        Assert.True(compared > 300);
    }

    [Fact]
    public void ReplayDemo_RecordedFight_MatchesTheGamesDrawsAndPlayer()
    {
        string game = TestGameFolder.RequireFolder();
        var demo = new DemoResource(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "level09-fire.dem")));
        var fixture = FixtureReader.Read<FightFixture>("level09-fire.json");
        var level = new LevelLoader(TestGameFolder.Objects).Load("Level_09.map", Path.Combine(game, "Maps", "Level_09.map"))!.Analysis;
        var points = new Playthrough(game, TestGameFolder.Objects, TestGameFolder.Table).ReplayDemo(level, demo, fixture.LoadingScreenRecords);

        Assert.NotEmpty(fixture.Draws);
        Assert.NotEmpty(fixture.Samples);
        for (int frame = 1; frame <= fixture.Draws.Length; frame++)
        {
            Assert.Equal(fixture.Draws[frame - 1], CountDraws(demo.Seed, points[frame].RandomState));
        }

        var stateBeforeFrame = new Dictionary<int, DemoReplayPoint>();
        for (int index = 1; index < points.Count; index++)
        {
            stateBeforeFrame.Add(points[index].Time, points[index - 1]);
        }

        foreach (var sample in fixture.Samples)
        {
            var state = stateBeforeFrame[sample.Time];

            Assert.Equal(sample.X, state.Position.X);
            Assert.Equal(sample.Y, state.Position.Y);
            Assert.Equal(sample.Speed, state.Speed);
            Assert.Equal(sample.Health, state.Health);
        }
    }

    [Fact]
    public void LoadDemo_RecordedLoad_BuildsTheSpritesWithTheGamesDraws()
    {
        string game = TestGameFolder.RequireFolder();
        var demo = new DemoResource(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "addon2-level1-load.dem")));
        int[][] sprites = FixtureReader.Read<int[][]>("addon2-level1-load.json");
        var level = new LevelLoader(TestGameFolder.Objects).Load("ADDON2/Level_01.map", Path.Combine(game, "Maps", "ADDON2", "Level_01.map"))!.Analysis;
        var world = new Playthrough(game, TestGameFolder.Objects, TestGameFolder.Table).LoadDemo(level, demo);
        var layer = world.GetLayer(1);

        Assert.Equal(10623 - 2, CountDraws(demo.Seed, world.RandomState));
        for (int slot = 0; slot < sprites.Length; slot++)
        {
            var unit = layer[slot].Unit!;
            int[] expected = sprites[slot];

            int[] actual = [(int)unit.Definition.Id, unit.LastTickTime, unit.FirstFrame, unit.Frame, unit.LastFrame, (int)unit.Animation, unit.Direction.Value];

            Assert.Equal(expected, actual);
        }
    }

    private static int CountDraws(int seed, int state)
    {
        var random = new GameRandom(seed);
        int draws = 0;
        while (random.State != state)
        {
            random.Next();
            draws++;
        }

        return draws;
    }

    private sealed record Sample(int Time, float X, float Y, float Speed);

    private sealed record FightSample(int Time, float X, float Y, float Speed, int Health);

    private sealed record FightFixture(int[] LoadingScreenRecords, int[] Draws, FightSample[] Samples);
}
