using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MyErrorTests
{
    private const string DatedPath = "logs\\error2026-01-01 12h00m00.log";
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_FirstLine_IsTheHeaderInTheDatedFile()
    {
        var fileSystem = new MemoryWriteOverlay();

        using (new MyError(fileSystem, new FakeTimeProvider(StartTime), "AlienShooter.exe"))
        {
            Assert.Equal("----< 2026-01-01 12:00:00 >----< AlienShooter.exe (Cr-1601-01-01 12:00:00 La-1601-01-01 12:00:00 Lw-1601-01-01 12:00:00) >----\r\n", Read(fileSystem, DatedPath));
        }
    }

    [Fact]
    public void Error_FormatsTheValueAndTextAfterTheTag()
    {
        var fileSystem = new MemoryWriteOverlay();
        var error = new MyError(fileSystem, new FakeTimeProvider(StartTime), "AlienShooter.exe");
        string name = "смерть 1 Spawn ЛЕТУН";
        error.Error($"VID [881-{name}]", LogError.Invalid, 0x520, "child"u8);
        error.Error("SPRITE -1", LogError.Invalid, 1, "noRef at Release"u8);
        error.Error($"VID [881-{name}]", LogError.Invalid, -1311, "child"u8);

        string[] lines = Read(fileSystem, DatedPath).Split("\r\n");
        Assert.Equal("!!!ERROR 12:00:00!!!VID [881-смерть 1 Spawn ЛЕТУН]: 0x520 Invalid child", lines[1]);
        Assert.Equal("!!!ERROR 12:00:00!!!SPRITE -1: 0x1 Invalid noRef at Release", lines[2]);
        Assert.Equal("!!!ERROR 12:00:00!!!VID [881-смерть 1 Spawn ЛЕТУН]: 0xFFFFFAE1 Invalid child", lines[3]);
    }

    [Fact]
    public void Write_Widths_PadTheNumbers()
    {
        var fileSystem = new MemoryWriteOverlay();
        var error = new MyError(fileSystem, new FakeTimeProvider(StartTime), "AlienShooter.exe");
        error.Write($"LoadVid::No   ={1001,-15}   sizeof(VID)   ={1156,-8}");
        error.Write($"NoVid[{3,3}]={1} Layer={13}");

        string[] lines = Read(fileSystem, DatedPath).Split("\r\n");
        Assert.Equal("LoadVid::No   =1001              sizeof(VID)   =1156    ", lines[1]);
        Assert.Equal("NoVid[  3]=1 Layer=13", lines[2]);
    }

    [Fact]
    public void Dispose_RenamesTheDatedFileOverTheLastLog()
    {
        var fileSystem = new MemoryWriteOverlay();
        using (var stale = fileSystem.OpenWrite("logs\\error.log", false)!)
        {
            stale.Write("old"u8);
        }

        new MyError(fileSystem, new FakeTimeProvider(StartTime), "AlienShooter.exe").Dispose();

        Assert.False(fileSystem.TryRead(DatedPath, out _));
        Assert.StartsWith("----< 2026-01-01", Read(fileSystem, "logs\\error.log"));
    }

    [Fact]
    public void Write_WithoutFiles_WritesNothing()
    {
        var error = new MyError(null, new FakeTimeProvider(StartTime), "AlienShooter.exe");
        error.Write("x"u8);

        Assert.False(error.IsOpen);
    }

    [Fact]
    public void Map_LoadAndEnd_LogTheScriptAndTheDefinitionsLeft()
    {
        var fileSystem = new MemoryWriteOverlay();
        using (var session = new GameSession(EngineTestMap.CreateRegistry(), fileSystem, new SteppedClock(new FakeTimeProvider(StartTime))))
        {
            using var map = EngineTestMap.CreateMap(session: session);
            var scriptFileSystem = new MemoryWriteOverlay(new GameFolder(TestGameFolder.RequireFolder()));
            scriptFileSystem.Set("maps\\log.lgc", "#include \"maps\\export.lgc\"\nmain()\n{\n  Log(\"from the script\");\n}\n"u8.ToArray());
            Assert.True(map.Script.Load(scriptFileSystem, "maps\\log.lgc"));
            map.Script.CallFunction(map, -1, 0, 0, 0);
        }

        string[] lines = Read(fileSystem, "logs\\error.log").Split("\r\n");
        int error = Array.IndexOf(lines, "!!!ERROR 12:00:00!!!MAP: 0x0 Section can't found (load 'VID')");
        Assert.True(error > 0);
        Assert.Equal(["Player release", "Sprite release", "Menu   release", "Average fps=0", "Script release", "!!!ERROR 12:00:00!!!RES 'maps\\engine_test.map' 'MAP ': 0x0 Invalid resource type"],
            lines[(error - 6)..error]);
        Assert.StartsWith("CurrentTime   =10                sizeof(SPRITE)=112      Map version   =12", lines[error + 1]);
        Assert.Contains(lines, line => line.StartsWith("LoadScript::ByteCode="));
        Assert.Contains("from the script", lines);
        Assert.Contains(lines, line => line.StartsWith("NoVid[  9]="));
        Assert.Equal(["Player release", "Sprite release", "Menu   release"], lines.SkipWhile(line => line != "Player release").Take(3));
        Assert.Equal(2, lines.Count(line => line.StartsWith("Vid    release ", StringComparison.Ordinal)));
        Assert.StartsWith("Vid    release ", lines.Last(line => line.Length != 0));
    }

    private static string Read(MemoryWriteOverlay fileSystem, string path)
    {
        Assert.True(fileSystem.TryRead(path, out var data));

        return GameTextEncoding.Cyrillic.GetString(data.Span);
    }
}
