namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the tour walks back over covered ground no more than the original allows (A4).
/// </summary>
public sealed class RevisitRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A4";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        float revisit = context.Analysis.RevisitShare;
        float ceiling = context.Target.RevisitCeiling;
        yield return new RuleResult(Id, RuleSeverity.Hard, revisit <= ceiling, $"revisit {revisit:P1}, ceiling {ceiling:P1}");
    }
}
