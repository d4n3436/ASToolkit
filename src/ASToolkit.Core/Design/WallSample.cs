using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Design;

/// <summary>
/// Represents a floor object with the walls of its cell.
/// </summary>
/// <param name="Id">The object ID.</param>
/// <param name="Dir">The facing.</param>
/// <param name="Sides">The walls of the cell.</param>
/// <param name="IsOpen">Whether the 5 by 5 cells around it are floor with no wall in its own cell.</param>
public readonly record struct WallSample(KnownObject Id, byte Dir, WallSides Sides, bool IsOpen)
{
    /// <summary>
    /// Gets a value indicating whether two neighboring walls meet at the cell.
    /// </summary>
    public bool Corner => (Sides & (WallSides.UpperBack | WallSides.UpperSlash)) == WallSides.Far
                          || (Sides & (WallSides.UpperSlash | WallSides.LowerBack)) == (WallSides.UpperSlash | WallSides.LowerBack)
                          || (Sides & WallSides.Near) == WallSides.Near
                          || (Sides & (WallSides.LowerSlash | WallSides.UpperBack)) == (WallSides.LowerSlash | WallSides.UpperBack);

    /// <summary>
    /// Gets a value indicating whether only far walls touch the cell.
    /// </summary>
    public bool FarOnly => Sides != WallSides.None && (Sides & WallSides.Near) == 0;

    /// <summary>
    /// Gets a value indicating whether only near walls touch the cell.
    /// </summary>
    public bool NearOnly => Sides != WallSides.None && (Sides & WallSides.Far) == 0;

    /// <summary>
    /// Gets the single wall of the cell, or <see cref="WallSides.None"/> when it has none or several.
    /// </summary>
    public WallSides Single => Sides is WallSides.UpperBack or WallSides.UpperSlash or WallSides.LowerBack or WallSides.LowerSlash ? Sides : WallSides.None;
}
