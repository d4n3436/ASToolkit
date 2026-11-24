using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class AlienShooter2CompilerTests
{
    private const string ScriptPath = "test.lgc";

    [Fact]
    public void Load_LogicalOperators_BindAndBeforeOrAndShortCircuit()
    {
        var script = Compile("int a; int b; int c;\nmain()\n{\n  a || b && c;\n}\n");
        var code = Decode(script, "main");

        Assert.Equal([ScriptOpcode.Load, ScriptOpcode.OrJump, ScriptOpcode.Load, ScriptOpcode.AndJump, ScriptOpcode.Load, ScriptOpcode.LogicalAnd,
            ScriptOpcode.LogicalOr, ScriptOpcode.EndStatement], code.Take(8).Select(instruction => instruction.Opcode));
        Assert.Equal(code[6].Offset, code[3].Target);
        Assert.Equal(code[7].Offset, code[1].Target);
    }

    [Fact]
    public void Load_LocalInitializer_AssignsAtRunTime()
    {
        const string text = "int g;\nmain()\n{\n  int x = g + 1;\n}\n";
        var script = Compile(text);
        var code = Decode(script, "main");

        Assert.Equal([ScriptOpcode.Load, ScriptOpcode.PushNumber, ScriptOpcode.Add, ScriptOpcode.Assign], code.Take(4).Select(instruction => instruction.Opcode));
        Assert.Throws<InvalidDataException>(() => new Script().Load(CreateSource(text), ScriptPath));
    }

    [Fact]
    public void Load_Prototype_CallsTheLaterDefinitionBySymbolIndex()
    {
        var script = Compile("f(int a);\nmain()\n{\n  f(2);\n}\nf(int a)\n{\n}\n");
        var call = Assert.Single(Decode(script, "main"), instruction => instruction.Opcode == ScriptOpcode.Call);
        int function = script.FindCalledFunction(call.Operand);

        Assert.Equal(script.Objects.Location("f"u8), function);
        Assert.True(script.Objects[function].Address > script.Objects[script.MainIndex].Address);
        Assert.Equal(1, script.Objects[function].Count);
    }

    [Fact]
    public void Load_CastOfNegation_ConvertsAfterNegating()
    {
        var code = Decode(Compile("int g;\nmain()\n{\n  (int)-g;\n}\n"), "main");

        Assert.Equal([ScriptOpcode.Load, ScriptOpcode.Negate, ScriptOpcode.CastInt], code.Take(3).Select(instruction => instruction.Opcode));
    }

    [Fact]
    public void Load_AddedTextLiterals_FoldIntoOneLiteral()
    {
        var script = Compile("string s;\nmain()\n{\n  s = \"ab\" + \"cd\";\n}\n");
        var push = Decode(script, "main")[0];
        var main = script.Objects[script.MainIndex];

        Assert.Equal(ScriptOpcode.PushText, push.Opcode);
        Assert.Equal("abcd", Encoding.ASCII.GetString(script.Code.Slice(main.Address + 1, push.Length - 2)));
    }

    [Fact]
    public void Load_CompileError_LeavesTheScriptEmptyWithTheError()
    {
        var script = new Script(ScriptVersion.AlienShooter2);

        Assert.True(script.Load(CreateSource("!!!!not used any more\nmain()\n{\n}\n"), ScriptPath));
        Assert.True(script.Code.IsEmpty);
        Assert.Equal(-1, script.MainIndex);
        Assert.NotNull(script.CompileError);
    }

    [Fact]
    public void CallFunction_AlienShooter2Script_IsRefused()
    {
        var script = Compile("main()\n{\n}\n");

        Assert.Throws<NotSupportedException>(() => script.CallFunction(null!, -1, 0, 0, 0));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Load_LevelScripts_CompileToCode()
    {
        string game = TestGameFolder.RequireFolder();
        if (!ObjectTable.Load(Path.Combine(game, "objects.res")).IsAlienShooter2)
        {
            Assert.Skip("The test game is not Alien Shooter 2.");
        }

        var fileSystem = new GameFolder(game);

        string[] levels = Directory.GetFiles(Path.Combine(game, "Maps"), "*.lgc")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.StartsWith("level_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("survive_0", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(levels);
        foreach (string level in levels)
        {
            var script = new Script(ScriptVersion.AlienShooter2);
            Assert.True(script.Load(fileSystem, "maps\\" + level));
            Assert.Null(script.CompileError);
            Assert.False(script.Code.IsEmpty, level);
        }
    }

    private static MemoryWriteOverlay CreateSource(string text)
    {
        var fileSystem = new MemoryWriteOverlay(new GameFolder(AppContext.BaseDirectory));
        fileSystem.Set(ScriptPath, Encoding.ASCII.GetBytes(text));

        return fileSystem;
    }

    private static Script Compile(string text)
    {
        var script = new Script(ScriptVersion.AlienShooter2);
        Assert.True(script.Load(CreateSource(text), ScriptPath));

        return script;
    }

    private static ScriptInstruction[] Decode(Script script, string function)
    {
        int index = script.Objects.Location(Encoding.ASCII.GetBytes(function));
        var instructions = new List<ScriptInstruction>();
        for (int offset = script.Objects[index].Address; offset < script.GetFunctionEnd(index);)
        {
            var instruction = ScriptInstruction.Read(script.Code, offset, ScriptVersion.AlienShooter2);
            instructions.Add(instruction);
            offset = instruction.End;
        }

        return instructions.ToArray();
    }
}
