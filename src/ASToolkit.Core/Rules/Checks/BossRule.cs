using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the boss of a level whose original has one comes late in the tour (D3).
/// </summary>
public sealed class BossRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "D3";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (!context.Target.HasBoss)
        {
            yield break;
        }

        var boss = context.Analysis.Objectives.FirstOrDefault(o => o.Kind == ObjectiveKind.Boss);
        yield return new RuleResult(Id, RuleSeverity.Soft, boss is not null && boss.Progress >= 0.55f, $"boss at {boss?.Progress:0.00}");
    }
}
