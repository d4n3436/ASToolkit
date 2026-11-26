using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ASToolkit.Tests.Engine;

public sealed class VidSoftwareTests
{
    private const string LogPath = "logs\\error2026-01-01 12h00m00.log";
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Load_RangeCodedFrames_MatchTheUncodedFrames()
    {
        using var session = new GameSession();
        var map = CreateMap(session);
        string folder = Path.Combine(TestGameFolder.RequireFolder(), "Vid");
        int checkedFiles = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*.vid"))
        {
            byte[] data = File.ReadAllBytes(path);
            var file = new VidResource(data);
            if (file.Type.HasFlag(VidType.Hardware) || file.Type.HasFlag(VidType.RangeCoded) || !file.Type.HasFlag(VidType.Palette) || file.FrameRecords.Length < 2)
                continue;

            var plain = LoadSoftware(file, data, map);
            byte[] codedData = RangeCode(data, true);
            var coded = LoadSoftware(new VidResource(codedData), codedData, map);

            Assert.True(coded.FileType.HasFlag(VidType.RangeCoded));
            Assert.Equal(plain.FrameOffsets, coded.FrameOffsets);
            int length = Math.Min(plain.Frames.Length, coded.Frames.Length);
            Assert.True(plain.Frames[..length].SequenceEqual(coded.Frames[..length]));
            if (++checkedFiles == 4)
                return;
        }

        Assert.True(checkedFiles > 0);
    }

    [Fact]
    public void Load_FileWithoutData_ReadsTheLastChunkLikeTheGame()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Vid", "926.vid"));
        var file = new VidResource(data);
        var fileSystem = new MemoryWriteOverlay();
        using var session = new GameSession(fileSystem: fileSystem, time: new FakeTimeProvider(StartTime));
        var map = CreateMap(session);
        int logged = ReadLog(fileSystem).Length;
        var vid = LoadSoftware(file, data, map);

        string error = Assert.Single(ReadLog(fileSystem)[logged..], line => line.StartsWith("!!!ERROR", StringComparison.Ordinal));
        Assert.EndsWith(": 0x0 Load DATA", error);
        Assert.Equal(59668, vid.Frames.Length);
        Assert.Equal(new int[file.FrameCount], vid.FrameOffsets);
    }

    private static Map CreateMap(GameSession session) => new MapSteam(TestGameFolder.RequireFolder(), TestGameFolder.Objects, new GameFolder(TestGameFolder.RequireFolder()), session);

    private static string[] ReadLog(MemoryWriteOverlay fileSystem)
    {
        Assert.True(fileSystem.TryRead(LogPath, out var log));

        return GameTextEncoding.Cyrillic.GetString(log.Span).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static VidSoftware LoadSoftware(VidResource file, byte[] data, Map map)
    {
        var vid = new VidSoftware(map, KnownObject.None, new GameObject(SpriteClass.Primitive, new Vector3(24, 16, 20)), [default], file, file.FrameCount, HighColorFormat.Rgb565);
        vid.Load(file, data);

        return vid;
    }

    [Fact]
    public void Load_RangeCodedFramesPastTheDataChunk_Throw()
    {
        using var session = new GameSession();
        var map = CreateMap(session);
        string folder = Path.Combine(TestGameFolder.RequireFolder(), "Vid");
        foreach (string path in Directory.EnumerateFiles(folder, "*.vid"))
        {
            byte[] data = File.ReadAllBytes(path);
            var file = new VidResource(data);
            if (file.Type.HasFlag(VidType.Hardware) || file.Type.HasFlag(VidType.RangeCoded) || !file.Type.HasFlag(VidType.Palette) || file.FrameRecords.Length < 2)
                continue;

            byte[] codedData = RangeCode(data, false);
            Assert.Throws<InvalidDataException>(() => LoadSoftware(new VidResource(codedData), codedData, map));
            return;
        }

        Assert.Fail("Could not find a software sprite file with a palette.");
    }

    private static byte[] RangeCode(byte[] data, bool pad)
    {
        var container = ResContainerBuilder.Parse(data);
        byte[] head = container.GetChunk("HEAD").Records[0];
        head[1] |= (byte)((int)VidType.RangeCoded >> 8);
        var chunk = container.GetChunk("DATA");
        var coder = new RangeCoder(1);
        for (int i = 0; i < chunk.Records.Count; i++)
        {
            byte[] source = chunk.Records[i];
            var record = new ResRecordBuilder();
            record.Write(source.AsSpan(0, sizeof(int)));
            record.WriteBody(source.AsSpan(sizeof(int)), coder);
            chunk.Records[i] = record.Bytes.ToArray();
        }

        if (pad)
        {
            int decoded = 0;
            foreach (byte[] record in chunk.Records)
            {
                decoded += BitConverter.ToInt32(record);
            }

            chunk.Records.Add(new byte[decoded]);
        }

        return container.Build();
    }
}
