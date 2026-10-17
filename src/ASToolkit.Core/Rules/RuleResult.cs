namespace ASToolkit.Core.Rules;

/// <summary>
/// Represents the outcome of one rule for one level.
/// </summary>
/// <param name="Id">The rule ID from the rule set, such as "B1.1".</param>
/// <param name="Severity">The rule severity.</param>
/// <param name="Passed">A value indicating whether the level meets the rule.</param>
/// <param name="Detail">The measured value against the target.</param>
public sealed record RuleResult(string Id, RuleSeverity Severity, bool Passed, string Detail);
