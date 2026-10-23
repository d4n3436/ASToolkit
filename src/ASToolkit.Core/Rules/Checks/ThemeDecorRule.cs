using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that a level has only decor the campaign uses on levels of its theme, at the heights it uses it (A13).
/// </summary>
public sealed class ThemeDecorRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A13";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        string[] foreign = context.Map.Sprites
            .Where(s => s.Id.IsDecor && !context.Target.ThemeDecor.Allows(s.Id, s.Position.Z))
            .Select(s => $"{s.Id} at z {s.Position.Z:0}")
            .Distinct()
            .Order()
            .ToArray();
        
        yield return new RuleResult(Id, RuleSeverity.Hard, foreign.Length == 0,
            foreign.Length == 0 ? "decor as the campaign uses it on this theme" : $"decor the campaign never uses this way on this theme: {string.Join(", ", foreign.Take(8))}");
    }
}
