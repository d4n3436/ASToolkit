using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the dynamite sites come late in the tour (C2).
/// </summary>
public sealed class DynamiteTimingRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "C2";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Target.DynamiteSites == 0)
        {
            yield break;
        }

        float[] sites = context.Analysis.Objectives
            .Where(o => o.Kind == ObjectiveKind.DynamiteSite)
            .Select(o => o.Progress)
            .Order()
            .ToArray();
        
        if (sites.Length > 0)
        {
            float median = RuleMath.GetQuantile(sites, 0.5f);
            yield return new RuleResult(Id, RuleSeverity.Hard, sites[0] >= 0.38f && median >= 0.6f, $"first site {sites[0]:0.00}, median {median:0.00}");
        }
    }
}
