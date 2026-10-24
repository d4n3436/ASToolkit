namespace ASToolkit.Core.Rules.Checks;

/// <summary>
/// Checks that the walk to the farthest point of the tour is about as long, for the floor area and for the tour, as in the
/// originals (A16).
/// </summary>
public sealed class SpineShareRule : ILevelRule
{
    private const float ShareTolerance = 0.08f;
    private const float ElongationTolerance = 0.4f;

    /// <inheritdoc/>
    public string Id => "A16";

    /// <inheritdoc/>
    public IEnumerable<RuleResult> Evaluate(RuleContext context)
    {
        var a = context.Analysis;
        var t = context.Target;
        float share = a.SpinePixels / (float)Math.Max(1, a.RoutePixels);
        yield return Compare(share, t.SpineShare, t.OriginalSpineShare, ShareTolerance, $"spine {a.SpinePixels} of tour {a.RoutePixels} ({share:0.00})");
        float elongation = ReferenceLevelSet.GetElongation(a);
        yield return Compare(elongation, t.Elongation, t.OriginalElongation, ElongationTolerance, $"spine over the root of the area {elongation:0.00}");
    }

    private RuleResult Compare(float value, float blended, float original, float tolerance, string message)
    {
        float low = Math.Min(blended, original);
        float high = Math.Max(blended, original);
        return new RuleResult(Id, RuleSeverity.Soft, value >= low - tolerance && value <= high + tolerance, $"{message}, originals {low:0.00} to {high:0.00}");
    }
}
