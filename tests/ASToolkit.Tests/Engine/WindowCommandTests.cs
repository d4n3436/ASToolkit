using System.Buffers.Binary;
using System.Drawing;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class WindowCommandTests
{
    private const string ScreenshotPath = "Screens\\2026-01-01 12h0000.tga";
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Step_ShiftF9_WritesTheScreenAsATgaFile()
    {
        var (map, files) = CreateMap();
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.Shift, 0);
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.F9, 0);
        map.Step();

        Assert.True(files.TryRead(ScreenshotPath, out var written));
        byte[] data = written.ToArray();
        var size = Size.Truncate(map.Session.Graphics.ScreenSize);
        Assert.Equal(2, data[2]);
        Assert.Equal(size.Width, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)));
        Assert.Equal(size.Height, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)));
        Assert.Equal(24, data[16]);
        Assert.Equal(18 + size.Width * size.Height * 3, data.Length);
        Assert.NotEqual(VirtualKey.None, map.Input.VirtualKey);
    }

    [Fact]
    public void Step_F9WithoutShift_ReachesTheInputInstead()
    {
        var (map, files) = CreateMap();
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.Shift, 0);
        map.Input.PostMessage(WindowMessageId.KeyUp, (int)VirtualKey.Shift, 0);
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.F9, 0);
        map.Step();

        Assert.False(files.TryRead(ScreenshotPath, out _));
        Assert.Equal(VirtualKey.F9, map.Input.VirtualKey);
    }

    [Fact]
    public void Step_F12_IsNotAnAccelerator()
    {
        var (map, _) = CreateMap();
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.F12, 0);

        Assert.False(map.Step());
        Assert.Equal(VirtualKey.F12, map.Input.VirtualKey);
    }

    [Fact]
    public void Step_F3WithACanceledDialog_KeepsTheMap()
    {
        var (map, _) = CreateMap();
        byte[] before = map.MapName.ToArray();
        map.Input.PostMessage(WindowMessageId.KeyDown, (int)VirtualKey.F3, 0);
        map.Step();

        Assert.Equal(before, map.MapName.ToArray());
    }

    private static (Map Map, MemoryWriteOverlay Files) CreateMap()
    {
        var files = EngineTestMap.CreateFiles();
        var registry = Playthrough.CreateRegistry(TestGameFolder.RequireFolder(), "Level_02.map", new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default));
        var map = new MapSteam(TestGameFolder.Objects, files, new GameSession(registry, time: new SteppedClock(new FakeTimeProvider(StartTime))));
        map.Input.PostMessage(WindowMessageId.ActivateApp, 1, 0);
        map.Pace([FramePacer.DefaultFrameTime]);
        map.LoadAt(EngineTestMap.MapName, EngineTestMap.StartRealTime);
        map.Session.Graphics.Screen = new Screen(Size.Ceiling(map.Session.Graphics.ScreenSize), true);
        map.Step();

        return (map, files);
    }
}
