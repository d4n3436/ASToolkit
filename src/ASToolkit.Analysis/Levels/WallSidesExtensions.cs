namespace ASToolkit.Analysis.Levels;

/// <summary>
/// Provides the shapes of the walls around a cell.
/// </summary>
public static class WallSidesExtensions
{
    extension(WallSides sides)
    {
        /// <summary>
        /// Gets a value indicating whether two walls that meet stand on the sides.
        /// </summary>
        public bool IsCorner => sides.HasFlag(WallSides.Far)
                                || sides.HasFlag(WallSides.UpperSlash | WallSides.LowerBack)
                                || sides.HasFlag(WallSides.Near)
                                || sides.HasFlag(WallSides.LowerSlash | WallSides.UpperBack);
    }
}
