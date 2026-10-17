namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that a level has a checkpoint early in the tour exactly when its original has one (A9).
/// </summary>
public sealed class CheckpointRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A9";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var checkpoints = context.Analysis.Checkpoints;
        if (context.Target.HasCheckpoint)
        {
            yield return new RuleResult(Id, RuleSeverity.Hard, checkpoints.Any(c => c.Progress is >= 0.08 and <= 0.5),
                $"checkpoints at {string.Join(", ", checkpoints.Select(c => c.Progress.ToString("0.00")))}");
        }
        else
        {
            yield return new RuleResult(Id, RuleSeverity.Hard, checkpoints.Count == 0, $"{checkpoints.Count} checkpoints, original has none");
        }
    }
}
