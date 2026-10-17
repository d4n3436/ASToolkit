using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that ambushes trigger early in the tour (A6).
/// </summary>
public sealed class AmbushTimingRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A6";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        double[] triggers = context.Analysis.Elements
            .Where(e => e is { Kind: ElementKind.Spawner, TriggerProgress: not null })
            .Select(e => e.TriggerProgress!.Value)
            .Order()
            .ToArray();
        
        if (triggers.Length == 0)
        {
            yield break;
        }

        double late = triggers.Count(p => p > 0.75) / (double)triggers.Length;
        double low = RuleMath.GetQuantile(triggers, .25);
        double high = RuleMath.GetQuantile(triggers, .75);
        yield return new RuleResult(Id, RuleSeverity.Soft, low >= 0.0 && high <= 0.5 && late <= 0.1, $"middle half {low:0.00}-{high:0.00}, past 0.75 {late:P0}");
    }
}
