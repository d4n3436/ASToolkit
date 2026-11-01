namespace ASToolkit.Core.Rooms;

/// <summary>
/// Specifies where a part of a room stands.
/// </summary>
public enum PartSpot
{
    /// <summary>
    /// Against one wall.
    /// </summary>
    Wall,

    /// <summary>
    /// Against two walls that meet.
    /// </summary>
    Corner,

    /// <summary>
    /// Away from the walls.
    /// </summary>
    Center
}
