using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks the share of each item category inside crates, and that no dynamite lies in a crate (A8).
/// </summary>
public sealed class CrateLootRule : ILevelRule
{
    private static readonly (ItemCategory Category, double Low, double High)[] Shares =
    [
        (ItemCategory.Ammo, .25, .40), (ItemCategory.Armor, .40, .50), (ItemCategory.Weapon, .30, .40), (ItemCategory.Health, .15, .25)
    ];

    /// <inheritdoc/>
    public string Id => "A8";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var elements = context.Analysis.Elements;
        int dynamiteInCrates = elements.Count(e => e is { Kind: ElementKind.Crate, Category: ItemCategory.Dynamite });
        yield return new RuleResult(Id, RuleSeverity.Hard, dynamiteInCrates == 0, $"dynamite in crates {dynamiteInCrates}");
        foreach ((var category, double low, double high) in Shares)
        {
            int inCrates = elements.Count(e => e.Kind == ElementKind.Crate && e.Category == category);
            int total = inCrates + elements.Count(e => e.Kind == ElementKind.Pickup && e.Category == category);
            if (total >= 4)
            {
                double share = inCrates / (double)total;
                yield return new RuleResult(Id, RuleSeverity.Soft, share >= low && share <= high, $"{category} in crates {share:P0}, target {low:P0}-{high:P0}");
            }
        }
    }
}
