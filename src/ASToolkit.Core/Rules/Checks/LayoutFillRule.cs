namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the floor fills its bounding box about as much as the original's does (A15).
/// </summary>
public sealed class LayoutFillRule : ILevelRule
{
    private const double MinFill = 0.25;
    private const double Tolerance = 0.08;

    /// <inheritdoc/>
    public string Id => "A15";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var model = context.Model;
        if (model is null)
        {
            yield break;
        }

        double fill = model.GetBoxFill();
        double target = context.Target.BoxFill;
        yield return new RuleResult(Id, RuleSeverity.Hard, fill >= MinFill, $"floor fills {fill:P0} of its box, floor {MinFill:P0}");
        yield return new RuleResult(Id, RuleSeverity.Soft, fill >= target - Tolerance, $"floor fills {fill:P0} of its box, target {target:P0}");
    }
}
