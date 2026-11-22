using ASToolkit.Core.Engine.IO;
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
        var fileSystem = new GameFolder(TestGameFolder.RequireFolder());
        var demo = new DemoResource(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "addon2-level1-walk.dem")));
        string path = Path.ChangeExtension(GameTextEncoding.Cyrillic.GetString(demo.MapName), ".lgc");
        var compiled = new Script();
        var recorded = new Script();
        Assert.True(compiled.Load(fileSystem, path));
        Assert.True(recorded.Load(fileSystem, path));

        int offset = recorded.ReadState(demo.Body);
        while (demo.TryReadFrame(ref offset, out _))
        {
        }

        Assert.Equal(demo.Body.Length, offset);
        Assert.Equal(compiled.VariableCount, recorded.VariableCount);
        for (int i = 0; i < compiled.VariableCount; i++)
        {
            var expected = recorded.GetSlot(i);
            var actual = compiled.GetSlot(i);
            Assert.Equal(expected.Flags & CompiledFlags, actual.Flags & CompiledFlags);
            if (expected.Flags.HasFlag(StackObjectFlags.Initialized))
            {
                Assert.Equal(expected.Value, actual.Value);
                Assert.Equal(expected.Text.ToArray(), actual.Text.ToArray());
            }
        }

        Assert.Equal(recorded.Objects.Length, compiled.Objects.Length);
        for (int i = 0; i < compiled.Objects.Length; i++)
        {
            var expected = recorded.Objects[i];
            var actual = compiled.Objects[i];
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
