using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks the share of each item category inside crates, and that no dynamite lies in a crate (A8).
/// </summary>
public sealed class CrateLootRule : ILevelRule
{
    private static readonly (ItemCategory Category, float Low, float High)[] Shares =
    [
        (ItemCategory.Ammo, 0.25f, 0.40f), (ItemCategory.Armor, 0.40f, 0.50f), (ItemCategory.Weapon, 0.30f, 0.40f), (ItemCategory.Health, 0.15f, 0.25f)
    ];

    /// <inheritdoc/>
    public string Id => "A8";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var elements = context.Analysis.Elements;
        int dynamiteInCrates = elements.Count(e => e is { Kind: ElementKind.Crate, Category: ItemCategory.Dynamite });
        yield return new RuleResult(Id, RuleSeverity.Hard, dynamiteInCrates == 0, $"dynamite in crates {dynamiteInCrates}");
        foreach ((var category, float low, float high) in Shares)
        {
            int inCrates = elements.Count(e => e.Kind == ElementKind.Crate && e.Category == category);
            int total = inCrates + elements.Count(e => e.Kind == ElementKind.Pickup && e.Category == category);
            if (total >= 4)
            {
                float share = inCrates / (float)total;
                yield return new RuleResult(Id, RuleSeverity.Soft, share >= low && share <= high, $"{category} in crates {share:P0}, target {low:P0}-{high:P0}");
            }
        }
    }
}
