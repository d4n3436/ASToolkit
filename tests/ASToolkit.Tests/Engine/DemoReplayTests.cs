using System.Text;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class DemoReplayTests
{
    private const int ClockReadsBeforeTheLoadBar = 3;

    [Theory]
    [InlineData("addon2-level1-walk")]
    [InlineData("level02-walk")]
    [InlineData("level03-walk")]
    public void DemoTact_RecordedWalk_MatchesTheGameExactly(string fixture)
    {
        var samples = FixtureReader.Read<WalkSample[]>(fixture + ".json");
        var points = Replay(LoadDemo(fixture), LoadDemo(fixture, isRequested: false));
        var stateBeforeFrame = new Dictionary<int, ReplayPoint>();
        for (int i = 1; i < points.Count; i++)
        {
            stateBeforeFrame.Add(points[i].Time, points[i - 1]);
        }

        int compared = 0;
        foreach (var sample in samples)
        {
            if (!stateBeforeFrame.TryGetValue(sample.Time, out var state))
                continue;

            Assert.Equal(sample.X, state.Position.X, 0f);
            Assert.Equal(sample.Y, state.Position.Y, 0f);
            Assert.Equal(sample.Speed, state.Speed);
            compared++;
        }

        Assert.True(compared > 300);
    }

    [Fact]
    public void DemoTact_RecordedFight_MatchesTheGamesDrawsAndPlayer()
    {
        var fixture = FixtureReader.Read<FightFixture>("level09-fire.json");
        var map = LoadDemo("level09-fire", fixture.LoadingScreenRecords);
        int seed = ReadDemo("level09-fire").Seed;
        var points = Replay(map, LoadDemo("level09-fire", fixture.LoadingScreenRecords, isRequested: false));

        Assert.NotEmpty(fixture.Draws);
        Assert.NotEmpty(fixture.Samples);
        for (int frame = 1; frame <= fixture.Draws.Length; frame++)
        {
            Assert.Equal(fixture.Draws[frame - 1], CountDraws(seed, points[frame].RandomState));
        }

        var stateBeforeFrame = new Dictionary<int, ReplayPoint>();
        for (int i = 1; i < points.Count; i++)
        {
            stateBeforeFrame.Add(points[i].Time, points[i - 1]);
        }

        foreach (var sample in fixture.Samples)
        {
            var state = stateBeforeFrame[sample.Time];

            Assert.Equal(sample.X, state.Position.X, 0f);
            Assert.Equal(sample.Y, state.Position.Y, 0f);
            Assert.Equal(sample.Speed, state.Speed);
            Assert.Equal(sample.Health, state.Health);
        }
    }

    [Fact]
    public void Map_RecordedDemoLoad_BuildsTheSpritesWithTheGamesDraws()
    {
        int[][] sprites = FixtureReader.Read<int[][]>("addon2-level1-load.json");
        var demo = ReadDemo("addon2-level1-load");
        var map = LoadDemo("addon2-level1-load", isRequested: false);
        var layer = map.GetLayer(1);

        Assert.Equal(10623 - 2, CountDraws(demo.Seed, map.RandomState));
        for (int slot = 0; slot < sprites.Length; slot++)
        {
            var sprite = layer[slot]!;
            int[] expected = sprites[slot];

            int[] actual = [(int)sprite.Vid.Id, sprite.LastTickTime, sprite.FirstFrame, sprite.Frame, sprite.LastFrame, (int)sprite.Animation, sprite.Direction.Value];

            Assert.Equal(expected, actual);
        }
    }

    private static DemoResource ReadDemo(string fixture) => new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".dem")));

    private static Map LoadDemo(string fixture, IReadOnlyList<int>? loadingScreenRecords = null, bool isRequested = true)
    {
        string game = TestGameFolder.RequireFolder();
        string demoPath = "demos\\" + fixture + ".dem";
        var files = new MemoryWriteOverlay(new GameFolder(game));
        files.Set(demoPath, ReadDemo(fixture).Data);

        var clock = new SteppedClock();
        var registry = new Registry("");
        var map = new MapSteam(TestGameFolder.Objects, files, new GameSession(registry, time: clock));
        clock.Schedule((loadingScreenRecords ?? []).Select(record => record + ClockReadsBeforeTheLoadBar));
        map.Input.PostMessage(WindowMessageId.ActivateApp, 1, 0);
        if (isRequested)
        {
            map.RequestLoad(Encoding.ASCII.GetBytes(demoPath));
        }
        else
        {
            map.LoadAt(Encoding.ASCII.GetBytes(demoPath), EngineTestMap.StartRealTime);
        }

        return map;
    }

    private static List<ReplayPoint> Replay(Map map, Map loaded)
    {
        var points = new List<ReplayPoint> { GetPoint(loaded) };
        while (true)
        {
            uint realTime = map.TryGetNextDemoTime(out int next) ? map.RealCurrentTime + (uint)(next - map.CurrentTime) : map.GetPacer().TakeNextRealTime(map);
            if (map.TactAt(realTime) || !map.IsPlayingDemo)
                break;

            points.Add(GetPoint(map));
        }

        return points;
    }

    private static ReplayPoint GetPoint(Map map)
    {
        var man = map.Flagman(0) ?? EngineTestMap.FindPlacedMan(map);

        return new ReplayPoint(map.CurrentTime, man.Position, man.Speed, man.Hp, map.RandomState);
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
}
