namespace ASToolkit.Analysis.Design;

/// <summary>
/// Represents a count out of a total, which adds up over levels.
/// </summary>
/// <param name="Count">The cases that meet the measure.</param>
/// <param name="Total">All cases.</param>
public readonly record struct MeasureShare(int Count, int Total)
{
    /// <summary>
    /// Gets the count over the total, or null when there are no cases.
    /// </summary>
    public float? Value => Total == 0 ? null : (float)Count / Total;

    /// <summary>
    /// Adds two shares.
    /// </summary>
    /// <param name="left">The first share.</param>
    /// <param name="right">The second share.</param>
    /// <returns>The share with the summed counts and totals.</returns>
    public static MeasureShare operator +(MeasureShare left, MeasureShare right) => new(left.Count + right.Count, left.Total + right.Total);
}
