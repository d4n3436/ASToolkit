using ASToolkit.Core.Formats;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents a room of an original level with its role, its type and what it holds.
/// </summary>
public sealed class AtlasRoom
{
    /// <summary>
    /// Gets the name of the map, such as Level_03.map or ADDON/Level_02.map.
    /// </summary>
    public required string MapName { get; init; }

    /// <summary>
    /// Gets the place.
    /// </summary>
    public required LevelPlace Place { get; init; }

    /// <summary>
    /// Gets where the room stands on the door graph.
    /// </summary>
    public required RoomRole Role { get; init; }

    /// <summary>
    /// Gets what the room is furnished as.
    /// </summary>
    public required RoomType Type { get; init; }

    /// <summary>
    /// Gets what the other side of a room furnished two ways is furnished as, such as the office side of a hall, or none.
    /// </summary>
    public required RoomType? Side { get; init; }

    /// <summary>
    /// Gets the type its contents suggest.
    /// </summary>
    public required RoomType ProposedType { get; init; }

    /// <summary>
    /// Gets the number of rooms it opens onto.
    /// </summary>
    public required int Doors { get; init; }

    /// <summary>
    /// Gets the share of its cell box its cells fill.
    /// </summary>
    public required float Fill { get; init; }

    /// <summary>
    /// Gets the number of pieces of each object in it.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, int> Pieces { get; init; }

    /// <summary>
    /// Gets a value indicating whether the place is a room of the level rather than a closet, a secret room or ground outside.
    /// </summary>
    public bool IsRoom => Role is not (RoomRole.Closet or RoomRole.Secret or RoomRole.Outside);

    /// <summary>
    /// Gets the number of desks, tables with a chair or a computer beside them.
    /// </summary>
    public required int Desks { get; init; }

    /// <summary>
    /// Gets the groups of furniture in it.
    /// </summary>
    public required IReadOnlyList<RoomPart> Parts { get; init; }

    /// <summary>
    /// Gets the number of each scattered piece in it, papers and corpses.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, int> Scatter { get; init; }
}
