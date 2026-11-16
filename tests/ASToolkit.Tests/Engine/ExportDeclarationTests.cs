using System.Text;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ExportDeclarationTests
{
    private const string OldPrefix = "Old";

    [Fact]
    public void ScriptFunction_EveryMember_MatchesAnExternOfExport()
    {
        const string ScriptPath = "Maps\\export_test.lgc";
        var script = new Script();
        var files = new ScriptOverlay(new ScriptFolder(TestGameFolder.RequireFolder()), ScriptPath, "#include \"maps\\export.lgc\"\nmain()\n{\n}\n");
        Assert.True(script.Load(files, ScriptPath));
        var externs = new Dictionary<int, string>();
        foreach (var name in script.Objects)
        {
            if (name.Kind == ScriptObjectKind.Extern)
            {
                externs[name.Address] = Encoding.ASCII.GetString(name.Name.Span);
            }
        }

        foreach (var function in Enum.GetValues<ScriptFunction>())
        {
            string name = function.ToString();
            if (name.StartsWith(OldPrefix, StringComparison.Ordinal))
            {
                Assert.DoesNotContain((int)function, externs.Keys);
                continue;
            }

            Assert.True(externs.TryGetValue((int)function, out string? declared), $"Missing extern {(int)function}.");
            Assert.Equal(declared, name, ignoreCase: true);
        }
    }

    [Fact]
    public void VidDataType_Values_MatchTheVidDefinesOfExport()
    {
        const string Prefix = "#define VID_";
        var defined = new SortedSet<int>();
        foreach (string line in ReadExport())
        {
            string text = line.Trim();
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            string[] parts = text.Split((char[])[' ', '\t', '/'], StringSplitOptions.RemoveEmptyEntries);
            defined.Add(int.Parse(parts[2]));
        }

        Assert.Equal(defined, Enum.GetValues<VidDataType>().Select(type => (int)type).Order());
    }

    private static string[] ReadExport() =>
        File.ReadAllLines(Path.Combine(TestGameFolder.RequireFolder(), "Maps", "EXPORT.LGC"), GameTextEncoding.Cyrillic);
}
