using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that ambush spawners lie ahead of their trigger and out of sight (A7).
/// </summary>
public sealed class AmbushPlacementRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "A7";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var elements = context.Analysis.Elements;
        var spawners = elements.Where(e => e is { Kind: ElementKind.Spawner, TriggerProgress: not null }).ToArray();
        if (spawners.Length == 0)
        {
            yield break;
        }

        var walls = elements.Where(e => e.Kind == ElementKind.SpawnWall).ToArray();
        var pads = elements.Where(e => e.Kind == ElementKind.TeleportPad).ToArray();
        int ahead = 0, side = 0, behind = 0, level = 0, inView = 0, inRange = 0, open = 0;
        foreach (var spawner in spawners)
        {
            bool closet = IsNear(spawner, walls, 100);
            bool atPad = IsNear(spawner, pads, 260);
            if (spawner.BranchDepth > 300 && spawner.BranchDepth - (spawner.TriggerBranchDepth ?? 0) > 200)
            {
                side++;
            }
            else if (spawner.Progress > spawner.TriggerProgress + 0.03)
            {
                ahead++;
            }
            else if (spawner.Progress < spawner.TriggerProgress - 0.03)
            {
                behind++;
            }
            else if (!closet && !atPad)
            {
                level++;
            }

            if (closet || atPad)
            {
                continue;
            }

            open++;
            (int x, int y, _) = spawner.Program!.Trigger!.Value;
            if (Math.Abs(spawner.X - x) < 640 && Math.Abs(spawner.Y - y) < 400)
            {
                inView++;
            }

            if (spawner.TriggerDistance is >= 900 and <= 3500)
            {
                inRange++;
            }
        }

        int n = spawners.Length;
        yield return new RuleResult(Id, RuleSeverity.Hard, behind <= 0.25 * n, $"behind {behind} of {n}");
        yield return new RuleResult(Id, RuleSeverity.Soft, ahead + side >= 0.85 * n, $"ahead {ahead}, side room {side} of {n}");
        yield return new RuleResult(Id, RuleSeverity.Soft, level <= 0.05 * n, $"at the trigger {level} of {n}");
        if (open > 0)
        {
            yield return new RuleResult(Id, RuleSeverity.Soft, inView <= 0.1 * open, $"within a screen of the trigger {inView} of {open}");
            yield return new RuleResult(Id, RuleSeverity.Soft, inRange >= 0.8 * open, $"900-3500 px from the trigger {inRange} of {open}");
        }
    }

    private static bool IsNear(RouteElement e, IReadOnlyList<RouteElement> others, double distance) =>
        others.Any(o => Math.Abs(o.X - e.X) < distance && Math.Abs(o.Y - e.Y) < distance);
}
