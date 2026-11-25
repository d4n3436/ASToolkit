using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class HighSpaceTests
{
    private const string ScriptPath = "test.lgc";
    private const byte HighSpace = 0xe9;
    private const byte HighLetter = 0xcb;
    private const byte ElLetter = 0xeb;
    private const byte EnLetter = 0xed;

    [Fact]
    public void Load_HighSpaceBetweenTokens_SkipsItAsBlank()
    {
        var script = new Script();

        Assert.True(script.Load(CreateSource(HighSpace), ScriptPath));
        Assert.True(script.Objects.Location("b"u8) >= 0);
    }

    [Theory]
    [InlineData(ElLetter)]
    [InlineData(EnLetter)]
    public void Load_LetterBetweenTokens_IsNotSkipped(byte letter)
    {
        var script = new Script();

        Assert.Throws<InvalidDataException>(() => script.Load(CreateSource(letter), ScriptPath));
    }

    [Theory]
    [InlineData(HighLetter)]
    [InlineData(ElLetter)]
    public void Load_HighLetterInName_ContinuesTheName(byte letter)
    {
        var script = new Script();

        var fileSystem = new MemoryWriteOverlay(new GameFolder(AppContext.BaseDirectory));
        fileSystem.Set(ScriptPath, Join("int a"u8, letter, ";\nmain() { }"u8));
        Assert.True(script.Load(fileSystem, ScriptPath));
        Assert.True(script.Objects.Location([(byte)'a', letter]) >= 0);
    }

    [Fact]
    public void Load_HighDigitInEscape_ReadsAThreeDigitCode()
    {
        var script = new Script();

        var fileSystem = new MemoryWriteOverlay(new GameFolder(AppContext.BaseDirectory));
        fileSystem.Set(ScriptPath, Join("string s = \"\\1"u8, ElLetter, "3\";\nmain() { }"u8));
        Assert.True(script.Load(fileSystem, ScriptPath));
        Assert.Equal([0x1b], script.GetSlot(script.Objects[script.Objects.Location("s"u8)].Address).Text.ToArray());
    }

    [Fact]
    public void Load_HighSpaceInAlienShooter2_IsNotSkipped()
    {
        var script = new Script(ScriptVersion.AlienShooter2);

        script.Load(CreateSource(HighSpace), ScriptPath);

        Assert.NotNull(script.CompileError);
    }

    [Theory]
    [InlineData(HighSpace, 3)]
    [InlineData(ElLetter, 2)]
    [InlineData(EnLetter, 2)]
    public void Action_NextCommand_SkipsOnlyHighSpaces(byte character, int visibleCount)
    {
        var map = EngineTestMap.CreateMap();
        var source = new MemoryWriteOverlay(new GameFolder(TestGameFolder.RequireFolder()));
        source.Set(ScriptPath, "#include \"maps\\export.lgc\"\nmain() { MenuLoad(\"maps\\\\gamemenu.men\"); }"u8.ToArray());
        Assert.True(map.Script.Load(source, ScriptPath));
        map.Script.CallFunction(map, -1, 0, 0, 0);
        var text = Enumerable.Range(0, 17).SelectMany(map.GetLayer).OfType<SText>().First();
        text.Shown = new byte[] { (byte)'a', character, (byte)'b', (byte)'c' };
        text.VisibleCount = 0;
        text.Timer = 0;

        Map.Action(text, ActionCode.NextCommand);
        Map.Action(text, ActionCode.NextCommand);

        Assert.Equal(visibleCount, text.VisibleCount);
    }

    private static MemoryWriteOverlay CreateSource(byte separator)
    {
        var fileSystem = new MemoryWriteOverlay(new GameFolder(AppContext.BaseDirectory));
        fileSystem.Set(ScriptPath, Join("int a;"u8, separator, "int b;\nmain() { }"u8));

        return fileSystem;
    }

    private static byte[] Join(ReadOnlySpan<byte> before, byte middle, ReadOnlySpan<byte> after)
    {
        byte[] bytes = new byte[before.Length + 1 + after.Length];
        before.CopyTo(bytes);
        bytes[before.Length] = middle;
        after.CopyTo(bytes.AsSpan(before.Length + 1));

        return bytes;
    }
}
