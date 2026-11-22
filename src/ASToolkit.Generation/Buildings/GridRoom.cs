using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Rooms;

namespace ASToolkit.Generation.Buildings;

/// <summary>
/// Represents a rectangle placed on a building grid, with the group that decides which other rectangles it keeps its distance from.
/// </summary>
internal sealed class GridRoom
{
    public required int Id { get; init; }

    public required UnitRect Rect { get; set; }

    public required GridKind Kind { get; init; }

    public required int Group { get; init; }

    public int Phase { get; set; }

    public RoomType? Type { get; set; }

    public bool AlongU { get; init; }

    public bool IsJoint { get; init; }

    public int? Owner { get; init; }

    public int? Host { get; init; }

}
