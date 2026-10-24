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
        float[] triggers = context.Analysis.Elements
            .Where(e => e is { Kind: ElementKind.Spawner, TriggerProgress: not null })
            .Select(e => e.TriggerProgress!.Value)
            .Order()
            .ToArray();
        
        if (triggers.Length == 0)
        {
            yield break;
        }

        float late = triggers.Count(p => p > 0.75f) / (float)triggers.Length;
        float low = RuleMath.GetQuantile(triggers, 0.25f);
        float high = RuleMath.GetQuantile(triggers, 0.75f);
        yield return new RuleResult(Id, RuleSeverity.Soft, low >= 0.0f && high <= 0.5f && late <= 0.1f, $"middle half {low:0.00}-{high:0.00}, past 0.75 {late:P0}");
    }
}
