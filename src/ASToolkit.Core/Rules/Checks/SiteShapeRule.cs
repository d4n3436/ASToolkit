using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that every dynamite site stands at a monster hole on lab levels and at a teleport pad on military levels (C6).
/// </summary>
public sealed class SiteShapeRule : ILevelRule
{
    private const float LabReach = 150;
    private const float MilitaryReach = 260;

    /// <inheritdoc/>
    public string Id => "C6";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var elements = context.Analysis.Elements;
        bool lab = context.Analysis.Theme == LevelTheme.Lab;
        var anchors = elements
            .Where(e => lab ? e.Kind is ElementKind.MonsterHole or ElementKind.SpawnWall or ElementKind.PowerTrap : e.Kind == ElementKind.TeleportPad)
            .ToArray();
        
        float reach = lab ? LabReach : MilitaryReach;
        foreach (var site in elements.Where(e => e.Kind == ElementKind.DynamiteSite))
        {
            float nearest = anchors.Length == 0 ? float.PositiveInfinity : anchors.Min(a => RuleMath.GetDistance(a.X, a.Y, site.X, site.Y));
            string anchor = lab ? "monster hole" : "teleport pad";
            yield return new RuleResult(Id, RuleSeverity.Hard, nearest <= reach, $"site at ({site.X}, {site.Y}) {nearest:0} px from the nearest {anchor}");
        }
    }
}
