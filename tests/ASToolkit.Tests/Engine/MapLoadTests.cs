using System.Buffers.Binary;
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
    private const string ExtraMapPath = "maps\\extra.map";
    private const KnownObject ExtraId = (KnownObject)2000;
    private const int EndHandle = -1;

    [Fact]
    public void LoadVid_ObjectIdsSharingASpriteFile_JoinOneMirrorRing()
    {
        var map = EngineTestMap.CreateMap();
        var vids = map.Vids;
        int pairs = 0;
        for (int i = 0; i < vids.Length; i++)
        {
            var first = vids[i];
            if (first is null || string.IsNullOrEmpty(first.Path))
                continue;

            for (int j = i + 1; j < vids.Length; j++)
            {
                var second = vids[j];
                if (second is null || second.Path != first.Path || second.IsBuiltTerrainClass != first.IsBuiltTerrainClass)
                    continue;

                bool isHardware = first.FileType.HasFlag(VidType.Hardware);
                if (!isHardware && (second.ObjectGamma != first.ObjectGamma || second.Properties.HasFlag(ObjectProperties.Gamma) != first.Properties.HasFlag(ObjectProperties.Gamma)))
                    continue;

                var mirror = first.NextMirror;
                while (mirror != first && mirror != second)
                {
                    mirror = mirror.NextMirror;
                }

                Assert.Same(second, mirror);
                pairs++;
            }
        }

        Assert.True(pairs > 0);
    }

    [Fact]
    public void Load_MissingFile_LogsTheInvalidMapAndStaysLoading()
    {
        var logs = new MemoryWriteOverlay();
        using (var session = new GameSession(Playthrough.DisplaySettings, Playthrough.AdapterModes, EngineTestMap.CreateRegistry(), logs, new SteppedClock()))
        {
            var map = EngineTestMap.CreateMap(session: session);
            map.LoadAt("maps\\missing.map"u8, EngineTestMap.StartRealTime);

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
        var registry = EngineTestMap.CreateCaptureRegistry();
        var map = new MapSteam(TestGameFolder.Objects, files, new GameSession(EngineTestMap.CaptureDisplaySettings, EngineTestMap.CaptureDisplayModes, registry, time: new SteppedClock()));

        map.LoadAt("maps\\old.map"u8, EngineTestMap.StartRealTime);

        Assert.False(map.IsLoading);
        Assert.Equal(new SizeF(2000, 1600), map.Size);
        Assert.Equal(new Vector3(300, 400, 0), map.FindFirstSprite(KnownObject.Crate).Position);
    }

    [Fact]
    public void Load_MapWithAnObjChunk_AddsItsVidsUntilTheNextLoad()
    {
        Assert.DoesNotContain(ExtraId, TestGameFolder.Objects.Ids);
        var files = EngineTestMap.CreateFiles();
        Assert.True(files.TryRead(EngineTestMap.MapPath, out var plain));
        Assert.True(files.TryRead("maps\\engine_test.lgc", out var script));
        var objects = new ResContainer(File.ReadAllBytes(TestGameFolder.Objects.Path));
        byte[] record = objects.Chunks.First().Records.First(entry => BinaryPrimitives.ReadInt32LittleEndian(entry.Span) == (int)KnownObject.Crate).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(record, (int)ExtraId);
        var map = ResContainerBuilder.Parse(plain);
        map.Chunks.Add(new ResChunkBuilder { Tag = "OBJ ", Records = [record] });
        files.Set(ExtraMapPath, map.Build());
        files.Set("maps\\extra.lgc", script);
        var registry = EngineTestMap.CreateCaptureRegistry();
        var steam = new MapSteam(TestGameFolder.Objects, files, new GameSession(EngineTestMap.CaptureDisplaySettings, EngineTestMap.CaptureDisplayModes, registry, time: new SteppedClock()));

        steam.LoadAt("maps\\extra.map"u8, EngineTestMap.StartRealTime);
        var extra = steam.FindVid(ExtraId);
        steam.LoadAt(EngineTestMap.MapName, EngineTestMap.StartRealTime);

        Assert.NotNull(extra);
        Assert.True(extra.FileType.HasFlag(VidType.FrameVector));
        Assert.Equal(steam.FindVid(KnownObject.Crate)!.Path, extra.Path);
        Assert.False(steam.FindVid(KnownObject.Crate)!.FileType.HasFlag(VidType.FrameVector));
        Assert.Null(steam.FindVid(ExtraId));
    }

    [Fact]
    public void Load_InDebugMode_PresentsAFrameForEveryLoadingText()
    {
        var map = EngineTestMap.CreateMap();
        var device = new RecordingGraphicsDevice();
        map.Session.Graphics.Device = device;
        map.Constant.IsDebugMode = true;

        map.LoadAt(EngineTestMap.MapName, EngineTestMap.StartRealTime);

        Assert.Equal(14, device.Presented.Count);
        Assert.Equal(device.Scenes, device.EndedScenes);
        Assert.Empty(map.Session.Graphics.LoadingText.ToArray());
    }

    [Fact]
    public void Step_ActiveWindow_PresentsTheFrame()
    {
        var map = EngineTestMap.CreateMap();
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
        head.AddRange(BitConverter.GetBytes(Map.LevelStartTime));
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
