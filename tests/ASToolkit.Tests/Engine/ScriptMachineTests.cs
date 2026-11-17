using ASToolkit.Core.Engine.Scripts;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ScriptMachineTests
{
    private const string ScriptPath = "test.lgc";
    private const int NoteFunction = 70;
    private const int DestroyFunction = 84;

    [Fact]
    public void CallFunction_Expressions_FollowTheGamesOperators()
    {
        var (script, host) = Compile("""
            extern Note(int value) 70;
            int zero;
            main()
            {
              Note(2 + 3 * 4);
              Note(7 / zero);
              Note(-7 % 3);
              Note(1 < 2 == 1);
              Note(1 || 0 && 0);
              Note("ab" + "cd" == "abcd");
              Note("12" + 3);
              Note("0x1F" + 0);
              Note(0x);
              Note(0x1G);
              Note(09);
              Note(4294967295);
            }
            """);

        script.CallFunction(host, -1, 0, 0, 0);

        Assert.Equal([14, 0x0fffffff, -1, 1, 0, 1, 15, 31, 0x24, 1, 0, -1], host.Notes);
    }

    [Fact]
    public void CallFunction_OnceBlock_RunsOnlyTheFirstTime()
    {
        var (script, host) = Compile("""
            extern Note(int value) 70;
            int count;
            main()
            {
              iff(1) { Note(100); }
              count += 1;
              Note(count);
            }
            """);
        byte[] compiled = script.Code.ToArray();

        script.CallFunction(host, -1, 0, 0, 0);
        script.CallFunction(host, -1, 0, 0, 0);

        Assert.Equal([100, 1, 2], host.Notes);
        Assert.Equal((byte)ScriptOpcode.Jump, script.Code[script.Objects[script.MainIndex].Address]);
        Assert.NotEqual(compiled, script.Code.ToArray());
    }

    [Fact]
    public void CallFunction_LoopsAndCalls_BreakAndFillDefaults()
    {
        var (script, host) = Compile("""
            extern Note(int value) 70;
            Add(int a, int b = 10) { return a + b; }
            main()
            {
              int i;
              for (i = 0; i < 10; i++) { if (i == 3) break; Note(i); }
              while (1) { break; }
              do { i--; } while (i > 1);
              Note(i);
              Note(Add(5));
              Note(Add(1, 2));
              return 42;
            }
            """);

        int result = script.CallFunction(host, -1, 0, 0, 0);

        Assert.Equal([0, 1, 2, 1, 15, 3], host.Notes);
        Assert.Equal(42, result);
    }

    [Fact]
    public void CallFunction_DestroyingItsOwnSprite_ReturnsOne()
    {
        var (script, host) = Compile("""
            extern Note(int value) 70;
            extern Destroy(int unit) 84;
            int values[3];
            F001_14(int unit)
            {
              int copy;
              values[1] = 5;
              values[1] += 2;
              Note(values[1]);
              copy = unit;
              Destroy(copy);
            }
            """);
        int handler = script.FindObject("F001_14"u8);

        int result = script.CallFunction(host, handler, 77, 0, 0);

        Assert.Equal(1, result);
        Assert.Equal([7, 77], host.Notes);
        var copy = script.GetSlot(script.Objects[handler].FirstVariable + 1);
        Assert.Equal(77, copy.Value);
        Assert.True(copy.Flags.HasFlag(StackObjectFlags.Sprite));
    }

    [Fact]
    public void ReadState_WrittenState_RestoresTheInitialValues()
    {
        var (script, _) = Compile("""
            string name = "abc";
            int money = 50;
            main() { }
            """);
        var state = new MemoryStream();
        script.WriteState(state);
        int name = script.Objects[script.FindObject("name"u8)].Address;
        int money = script.Objects[script.FindObject("money"u8)].Address;
        script.GetSlot(name).Text = "changed"u8.ToArray();
        script.GetSlot(money).Value = 0;

        int read = script.ReadState(state.ToArray());

        Assert.Equal(state.Length, read);
        Assert.Equal("abc"u8.ToArray(), script.GetSlot(name).Text.ToArray());
        Assert.Equal(50, script.GetSlot(money).Value);
    }

    private static (Script Script, NoteHost Host) Compile(string text)
    {
        var script = new Script();
        Assert.True(script.Load(new ScriptOverlay(new ScriptFolder(AppContext.BaseDirectory), ScriptPath, text), ScriptPath));

        return (script, new NoteHost());
    }

    private sealed class NoteHost : IScriptFunctionHost
    {
        public List<int> Notes { get; } = [];

        public void ExecFunc(Script script, int function)
        {
            switch (function)
            {
                case NoteFunction:
                case DestroyFunction:
                    Notes.Add(script.PopNumber());
                    script.PushNumber(0);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected function {function}.");
            }
        }
    }
}
