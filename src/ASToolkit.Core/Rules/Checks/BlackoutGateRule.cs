using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks the blackout of a level whose original has one (B2).
/// </summary>
public sealed class BlackoutGateRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "B2";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Target.Style != LevelStyle.Blackout)
        {
            yield break;
        }

        var a = context.Analysis;
        var blackout = a.Objectives.FirstOrDefault(o => o.Kind == ObjectiveKind.Blackout);
        int? before = blackout is null ? null : (int)Math.Round(blackout.Progress * a.RoutePixels);
        yield return new RuleResult("B2.1", RuleSeverity.Hard, context.Measurement.Style == LevelStyle.Blackout && before <= 2000,
            $"tour before the blackout {before}");
        int searched = a.Legs.Where(l => l.Phase == RoutePhase.DoorsLocked).Select(l => l.Pixels).FirstOrDefault();
        yield return new RuleResult("B2.2", RuleSeverity.Hard, searched is >= 4000 and <= 6500, $"blackout to breaker {searched}");
        if (context.Script.Blackout is not null)
        {
            var b = context.Script.Blackout.Value;
            yield return new RuleResult("B2.3", RuleSeverity.Soft, a.Checkpoints.Any(c => Math.Abs(c.X - b.X) + Math.Abs(c.Y - b.Y) <= 150), "checkpoint on the blackout trigger");
        }
    }
}
