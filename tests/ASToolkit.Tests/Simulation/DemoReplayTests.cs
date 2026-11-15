using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Simulation;

public class DemoReplayTests
{
    [Fact]
    public void ReplayDemo_RecordedWalk_MatchesTheGameExactly()
    {
        string game = TestGameFolder.RequireFolder();
        var demo = new DemoResource(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "addon2-level1-walk.dem")));
        var samples = FixtureReader.Read<Sample[]>("addon2-level1-walk.json");
        var level = new LevelLoader(TestGameFolder.Objects).Load("ADDON2/Level_01.map", Path.Combine(game, "Maps", "ADDON2", "Level_01.map"))!.Analysis;
        var points = new Playthrough(game, TestGameFolder.Objects, TestGameFolder.Table).ReplayDemo(level, demo, CampaignState.CreateInitial().Loadout);
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

    private sealed record Sample(int Time, float X, float Y, float Speed);
}
