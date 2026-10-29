using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation;
using ASToolkit.Core.Rules;
using Xunit;

namespace ASToolkit.Tests.Generation;

[Trait("Category", "Integration")]
public class LevelGeneratorIntegrationTests
{
    private static readonly string[] ArmySources = ["Level_02.map", "Level_04.map", "Level_07.map", "ADDON/Level_02.map", "ADDON2/Level_04.map"];

    [Fact]
    public void Generate_EveryCampaignLevel_BreaksNoHardRule()
    {
        var generator = CreateGenerator();
        var broken = new List<string>();
        var truncated = new List<string>();
        foreach (int level in ReferenceLevelSet.CampaignLevels)
        {
            var generated = generator.Generate(11, level);
            broken.AddRange(generated.Results.Where(r => r is { Severity: RuleSeverity.Hard, Passed: false }).Select(r => $"L{level} {r.Id}: {r.Detail}"));
            truncated.AddRange(generated.Map.Sprites.Where(s => s.Payload.Length < 8).Select(s => $"L{level} {s.Id} at ({s.Position.X:0}, {s.Position.Y:0}): {s.Payload.Length} bytes"));
        }

        Assert.Empty(broken);
        Assert.Empty(truncated);
    }

    [Fact]
    public void Generate_SameSeedTwice_WritesTheSameFiles()
    {
        var generator = CreateGenerator();
        string folder = Directory.CreateTempSubdirectory().FullName;
        string first = Path.Combine(folder, "a.map");
        string second = Path.Combine(folder, "b.map");
        generator.Generate(3, 6).Write(first, generator.GetTemplatePath(6));
        generator.Generate(3, 6).Write(second, generator.GetTemplatePath(6));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(first, ".lgc")), File.ReadAllBytes(Path.ChangeExtension(second, ".lgc")));
    }

    private static LevelGenerator CreateGenerator()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        return new LevelGenerator(game, objects, ReferenceLevelSet.Load(game, objects), ArmyTable.Load(game, ArmySources));
    }
}
