using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that a level whose original has no gate gets a mild one (A12).
/// </summary>
public sealed class MildGateRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A12";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Target.Style != LevelStyle.Ungated)
        {
            yield break;
        }

        var a = context.Analysis;
        var style = context.Measurement.Style;
        var key = a.Objectives.FirstOrDefault(o => o.Kind is ObjectiveKind.Breaker or ObjectiveKind.Generator);
        double share = a.Gating.LockedShare ?? a.Gating.FencedOffShare ?? 0;
        yield return new RuleResult(Id, RuleSeverity.Hard, style != LevelStyle.Ungated && share is >= 0.15 and <= 0.35 && key is not null && key.Progress <= 0.4,
            $"style {style}, gated {share:P0}, key at {key?.Progress:0.00}");
        if (style == LevelStyle.LockedDoors)
        {
            int? keyToGate = a.Gating.KeyToGatePixels;
            yield return new RuleResult(Id, RuleSeverity.Hard, keyToGate <= 700, $"breaker to next locked door {keyToGate}");
        }
    }
}
