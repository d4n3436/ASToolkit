namespace ASToolkit.Core.Rules;

/// <summary>
/// Defines one rule of the rule set in docs/RULES.md.
/// </summary>
public interface ILevelRule
{
    /// <summary>
    /// Gets the rule ID from the rule set, such as "A1".
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Evaluates a level and returns one result per check that applies to it.
    /// </summary>
    IEnumerable<RuleResult> Evaluate(RuleContext context);
}
