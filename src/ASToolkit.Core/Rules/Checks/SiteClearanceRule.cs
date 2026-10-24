using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that solid decor keeps the originals' gap from every dynamite site (C7).
/// </summary>
public sealed class SiteClearanceRule : ILevelRule
{
    private const float LargeExtent = 40;
    private const float SmallGap = 16;
    private const float LargeGap = 30;

    /// <inheritdoc/>
    public string Id => "C7";

    /// <summary>
    /// Returns the distance from a point to the footprint of an object.
    /// </summary>
    public static float MeasureGap(GameObject obj, float objectX, float objectY, float x, float y)
    {
        float dx = Math.Max(0, Math.Abs(objectX - x) - obj.Size.X / 2);
        float dy = Math.Max(0, Math.Abs(objectY - y) - obj.Size.Y / 2);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Returns the smallest gap the originals leave between an object's footprint and a site.
    /// </summary>
    public static float GetMinGap(GameObject obj) => Math.Max(obj.Size.X, obj.Size.Y) >= LargeExtent ? LargeGap : SmallGap;

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
            if (!context.Objects.TryFind(sprite.Id, out var obj) || !obj.IsSolid || !sprite.Id.IsDecor)
            {
                continue;
            }

            if (sites.Any(site => MeasureGap(obj, sprite.Position.X, sprite.Position.Y, site.Position.X, site.Position.Y) < GetMinGap(obj)))
            {
                crowded.Add($"{sprite.Id} at ({sprite.Position.X:0}, {sprite.Position.Y:0})");
            }
        }

        yield return new RuleResult(Id, RuleSeverity.Hard, crowded.Count == 0,
            crowded.Count == 0 ? "no solid decor crowds a site" : $"crowding a site: {string.Join(", ", crowded.Take(5))}");
    }
}
