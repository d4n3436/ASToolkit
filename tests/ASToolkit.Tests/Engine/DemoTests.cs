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
        var map = EngineTestMap.CreateMap(recordDemo: true);
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.Right, 0);
        map.Input.PostMessage(WindowMessageId.HitTest, 0, 40 << 16 | 120);
        map.Input.PostMessage(WindowMessageId.LeftButtonDown, 0, 0);
        map.Input.PostKey(VirtualKey.X, 'x');
        map.StartPacedTact();
        int firstTime = map.CurrentTime;
        var cursor = new Vector2(120, 40) + map.Shift;
        map.DemoTact();
        map.Input.PostMessage(WindowMessageId.KeyUp, (int)VirtualKey.Right, 0);
        map.Input.PostMessage(WindowMessageId.LeftButtonUp, 0, 0);
        map.StartPacedTact();
        map.DemoTact();

        var demo = new DemoResource(map.FinishDemoRecording());

        Assert.Equal(1, demo.Seed);
        Assert.Equal(Map.LevelStartTime, demo.StartTime);
        Assert.Equal("maps\\engine_test.map"u8.ToArray(), demo.MapName.ToArray());
        int offset = map.Script.LoadVar(demo.Body);
        Assert.True(demo.TryReadFrame(ref offset, out var first));
        Assert.True(demo.TryReadFrame(ref offset, out var second));
        Assert.False(demo.TryReadFrame(ref offset, out _));
        Assert.Equal(firstTime, first.Time);
        Assert.Equal(InputFlags.Right | InputFlags.First | InputFlags.LeftClick | InputFlags.LeftDown, first.Input.Flags);
        Assert.Equal(cursor, first.Input.Cursor);
        Assert.Equal('x', first.Input.Key);
        Assert.Equal(map.CurrentTime, second.Time);
        Assert.Equal(InputFlags.LeftClickRelease, second.Input.Flags);
        Assert.Equal(demo.Body.Length, offset);
    }

    [Fact]
    public void DemoTact_PastTheLastFrame_QueuesTheDemosMap()
    {
        var recorder = EngineTestMap.CreateMap(recordDemo: true);
        recorder.DemoTact();
        var demo = new DemoResource(recorder.FinishDemoRecording());
        var player = EngineTestMap.CreateMap(demo: demo);

        player.DemoTact();
        player.StartPacedTact();
        bool drawn = player.DemoTact();

        Assert.False(drawn);
        Assert.False(player.IsPlayingDemo);
        Assert.Equal("maps\\logo.map"u8.ToArray(), player.PreviousMapName.ToArray());
        Assert.False(player.StartPacedTact());
        Assert.Equal("maps\\engine_test.map"u8.ToArray(), player.PreviousMapName.ToArray());
    }
}
