using System.Text;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rules;
using Xunit;

namespace ASToolkit.Tests.Rules;

public class RuleSetTests
{
    [Fact]
    public void Evaluate_CampaignOriginals_PassEveryHardRuleButTheGateRequirement()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var references = ReferenceLevelSet.Load(game, objects);
        var failures = new StringBuilder();
        foreach (int level in ReferenceLevelSet.CampaignLevels)
        {
            string name = ReferenceLevelSet.GetCampaignName(level);
            var (analysis, _) = references.GetLevel(name);
            var map = MapResourceBuilder.Load(Path.Combine(game, "Maps", name));
            var script = LevelScriptReader.Read(Path.Combine(game, "Maps", Path.ChangeExtension(name, ".lgc")));
            foreach (var result in RuleSet.Default.Evaluate(RuleContext.Create(map, script, analysis, objects, references.GetTarget(level))))
            {
                if (result is { Severity: RuleSeverity.Hard, Passed: false } && result.Id != "A12")
                {
                    failures.AppendLine($"{name} {result.Id}: {result.Detail}");
                }
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
    }

    [Fact]
    public void Evaluate_CampaignOriginals_KeepEveryComputerOnATable()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var references = ReferenceLevelSet.Load(game, objects);
        var failures = new StringBuilder();
        foreach (int level in ReferenceLevelSet.CampaignLevels)
        {
            string name = ReferenceLevelSet.GetCampaignName(level);
            var (analysis, _) = references.GetLevel(name);
            var map = MapResourceBuilder.Load(Path.Combine(game, "Maps", name));
            var script = LevelScriptReader.Read(Path.Combine(game, "Maps", Path.ChangeExtension(name, ".lgc")));
            foreach (var result in RuleSet.Default.Evaluate(RuleContext.Create(map, script, analysis, objects, references.GetTarget(level)))
                .Where(r => r.Id == "A18"))
            {
                if (!result.Passed)
                {
                    failures.AppendLine($"{name}: {result.Detail}");
                }
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
    }
}
