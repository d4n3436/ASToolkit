namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the tour reaches every objective without secret walls or forced gates (A2).
/// </summary>
public sealed class ReachabilityRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A2";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var f = context.Analysis.FallbackUsage;
        yield return new RuleResult(Id, RuleSeverity.Hard, f is { UsedSecretWalls: false, ForcedGates: false, Unreached: 0 },
            $"secret walls {f.UsedSecretWalls}, forced gates {f.ForcedGates}, unreached {f.Unreached}");
    }
}
