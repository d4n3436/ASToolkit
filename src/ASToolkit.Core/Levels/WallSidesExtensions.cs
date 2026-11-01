namespace ASToolkit.Core.Levels;

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
        public bool IsCorner => (sides & WallSides.Far) == WallSides.Far
                                || (sides & (WallSides.UpperSlash | WallSides.LowerBack)) == (WallSides.UpperSlash | WallSides.LowerBack)
                                || (sides & WallSides.Near) == WallSides.Near
                                || (sides & (WallSides.LowerSlash | WallSides.UpperBack)) == (WallSides.LowerSlash | WallSides.UpperBack);
    }
}
