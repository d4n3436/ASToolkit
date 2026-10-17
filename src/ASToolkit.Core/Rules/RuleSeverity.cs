namespace ASToolkit.Core.Rules;

/// <summary>
/// Defines what breaking a rule costs a level.
/// </summary>
public enum RuleSeverity
{
    /// <summary>The level is rejected.</summary>
    Hard,

    /// <summary>The level scores lower.</summary>
    Soft
}
