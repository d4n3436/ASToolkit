using ASToolkit.Core.Levels;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that decor faces the way the originals turn it against walls of that direction, and that frames they lean on a wall have one (A17).
/// </summary>
public sealed class WallFacingRule : ILevelRule
{
    private const double FloorHeight = 5;
    private const double BloodReach = 0.15;

    /// <inheritdoc/>
    public string Id => "A17";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var model = context.Model;
        if (model is null)
        {
            yield break;
        }

        var pools = model.Objects.Where(o => KnownObject.BloodPools.Contains(o.Id)).ToArray();
        var wrong = new List<string>();
        foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight && !context.Target.Facings.Fits(o.Id, o.Dir, model.Edges, o.U, o.V, IsOnBlood(o))))
        {
            (double x, double y) = model.Lattice.ToWorld(levelObject.U, levelObject.V);
            wrong.Add($"{levelObject.Id} at ({x:0}, {y:0}) dir {levelObject.Dir}");
        }

        yield return new RuleResult(Id, RuleSeverity.Hard, wrong.Count == 0,
            wrong.Count == 0 ? "decor faces its walls as in the originals" : $"facing no original gives it there: {string.Join(", ", wrong.Take(5))}");
        
        yield break;
        
        bool IsOnBlood(DecorObject o) => pools.Any(p => Math.Abs(p.U - o.U) < BloodReach && Math.Abs(p.V - o.V) < BloodReach);
    }
}
