using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class DemoTests
{
    [Fact]
    public void FinishDemoRecording_RecordedFrames_ReadBackThroughDemoResource()
    {
        var map = EngineTestMap.CreateMap(session: CreateRecordingSession());
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.Right, 0);
        map.Input.PostMessage(WindowMessageId.HitTest, 0, 40 << 16 | 120);
        map.Input.PostMessage(WindowMessageId.LeftButtonDown, 0, 0);
        map.Input.PostKey(VirtualKey.X, 'x');
        map.AdvanceClock();
        int firstTime = map.Time;
        var cursor = new Vector2(120, 40) + map.Camera;
        map.StepDemo(0, out _);
        map.Input.PostMessage(WindowMessageId.KeyUp, (int)VirtualKey.Right, 0);
        map.Input.PostMessage(WindowMessageId.LeftButtonUp, 0, 0);
        map.AdvanceClock();
        map.StepDemo(0, out _);

        var demo = new DemoResource(map.FinishDemoRecording());

        Assert.Equal(1, demo.Seed);
        Assert.Equal(GameClock.LevelStartTime, demo.StartTime);
        Assert.Equal("maps\\engine_test.map"u8.ToArray(), demo.MapName.ToArray());
        int offset = map.Script.ReadState(demo.Body);
        Assert.True(demo.TryReadFrame(ref offset, out var first));
        Assert.True(demo.TryReadFrame(ref offset, out var second));
        Assert.False(demo.TryReadFrame(ref offset, out _));
        Assert.Equal(firstTime, first.Time);
        Assert.Equal(InputFlags.Right | InputFlags.First | InputFlags.LeftClick | InputFlags.LeftDown, first.Input.Flags);
        Assert.Equal(cursor, first.Input.Cursor);
        Assert.Equal('x', first.Input.Key);
        Assert.Equal(map.Time, second.Time);
        Assert.Equal(InputFlags.LeftClickRelease, second.Input.Flags);
        Assert.Equal(demo.Body.Length, offset);
    }

    [Fact]
    public void StepDemo_PastTheLastFrame_QueuesTheDemosMap()
    {
        var recorder = EngineTestMap.CreateMap(session: CreateRecordingSession());
        recorder.StepDemo(0, out _);
        var demo = new DemoResource(recorder.FinishDemoRecording());
        var player = EngineTestMap.CreateMap(demo: demo);

        player.StepDemo(0, out _);
        player.AdvanceClock();
        bool drawn = player.StepDemo(0, out _);

        Assert.False(drawn);
        Assert.False(player.IsPlayingDemo);
        Assert.Empty(player.Session.PreviousMapName.ToArray());
        Assert.False(player.AdvanceClock());
        Assert.Equal("maps\\engine_test.map"u8.ToArray(), player.Session.PreviousMapName.ToArray());
    }

    private static GameSession CreateRecordingSession() => new(new Registry("")) { IsRecordingNextMap = true };
}
