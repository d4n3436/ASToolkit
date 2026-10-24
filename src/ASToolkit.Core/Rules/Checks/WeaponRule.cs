using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that a level has about the original's number of weapons and that they lie apart (D7).
/// </summary>
public sealed class WeaponRule : ILevelRule
{
    private const float Spacing = 600;
    private const float NearestSoft = 500;

    /// <inheritdoc/>
    public string Id => "D7";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var weapons = context.Analysis.Elements
            .Where(e => e.Kind is ElementKind.Pickup or ElementKind.Crate && e.Category == ItemCategory.Weapon)
            .ToArray();
        
        int target = context.Target.Weapons;
        yield return new RuleResult("D7.1", RuleSeverity.Hard, weapons.Length <= target + 2, $"{weapons.Length} weapons, ceiling {target + 2}");
        yield return new RuleResult("D7.1", RuleSeverity.Soft, Math.Abs(weapons.Length - target) <= 1, $"{weapons.Length} weapons, target {target}");
        if (weapons.Length < 2)
        {
            yield break;
        }

        int crowded = weapons.Max(w => weapons.Count(o => RuleMath.GetDistance(o.X, o.Y, w.X, w.Y) < Spacing));
        yield return new RuleResult("D7.2", RuleSeverity.Hard, crowded <= 2, $"at most {crowded} weapons within {Spacing} px of each other");
        float nearest = weapons.Min(w => weapons.Where(o => o != w).Min(o => RuleMath.GetDistance(o.X, o.Y, w.X, w.Y)));
        yield return new RuleResult("D7.2", RuleSeverity.Soft, nearest >= NearestSoft, $"closest two weapons {nearest:0} px apart");
    }
}
