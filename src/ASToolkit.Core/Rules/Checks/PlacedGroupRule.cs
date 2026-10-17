using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the placed monsters sit about as far along the tour as in the original (A10).
/// </summary>
public sealed class PlacedGroupRule : ILevelRule
{
    private const int MinMonsters = 4;
    private const double Tolerance = 0.25;

    /// <inheritdoc/>
    public string Id => "A10";

    /// <summary>
    /// Returns the median tour progress of a level's placed monsters, or null when it has fewer than four.
    /// </summary>
    public static double? GetMedianProgress(RouteAnalysis analysis)
    {
        double[] placed = analysis.Elements
            .Where(e => e.Kind == ElementKind.Monster)
            .Select(e => e.Progress)
            .Order()
            .ToArray();
        
        return placed.Length < MinMonsters ? null : RuleMath.GetQuantile(placed, .5);
    }

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        double? target = context.Target.PlacedMonsterProgress;
        double? median = GetMedianProgress(context.Analysis);
        if (target is null || median is null)
        {
            yield break;
        }

        yield return new RuleResult(Id, RuleSeverity.Soft, Math.Abs(median.Value - target.Value) <= Tolerance, $"placed monsters median at {median:0.00}, target {target:0.00}");
    }
}
