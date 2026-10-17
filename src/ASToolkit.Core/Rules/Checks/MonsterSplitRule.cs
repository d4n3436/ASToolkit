namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the share of monster hit points brought by ambushes is near the original's ambush chance (A5).
/// </summary>
public sealed class MonsterSplitRule : ILevelRule
{
    private const double Tolerance = 0.3;

    /// <inheritdoc/>
    public string Id => "A5";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        double spawned = context.Measurement.SpawnedShare;
        double chance = context.Target.AmbushChance;
        yield return new RuleResult(Id, RuleSeverity.Soft, Math.Abs(spawned - chance) <= Tolerance, $"ambush share {spawned:P0}, chance {chance:P0}");
    }
}
