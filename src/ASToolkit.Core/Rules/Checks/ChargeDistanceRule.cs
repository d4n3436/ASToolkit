using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that every charge lies 250 to 3,200 px from the nearest dynamite site (C4).
/// </summary>
public sealed class ChargeDistanceRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "C4";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Target.DynamiteSites == 0)
        {
            yield break;
        }

        var elements = context.Analysis.Elements;
        var sites = elements.Where(e => e.Kind == ElementKind.DynamiteSite).ToArray();
        foreach (var charge in elements.Where(e => e is { Kind: ElementKind.Pickup, Category: ItemCategory.Dynamite }))
        {
            float nearest = sites.Length == 0 ? 0 : sites.Min(s => RuleMath.GetDistance(s.X, s.Y, charge.X, charge.Y));
            yield return new RuleResult(Id, RuleSeverity.Hard, nearest is >= 250 and <= 3200, $"charge {nearest:0} px from the nearest site");
        }
    }
}
