using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that solid decor keeps the originals' gap from every dynamite site (C7).
/// </summary>
public sealed class SiteClearanceRule : ILevelRule
{
    private const double LargeExtent = 40;
    private const double SmallGap = 16;
    private const double LargeGap = 30;

    /// <inheritdoc/>
    public string Id => "C7";

    /// <summary>
    /// Returns the distance from a point to the footprint of an object.
    /// </summary>
    public static double MeasureGap(GameObject obj, double objectX, double objectY, double x, double y)
    {
        double dx = Math.Max(0, Math.Abs(objectX - x) - obj.ExtentX / 2);
        double dy = Math.Max(0, Math.Abs(objectY - y) - obj.ExtentY / 2);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Returns the smallest gap the originals leave between an object's footprint and a site.
    /// </summary>
    public static double GetMinGap(GameObject obj) => Math.Max(obj.ExtentX, obj.ExtentY) >= LargeExtent ? LargeGap : SmallGap;

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var sites = context.Map.Sprites.Where(s => s.Id == KnownObject.DynamiteSite).ToArray();
        if (sites.Length == 0)
        {
            yield break;
        }

        var crowded = new List<string>();
        foreach (var sprite in context.Map.Sprites)
        {
            var obj = context.Objects.Find(sprite.Id);
            if (obj is null || !obj.IsSolid || !sprite.Id.IsDecor)
            {
                continue;
            }

            if (sites.Any(site => MeasureGap(obj, sprite.X, sprite.Y, site.X, site.Y) < GetMinGap(obj)))
            {
                crowded.Add($"{sprite.Id} at ({sprite.X:0}, {sprite.Y:0})");
            }
        }

        yield return new RuleResult(Id, RuleSeverity.Hard, crowded.Count == 0,
            crowded.Count == 0 ? "no solid decor crowds a site" : $"crowding a site: {string.Join(", ", crowded.Take(5))}");
    }
}
