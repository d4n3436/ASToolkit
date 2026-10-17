using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the last dynamite site is the tour's last objective (C5).
/// </summary>
public sealed class FinalSiteRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "C5";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Target.DynamiteSites == 0)
        {
            yield break;
        }

        var last = context.Analysis.Objectives.LastOrDefault(o => o.Kind != ObjectiveKind.Blackout);
        yield return new RuleResult(Id, RuleSeverity.Soft, last?.Kind == ObjectiveKind.DynamiteSite, $"last objective {last?.Kind}");
    }
}
