using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Rooms;

namespace ASToolkit.Generation.Buildings;

/// <summary>
/// Represents a room of a building with its outline in layout units and the phase it belongs to.
/// </summary>
public sealed record BuildingRoom
{
    /// <summary>Gets the ID of the room, which is its index in the building's rooms.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the kind of the room.</summary>
    public required BuildingRoomKind Kind { get; init; }

    /// <summary>Gets the type of the room.</summary>
    public required RoomType Type { get; init; }

    /// <summary>Gets the rectangles that make up the outline of the room, necks and arms included.</summary>
    public required IReadOnlyList<UnitRect> Rects { get; init; }

    /// <summary>Gets the necks among the rects of the room, each of which is the throat of a door.</summary>
    public required IReadOnlyList<UnitRect> Necks { get; init; }

    /// <summary>Gets the phase the room belongs to.</summary>
    public required int Phase { get; init; }

    /// <summary>Gets the room a closet or secret room opens from.</summary>
    public int? Host { get; init; }
}
