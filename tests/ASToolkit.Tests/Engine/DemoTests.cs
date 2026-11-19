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
        var map = EngineTestMap.CreateMap(new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects));
        map.StartDemoRecording("maps\\level_02.map"u8, 1234);
        map.Input.Flags = InputFlags.Right | InputFlags.First;
        map.Input.Cursor = new Vector2(120.5f, -40.25f);
        map.Input.Key = 's';
        map.StepDemo(0, out _);
        map.AdvanceClock();
        map.Input.Flags = InputFlags.None;
        map.StepDemo(0, out _);

        var demo = new DemoResource(map.FinishDemoRecording());

        Assert.Equal(1234, demo.Seed);
        Assert.Equal(GameClock.LevelStartTime, demo.StartTime);
        Assert.Equal("maps\\level_02.map"u8.ToArray(), demo.MapName.ToArray());
        int offset = map.Script.ReadState(demo.Body);
        Assert.True(demo.TryReadFrame(ref offset, out var first));
        Assert.True(demo.TryReadFrame(ref offset, out var second));
        Assert.False(demo.TryReadFrame(ref offset, out _));
        Assert.Equal(GameClock.LevelStartTime, first.Time);
        Assert.Equal(InputFlags.Right | InputFlags.First, first.Input.Flags);
        Assert.Equal(new Vector2(120.5f, -40.25f), first.Input.Cursor);
        Assert.Equal('s', first.Input.Key);
        Assert.Equal(map.Time, second.Time);
        Assert.Equal(InputFlags.None, second.Input.Flags);
        Assert.Equal(demo.Body.Length, offset);
    }

    [Fact]
    public void StepDemo_PastTheLastFrame_QueuesTheDemosMap()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = EngineTestMap.CreateMap(definitions);
        recorder.StartDemoRecording("maps\\level_02.map"u8, 1);
        recorder.StepDemo(0, out _);
        var demo = new DemoResource(recorder.FinishDemoRecording());
        var player = EngineTestMap.CreateMap(definitions, demo: demo);

        player.StepDemo(0, out _);
        player.AdvanceClock();
        bool drawn = player.StepDemo(0, out _);

        Assert.False(drawn);
        Assert.False(player.IsPlayingDemo);
        Assert.False(player.IsQuitting);
        Assert.Equal("maps\\level_02.map"u8.ToArray(), player.MapToLoad);
    }
}
