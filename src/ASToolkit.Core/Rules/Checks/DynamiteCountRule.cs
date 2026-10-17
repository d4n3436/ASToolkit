using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that a level has the original's number of dynamite sites and one charge per site (C1).
/// </summary>
public sealed class DynamiteCountRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "C1";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var elements = context.Analysis.Elements;
        int sites = elements.Count(e => e.Kind == ElementKind.DynamiteSite);
        int charges = elements.Count(e => e is { Kind: ElementKind.Pickup, Category: ItemCategory.Dynamite });
        int target = context.Target.DynamiteSites;
        yield return target == 0
            ? new RuleResult(Id, RuleSeverity.Hard, sites == 0, $"{sites} sites, original has none")
            : new RuleResult(Id, RuleSeverity.Hard, sites == target && charges == sites, $"{sites} sites, {charges} charges, target {target}");
    }
}
