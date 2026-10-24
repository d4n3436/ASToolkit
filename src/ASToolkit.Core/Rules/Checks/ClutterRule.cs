namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that solid objects block the original's share of the floor and that the level has the original's number of breakables (A1).
/// </summary>
public sealed class ClutterRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A1";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var m = context.Measurement;
        var t = context.Target;
        yield return new RuleResult(Id, RuleSeverity.Hard, m.BlockedShare >= 0.6f * t.BlockedShare, $"blocked {m.BlockedShare:P1}, floor {0.6 * t.BlockedShare:P1}");
        yield return new RuleResult(Id, RuleSeverity.Soft, RuleMath.IsWithin(m.BlockedShare, t.BlockedShare, 0.3f), $"blocked {m.BlockedShare:P1}, target {t.BlockedShare:P1}");
        yield return new RuleResult(Id, RuleSeverity.Soft, RuleMath.IsWithin(m.Breakables, t.Breakables, 0.3f), $"breakables {m.Breakables}, target {t.Breakables}");
    }
}
