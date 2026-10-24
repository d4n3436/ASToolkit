using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks the number, spread and contents of the secret closets and the number of breakable shortcuts on a lab level (D4).
/// </summary>
public sealed class SecretClosetRule : ILevelRule
{
    private const float MaxWeaponShare = 0.5f;

    /// <inheritdoc/>
    public string Id => "D4";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Analysis.Theme != LevelTheme.Lab)
        {
            yield break;
        }

        var m = context.Measurement;
        var closets = m.SecretClosets;
        int target = context.Target.SecretClosets;
        yield return new RuleResult("D4.1", RuleSeverity.Soft, Math.Abs(closets.Count - target) <= 2, $"{closets.Count} secret closets, target {target}");
        if (closets.Count >= 3)
        {
            float first = closets.Min(c => c.Progress);
            float last = closets.Max(c => c.Progress);
            yield return new RuleResult("D4.2", RuleSeverity.Soft, first <= 0.3f && last >= 0.7f, $"closets from {first:0.00} to {last:0.00}");
        }

        if (closets.Count > 0)
        {
            int withWeapon = closets.Count(c => c.Items.Any(i => ItemCategorizer.GetCategory(i) == ItemCategory.Weapon));
            yield return new RuleResult("D4.3", RuleSeverity.Soft, withWeapon <= MaxWeaponShare * closets.Count, $"{withWeapon} of {closets.Count} closets hold a weapon");
        }

        yield return new RuleResult("D4.4", RuleSeverity.Soft, m.Shortcuts <= 2, $"{m.Shortcuts} breakable shortcuts");
    }
}
