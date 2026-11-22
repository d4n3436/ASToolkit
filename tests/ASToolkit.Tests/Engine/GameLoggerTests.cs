using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class GameLoggerTests
{
    private const string DatedPath = "logs\\error2026-01-01 12h00m00.log";

    [Fact]
    public void Write_FirstLine_IsTheHeaderInTheDatedFile()
    {
        var fileSystem = new MemoryWriteOverlay();

        using (new GameLogger(fileSystem, new FixedTime(), "AlienShooter.exe"))
        {
            Assert.Equal("----< 2026-01-01 12:00:00 >----< AlienShooter.exe () >----\r\n", Read(fileSystem, DatedPath));
        }
    }

    [Fact]
    public void Error_SecondPass_FormatsTheValueAndTextAfterTheTag()
    {
        var fileSystem = new MemoryWriteOverlay();
        var logger = new GameLogger(fileSystem, new FixedTime(), "AlienShooter.exe");
        var name = (ReadOnlyMemory<byte>)GameTextEncoding.Cyrillic.GetBytes("смерть 1 Spawn ЛЕТУН");
        logger.Error("VID [%i-%s]"u8, LogError.Invalid, 0x520, "child"u8, 881, name);
        logger.Error("SPRITE %i"u8, LogError.Invalid, 1, "noRef at Release"u8, -1);
        logger.Error("VID [%i-%s]"u8, LogError.Invalid, -1311, "child"u8, 881, name);

        string[] lines = Read(fileSystem, DatedPath).Split("\r\n");
        Assert.Equal("!!!ERROR 12:00:00!!!VID [881-смерть 1 Spawn ЛЕТУН]: 0x520 Invalid child", lines[1]);
        Assert.Equal("!!!ERROR 12:00:00!!!SPRITE -1: 0x1 Invalid noRef at Release", lines[2]);
        Assert.Equal("!!!ERROR 12:00:00!!!VID [881-смерть 1 Spawn ЛЕТУН]: 0xFFFFFAE1 Invalid child", lines[3]);
    }

    [Fact]
    public void Write_Widths_PadAsTheCRuntimeDoes()
    {
        var fileSystem = new MemoryWriteOverlay();
        var logger = new GameLogger(fileSystem, new FixedTime(), "AlienShooter.exe");
        logger.Write("LoadVid::No   =%-15i   sizeof(VID)   =%-8i"u8, 1001, 1156);
        logger.Write("NoVid[%3i]=%i Layer=%i"u8, 3, 1, 13);
        logger.Write("%02i|%5s|%-5s|%.2s|%c|%x|%#x|%u"u8, 7, (ReadOnlyMemory<byte>)"ab"u8.ToArray(), (ReadOnlyMemory<byte>)"cd"u8.ToArray(), (ReadOnlyMemory<byte>)"efg"u8.ToArray(), 'z', 255, 255, -1);

        string[] lines = Read(fileSystem, DatedPath).Split("\r\n");
        Assert.Equal("LoadVid::No   =1001              sizeof(VID)   =1156    ", lines[1]);
        Assert.Equal("NoVid[  3]=1 Layer=13", lines[2]);
        Assert.Equal("07|   ab|cd   |ef|z|ff|0xff|4294967295", lines[3]);
    }

    [Fact]
    public void Dispose_RenamesTheDatedFileOverTheLastLog()
    {
        var fileSystem = new MemoryWriteOverlay();
        using (var stale = fileSystem.OpenWrite("logs\\error.log", false)!)
        {
            stale.Write("old"u8);
        }

        new GameLogger(fileSystem, new FixedTime(), "AlienShooter.exe").Dispose();

        Assert.False(fileSystem.TryRead(DatedPath, out _));
        Assert.StartsWith("----< 2026-01-01", Read(fileSystem, "logs\\error.log"));
    }

    [Fact]
    public void Write_WithoutFiles_WritesNothing()
    {
        var logger = new GameLogger(null, new FixedTime(), "AlienShooter.exe");
        logger.Write("x"u8);

        Assert.False(logger.IsOpen);
    }

    [Fact]
    public void Map_LoadAndEnd_LogTheScriptAndTheDefinitionsLeft()
    {
        var fileSystem = new MemoryWriteOverlay();
        var definitions = TestGameFolder.CreateCatalog();
        var registry = new ScriptRegistry("");
        using (var session = new GameSession(registry, fileSystem, new FixedTime()))
        {
            using var map = EngineTestMap.CreateMap(definitions, session: session);
            var scriptFileSystem = new ScriptOverlay(new GameFolder(TestGameFolder.RequireFolder()), "maps\\log.lgc", "#include \"maps\\export.lgc\"\nmain()\n{\n  Log(\"from the script\");\n}\n");
            Assert.True(map.Script.Load(scriptFileSystem, "maps\\log.lgc"));
            map.Script.CallFunction(map, -1, 0, 0, 0);
        }

        string[] lines = Read(fileSystem, "logs\\error.log").Split("\r\n");
        int error = Array.IndexOf(lines, "!!!ERROR 12:00:00!!!MAP: 0x0 Section can't found (load 'VID')");
        Assert.True(error > 0);
        Assert.Equal(["Player release", "Sprite release", "Menu   release", "Average fps=0", "Script release"], lines[(error - 5)..error]);
        Assert.StartsWith("CurrentTime   =10                sizeof(SPRITE)=112      Map version   =0", lines[error + 1]);
        Assert.Contains(lines, line => line.StartsWith("LoadScript::ByteCode="));
        Assert.Contains("from the script", lines);
        Assert.Contains(lines, line => line.StartsWith("NoVid[  9]="));
        Assert.Equal(["Player release", "Sprite release", "Menu   release"], lines.SkipWhile(line => line != "Player release").Take(3));
    }

    private static string Read(MemoryWriteOverlay fileSystem, string path)
    {
        Assert.True(fileSystem.TryRead(path, out var data));

        return GameTextEncoding.Cyrillic.GetString(data.Span);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
}
}
