using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that a fenced level has the original's number of generators and no ambush right at them unless the original has one (D2).
/// </summary>
public sealed class GeneratorRule : ILevelRule
{
    private const double AmbushReach = 300;

    /// <inheritdoc/>
    public string Id => "D2";

    /// <summary>
    /// Returns whether an ambush of a level triggers right at one of its generators.
    /// </summary>
    public static bool HasAmbushAtGenerator(RouteAnalysis analysis)
    {
        var generators = analysis.Elements.Where(e => e.Kind == ElementKind.Generator).ToArray();
        return analysis.Elements.Any(e => e is { Kind: ElementKind.Spawner, Program.Trigger: not null }
                                          && generators.Any(g => RuleMath.GetDistance(g.X, g.Y, e.Program.Trigger.Value.X, e.Program.Trigger.Value.Y) <= AmbushReach));
    }

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var t = context.Target;
        if (t.Style != LevelStyle.Fenced)
        {
            yield break;
        }

        int generators = context.Analysis.Elements.Count(e => e.Kind == ElementKind.Generator);
        yield return new RuleResult(Id, RuleSeverity.Soft, generators == t.Generators, $"{generators} generators, target {t.Generators}");
        if (!t.HasGeneratorAmbush)
        {
            bool ambush = HasAmbushAtGenerator(context.Analysis);
            yield return new RuleResult(Id, RuleSeverity.Soft, !ambush, ambush ? "an ambush triggers at a generator" : "no ambush at the generators");
        }
    }
}
