namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the tour is 0.7 to 1.3 times the reference length (A3).
/// </summary>
public sealed class TourLengthRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A3";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        int tour = context.Analysis.RoutePixels;
        int reference = context.Target.ReferenceTourPixels;
        yield return new RuleResult(Id, RuleSeverity.Soft, tour >= 0.7 * reference && tour <= 1.3 * reference, $"tour {tour}, reference {reference}");
    }
}
