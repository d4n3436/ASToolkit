using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks the fences and the generator that drops them on a level whose original is fenced (B4).
/// </summary>
public sealed class FenceGateRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "B4";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var t = context.Target;
        if (t.Style != LevelStyle.Fenced)
        {
            yield break;
        }

        var a = context.Analysis;
        yield return new RuleResult("B4.1", RuleSeverity.Hard, Math.Abs((a.Gating.FencedOffShare ?? 0) - t.FencedOffShare!.Value) <= 0.1,
            $"fenced off {a.Gating.FencedOffShare:P0}, target {t.FencedOffShare:P0}");
        var generator = a.Objectives.FirstOrDefault(o => o.Kind == ObjectiveKind.Generator);
        yield return new RuleResult("B4.2", RuleSeverity.Hard, generator?.Progress is >= 0.08 and <= 0.3, $"first generator at {generator?.Progress:0.00}");
        int? keyToGate = a.Gating.KeyToGatePixels;
        yield return new RuleResult("B4.3", RuleSeverity.Hard, keyToGate >= 800, $"generator to fence {keyToGate}");
    }
}
