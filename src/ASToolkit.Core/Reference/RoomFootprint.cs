using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the outline of an original room on the building grid, its cells counted from the corner of its bounding box.
/// </summary>
/// <param name="Width">Gets the width of the footprint's bounding box in grid units.</param>
/// <param name="Height">Gets the height of the footprint's bounding box in grid units.</param>
/// <param name="Cells">Gets the grid cells the footprint covers.</param>
public readonly record struct RoomFootprint(int Width, int Height, IReadOnlyList<Cell> Cells);
