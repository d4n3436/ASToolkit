using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MapLoadTests
{
    private const string OldMapPath = "maps\\old.map";
    private const int EndHandle = -1;

    [Fact]
    public void Load_MissingFile_LogsTheInvalidMapAndStaysLoading()
    {
        var logs = new MemoryWriteOverlay();
        using (var session = new GameSession(new Registry(""), logs, new LoadingRedrawClock()))
        {
            var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog(), session: session);
            map.Load("maps\\missing.map"u8, GameClock.StartWallTime);

            Assert.True(map.IsLoading);
        }

        Assert.True(logs.TryRead("logs\\error.log", out var log));
        Assert.Contains("!!!ERROR!!!LOAD: Invalid map file maps\\missing.map", GameTextEncoding.Cyrillic.GetString(log.Span).Split("\r\n"));
    }

    [Fact]
    public void Load_MapWithoutGraphics_ReadsTheOldHeaderAndSprites()
    {
        var files = EngineTestMap.CreateFiles();
        Assert.True(files.TryRead("maps\\engine_test.lgc", out var script));
        files.Set("maps\\old.lgc", script);
        files.Set(OldMapPath, CreateOldMap());
        var registry = new Registry("");
        var map = new Map(TestGameFolder.CreateCatalog(), files, registry, new GameSession(registry, time: new LoadingRedrawClock()));

        map.Load("maps\\old.map"u8, GameClock.StartWallTime);

        Assert.False(map.IsLoading);
        Assert.Equal(new SizeF(2000, 1600), map.Size);
        Assert.Equal(new Vector3(300, 400, 0), map.FindFirstSprite(KnownObject.Crate).Position);
    }

    [Fact]
    public void Load_InDebugMode_PresentsAFrameForEveryLoadingText()
    {
        var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog());
        var device = new RecordingGraphicsDevice();
        map.Session.Graphics.Device = device;

        map.Load(EngineTestMap.MapName, GameClock.StartWallTime);

        Assert.True(map.Session.IsDebugMode);
        Assert.Equal(14, device.Presented.Count);
        Assert.Equal(device.Scenes, device.EndedScenes);
        Assert.Empty(map.Session.Graphics.LoadingText.ToArray());
    }

    [Fact]
    public void Step_ActiveWindow_PresentsTheFrame()
    {
        var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog());
        var device = new RecordingGraphicsDevice();
        map.Session.Graphics.Device = device;

        map.Step();
        map.Step();

        Assert.Equal(2, device.Presented.Count);
        Assert.Equal(2, device.EndedScenes);
    }

    private static byte[] CreateOldMap()
    {
        var head = new List<byte>();
        head.AddRange(BitConverter.GetBytes(2000));
        head.AddRange(BitConverter.GetBytes(1600));
        head.AddRange(BitConverter.GetBytes((short)0));
        head.AddRange(BitConverter.GetBytes((short)0));
        head.AddRange(BitConverter.GetBytes(GameClock.LevelStartTime));
        head.AddRange(BitConverter.GetBytes(6));
        var sprites = new List<byte>();
        sprites.AddRange(BitConverter.GetBytes(1));
        sprites.AddRange(BitConverter.GetBytes((short)KnownObject.Crate));
        sprites.AddRange(BitConverter.GetBytes((short)300));
        sprites.AddRange(BitConverter.GetBytes((short)400));
        sprites.AddRange(BitConverter.GetBytes((short)0));
        sprites.Add(0);
        sprites.Add(0);
        sprites.AddRange(BitConverter.GetBytes(EndHandle));
        var file = new ResContainerBuilder { Magic = "RES "u8.ToArray(), Kind = "MAP "u8.ToArray() };
        file.Chunks.Add(new ResChunkBuilder { Tag = "HEAD", Flags = ResChunkFlags.ExtendedHeader, Records = [head.ToArray()] });
        file.Chunks.Add(new ResChunkBuilder { Tag = "SPR ", Flags = ResChunkFlags.ExtendedHeader, Records = [sprites.ToArray()] });
        file.Chunks.Add(new ResChunkBuilder { Tag = "SPRD", Flags = ResChunkFlags.ExtendedHeader, Records = [MemoryMarshal.AsBytes([EndHandle]).ToArray()] });

        return file.Build();
    }
}
