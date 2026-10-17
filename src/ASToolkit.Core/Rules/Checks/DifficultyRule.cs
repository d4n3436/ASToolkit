namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the total monster hit points are within 15% of the target (A11).
/// </summary>
public sealed class DifficultyRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A11";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        int hp = context.Measurement.HitPoints;
        int target = context.Target.HitPoints;
        yield return new RuleResult(Id, RuleSeverity.Soft, RuleMath.IsWithin(hp, target, 0.15), $"HP {hp}, target {target}");
    }
}
