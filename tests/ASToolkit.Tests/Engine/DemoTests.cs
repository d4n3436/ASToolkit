using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class DemoTests
{
    [Fact]
    public void FinishDemoRecording_RecordedFrames_ReadBackThroughDemoResource()
    {
        var world = EngineTestWorld.CreateWorld(new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects));
        world.StartDemoRecording("maps\\level_02.map"u8, 1234, new byte[sizeof(int)]);
        world.Input.Flags = InputFlags.Right | InputFlags.First;
        world.Input.Cursor = new Vector2(120.5f, -40.25f);
        world.Input.Key = 's';
        world.StepDemo(0, out _);
        world.AdvanceClock();
        world.Input.Flags = InputFlags.None;
        world.StepDemo(0, out _);

        var demo = new DemoResource(world.FinishDemoRecording());

        Assert.Equal(1234u, demo.Seed);
        Assert.Equal(GameClock.LevelStartTime, demo.StartTime);
        Assert.Equal("maps\\level_02.map"u8.ToArray(), demo.MapName.ToArray());
        Assert.Equal(2, demo.Frames.Length);
        Assert.Equal(GameClock.LevelStartTime, demo.Frames[0].Time);
        Assert.Equal(InputFlags.Right | InputFlags.First, demo.Frames[0].Input.Flags);
        Assert.Equal(new Vector2(120.5f, -40.25f), demo.Frames[0].Input.Cursor);
        Assert.Equal('s', demo.Frames[0].Input.Key);
        Assert.Equal(world.Time, demo.Frames[1].Time);
        Assert.Equal(InputFlags.None, demo.Frames[1].Input.Flags);
    }

    [Fact]
    public void StepDemo_PastTheLastFrame_QueuesTheDemosMap()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = EngineTestWorld.CreateWorld(kinds);
        recorder.StartDemoRecording("maps\\level_02.map"u8, 1, new byte[sizeof(int)]);
        recorder.StepDemo(0, out _);
        var demo = new DemoResource(recorder.FinishDemoRecording());
        var player = EngineTestWorld.CreateWorld(kinds);
        player.StartDemoPlayback(demo);

        player.StepDemo(0, out _);
        player.AdvanceClock();
        bool drawn = player.StepDemo(0, out _);

        Assert.False(drawn);
        Assert.False(player.IsPlayingDemo);
        Assert.False(player.IsQuitting);
        Assert.Equal("maps\\level_02.map"u8.ToArray(), player.MapToLoad);
    }
}
