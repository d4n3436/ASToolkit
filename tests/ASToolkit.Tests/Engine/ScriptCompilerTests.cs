using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ScriptCompilerTests
{
    private const StackObjectFlags CompiledFlags = StackObjectFlags.Text | StackObjectFlags.Number | StackObjectFlags.ArrayElement |
        StackObjectFlags.Initialized | StackObjectFlags.NotInitialized;

    [Fact]
    public void Load_RecordedLevelScript_MatchesTheScriptStateOfTheDemo()
    {
        var source = new ScriptFolder(TestGameFolder.RequireFolder());
        var demo = new DemoResource(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "addon2-level1-walk.dem")));
        string path = Path.ChangeExtension(GameTextEncoding.Cyrillic.GetString(demo.MapName), ".lgc");
        var compiled = new Script();
        var recorded = new Script();
        Assert.True(compiled.Load(source, path));
        Assert.True(recorded.Load(source, path));

        int offset = recorded.ReadState(demo.Body);
        while (demo.TryReadFrame(ref offset, out _))
        {
        }

        Assert.Equal(demo.Body.Length, offset);
        Assert.Equal(compiled.VariableCount, recorded.VariableCount);
        for (int index = 0; index < compiled.VariableCount; index++)
        {
            var expected = recorded.GetSlot(index);
            var actual = compiled.GetSlot(index);
            Assert.Equal(expected.Flags & CompiledFlags, actual.Flags & CompiledFlags);
            if ((expected.Flags & StackObjectFlags.Initialized) != 0)
            {
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(expected.Text.ToArray(), actual.Text.ToArray());
            }
        }

        Assert.Equal(recorded.Objects.Length, compiled.Objects.Length);
        for (int index = 0; index < compiled.Objects.Length; index++)
        {
            var expected = recorded.Objects[index];
            var actual = compiled.Objects[index];
            Assert.Equal(expected.Name.ToArray(), actual.Name.ToArray());
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Address, actual.Address);
            Assert.Equal(expected.Count, actual.Count);
            if (actual.Kind != ScriptObjectKind.Variable)
            {
                Assert.Equal(expected.FirstVariable, actual.FirstVariable);
            }
        }
    }
}
