using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Represents a rectangle a building grammar placed on its grid, with the group it keeps its distance from other groups by.
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
