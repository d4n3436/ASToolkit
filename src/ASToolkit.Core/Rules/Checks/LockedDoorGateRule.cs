using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks the locked door gate of a level whose original locks its doors (B1).
/// </summary>
public sealed class LockedDoorGateRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "B1";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var t = context.Target;
        if (t.Style != LevelStyle.LockedDoors)
        {
            yield break;
        }

        var a = context.Analysis;
        yield return new RuleResult("B1.1", RuleSeverity.Hard,
            context.Measurement.Style == LevelStyle.LockedDoors && Math.Abs((a.Gating.LockedShare ?? 0) - t.LockedShare!.Value) <= 0.1f,
            $"locked {a.Gating.LockedShare:P0}, target {t.LockedShare:P0}");
        int? walk = a.Legs.Count > 0 && a.Legs[0].Phase == RoutePhase.DoorsLocked ? a.Legs[0].Pixels : null;
        if (t.WalkToBreakerPixels is not null)
        {
            yield return new RuleResult("B1.2", RuleSeverity.Soft, walk is not null && RuleMath.IsWithin(walk.Value, t.WalkToBreakerPixels.Value, 0.3f),
                $"walk to breaker {walk}, target {t.WalkToBreakerPixels}");
        }

        int? keyToGate = a.Gating.KeyToGatePixels;
        yield return new RuleResult("B1.3", RuleSeverity.Hard, keyToGate <= 700, $"breaker to next locked door {keyToGate}");
    }
}
