using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the outline of an original room on the building grid, its cells counted from the corner of its bounding box.
/// </summary>
/// <param name="Width">The width of the footprint's bounding box in grid units.</param>
/// <param name="Height">The height of the footprint's bounding box in grid units.</param>
/// <param name="Cells">The grid cells the footprint covers.</param>
public readonly record struct RoomFootprint(int Width, int Height, IReadOnlyList<Cell> Cells);
