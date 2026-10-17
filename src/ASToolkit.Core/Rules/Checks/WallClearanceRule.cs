using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that decor stands as far from the walls as the originals place it (A14).
/// </summary>
public sealed class WallClearanceRule : ILevelRule
{
    private const double FloorHeight = 5;
    private const double Tolerance = 0.05;
    private const double MaxCloseShare = 0.12;

    /// <inheritdoc/>
    public string Id => "A14";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var model = context.Model;
        if (model is null)
        {
            yield break;
        }

        var table = context.Target.WallClearance;
        int measured = 0;
        int close = 0;
        var inWall = new List<string>();
        foreach (var levelObject in model.Objects.Where(o => o.Z < FloorHeight))
        {
            if (!table.TryGetClearance(levelObject.Id, out double minimum, out double low))
            {
                continue;
            }

            double d = WallClearanceTable.MeasureDistance(model.Edges, levelObject.U, levelObject.V);
            if (double.IsInfinity(d))
            {
                continue;
            }

            measured++;
            if (d < low)
            {
                close++;
            }

            if (d < minimum - Tolerance)
            {
                (double x, double y) = model.Lattice.ToWorld(levelObject.U, levelObject.V);
                inWall.Add($"{levelObject.Id} at ({x:0}, {y:0})");
            }
        }

        yield return new RuleResult(Id, RuleSeverity.Hard, inWall.Count == 0,
            inWall.Count == 0 ? "no decor closer to a wall than in any original" : $"closer to a wall than in any original: {string.Join(", ", inWall.Take(5))}");
        if (measured > 0)
        {
            yield return new RuleResult(Id, RuleSeverity.Soft, close <= MaxCloseShare * measured, $"{close} of {measured} objects closer to a wall than 95% of the originals'");
        }
    }
}
