using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that a level has one mounted cannon midway through the tour with free floor around it when its original has one,
/// and none otherwise (D5).
/// </summary>
public sealed class MountedCannonRule : ILevelRule
{
    private const double FreeRadius = 100;

    /// <inheritdoc/>
    public string Id => "D5";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var cannons = context.Analysis.Elements.Where(e => e.Kind == ElementKind.MountedCannon).ToArray();
        if (!context.Target.HasMountedCannon)
        {
            yield return new RuleResult(Id, RuleSeverity.Soft, cannons.Length == 0, $"{cannons.Length} cannons, original has none");
            yield break;
        }

        yield return new RuleResult(Id, RuleSeverity.Soft, cannons is [{ Progress: >= 0.15 and <= 0.8 }],
            $"cannons at {string.Join(", ", cannons.Select(c => c.Progress.ToString("0.00")))}");
        var grid = context.Analysis.Grid;
        foreach (var cannon in cannons)
        {
            double clear = grid.BlockedCells.Select(c => GetDistance(grid.Lattice, c, cannon)).DefaultIfEmpty(double.PositiveInfinity).Min();
            yield return new RuleResult(Id, RuleSeverity.Soft, clear >= FreeRadius, $"nearest solid object {clear:0} px from the cannon, at least {FreeRadius}");
        }
    }

    private static double GetDistance(Lattice lattice, Cell c, RouteElement cannon)
    {
        (double x, double y) = lattice.GetCenter(c);
        return RuleMath.GetDistance(x, y, cannon.X, cannon.Y);
    }
}
