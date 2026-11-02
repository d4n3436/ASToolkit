namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents a fitted line of a level quantity over the level number with a term for military levels, such as the natural logarithm of a level's floor pixels or of its rooms per million floor pixels.
/// </summary>
/// <param name="Intercept">The value of the line before the first level.</param>
/// <param name="Slope">The change of the line per level.</param>
/// <param name="MilitaryTerm">The shift of the line on military levels.</param>
/// <param name="Spread">The standard deviation of the levels around the line.</param>
public readonly record struct ThemeLine(double Intercept, double Slope, double MilitaryTerm, double Spread)
{
    /// <summary>
    /// Returns the value the line gives a level.
    /// </summary>
    public double Evaluate(int level, bool isMilitary) => Intercept + Slope * level + (isMilitary ? MilitaryTerm : 0);
}
