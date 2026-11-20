using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class DemoReplayTests
{
    [Theory]
    [InlineData("addon2-level1-walk")]
    [InlineData("level02-walk")]
    [InlineData("level03-walk")]
    public void StepDemo_RecordedWalk_MatchesTheGameExactly(string fixture)
    {
        var samples = FixtureReader.Read<Sample[]>(fixture + ".json");
        var points = Replay(LoadDemo(fixture));
        var stateBeforeFrame = new Dictionary<int, Point>();
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
    public void StepDemo_RecordedFight_MatchesTheGamesDrawsAndPlayer()
    {
        var fixture = FixtureReader.Read<FightFixture>("level09-fire.json");
        var map = LoadDemo("level09-fire", fixture.LoadingScreenRecords);
        int seed = ReadDemo("level09-fire").Seed;
        var points = Replay(map);

        Assert.NotEmpty(fixture.Draws);
        Assert.NotEmpty(fixture.Samples);
        for (int frame = 1; frame <= fixture.Draws.Length; frame++)
        {
            Assert.Equal(fixture.Draws[frame - 1], CountDraws(seed, points[frame].RandomState));
        }

        var stateBeforeFrame = new Dictionary<int, Point>();
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
    public void Map_RecordedDemoLoad_BuildsTheSpritesWithTheGamesDraws()
    {
        int[][] sprites = FixtureReader.Read<int[][]>("addon2-level1-load.json");
        var demo = ReadDemo("addon2-level1-load");
        var map = LoadDemo("addon2-level1-load");
        var layer = map.GetLayer(1);

        Assert.Equal(10623 - 2, CountDraws(demo.Seed, map.RandomState));
        for (int slot = 0; slot < sprites.Length; slot++)
        {
            var sprite = layer[slot].Sprite!;
            int[] expected = sprites[slot];

            int[] actual = [(int)sprite.Definition.Id, sprite.LastTickTime, sprite.FirstFrame, sprite.Frame, sprite.LastFrame, (int)sprite.Animation, sprite.Direction.Value];

            Assert.Equal(expected, actual);
        }
    }

    private static DemoResource ReadDemo(string fixture) => new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".dem")));

    private static Map LoadDemo(string fixture, IReadOnlyList<int>? loadingScreenRecords = null)
    {
        string game = TestGameFolder.RequireFolder();
        var demo = ReadDemo(fixture);
        string mapPath = GameTextEncoding.Cyrillic.GetString(demo.MapName);
        var template = LevelTemplate.FromMap(new GameFolder(game), mapPath, new SpriteCatalog(game, TestGameFolder.Objects),
            TestGameFolder.Table.GetArray("GettingAmmo")) with { LoadingScreenRecords = loadingScreenRecords ?? [] };

        return new Map(template, new GameRandom(demo.Seed), demo: demo);
    }

    private static List<Point> Replay(Map map)
    {
        var points = new List<Point> { GetPoint(map) };
        uint now = 0;
        while (true)
        {
            bool drawFrame = map.StepDemo(now, out now);

            if (!map.IsPlayingDemo)
                break;

            map.Step(drawFrame);
            points.Add(GetPoint(map));

            if (map.TryGetNextDemoTime(out int next))
            {
                map.AdvanceClock(map.WallTime + (uint)(next - map.Time));
            }
            else
            {
                map.AdvanceClock();
            }
        }

        return points;
    }

    private static Point GetPoint(Map map)
    {
        var man = map.HasPlayer ? map.Player : EngineTestMap.FindPlacedMan(map);

        return new Point(map.Time, man.Position, man.Speed, man.Hp, map.RandomState);
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

    private sealed record Point(int Time, Vector3 Position, float Speed, int Health, int RandomState);

    private sealed record Sample(int Time, float X, float Y, float Speed);

    private sealed record FightSample(int Time, float X, float Y, float Speed, int Health);

    private sealed record FightFixture(int[] LoadingScreenRecords, int[] Draws, FightSample[] Samples);
}
