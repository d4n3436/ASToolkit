using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that each batch of charges on the tour is followed by as many sites as it holds charges (C3).
/// </summary>
public sealed class ChargeBatchRule : ILevelRule
{
    /// <inheritdoc/>
    public string Id => "C3";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        if (context.Target.DynamiteSites == 0)
        {
            yield break;
        }

        var sequence = context.Analysis.Objectives
            .Where(o => o.Kind is ObjectiveKind.Dynamite or ObjectiveKind.DynamiteSite)
            .Select(o => o.Kind)
            .ToArray();
        
        yield return new RuleResult(Id, RuleSeverity.Soft, IsBatched(sequence), string.Concat(sequence.Select(k => k == ObjectiveKind.Dynamite ? 'D' : 'S')));
    }

    private static bool IsBatched(ObjectiveKind[] sequence)
    {
        int i = 0;
        while (i < sequence.Length)
        {
            int charges = 0;
            while (i < sequence.Length && sequence[i] == ObjectiveKind.Dynamite)
            {
                charges++;
                i++;
            }

            int sites = 0;
            while (i < sequence.Length && sequence[i] == ObjectiveKind.DynamiteSite)
            {
                sites++;
                i++;
            }

            if (charges != sites)
            {
                return false;
            }
        }

        return true;
    }
}
