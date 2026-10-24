namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the floor fills its bounding box about as much as the original's does (A15).
/// </summary>
public sealed class LayoutFillRule : ILevelRule
{
    private const float MinFill = 0.25f;
    private const float Tolerance = 0.08f;

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

        float fill = model.GetBoxFill();
        float target = context.Target.BoxFill;
        yield return new RuleResult(Id, RuleSeverity.Hard, fill >= MinFill, $"floor fills {fill:P0} of its box, floor {MinFill:P0}");
        yield return new RuleResult(Id, RuleSeverity.Soft, fill >= target - Tolerance, $"floor fills {fill:P0} of its box, target {target:P0}");
    }
}
