namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents a fitted line of a level quantity over the level number with a term for levels of the concrete set, such as the natural logarithm of a level's floor pixels or of its rooms per million floor pixels.
/// </summary>
/// <param name="Intercept">The value of the line before the first level.</param>
/// <param name="Slope">The change of the line per level.</param>
/// <param name="ConcreteTerm">The shift of the line on levels of the concrete set.</param>
/// <param name="Spread">The standard deviation of the levels around the line.</param>
public readonly record struct WallSetLine(double Intercept, double Slope, double ConcreteTerm, double Spread)
{
    /// <summary>
    /// Returns the value the line gives a level.
    /// </summary>
    public double Evaluate(int level, bool isConcrete) => Intercept + Slope * level + (isConcrete ? ConcreteTerm : 0);
}
