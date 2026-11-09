using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Represents a building laid out by its program, with its rooms and their phases, the rooms that touch, and the locks and roles the doors stage
/// takes as given.
/// </summary>
public sealed record BuildingPlan
{
    /// <summary>Gets the gate style of the building's level.</summary>
    public required LevelStyle Gate { get; init; }

    /// <summary>Gets a value indicating whether the level has dynamite sites.</summary>
    public required bool HasSites { get; init; }

    /// <summary>Gets the rooms of the building.</summary>
    public required IReadOnlyList<BuildingRoom> Rooms { get; init; }

    /// <summary>Gets the pairs of rooms that share a wall a door can stand in.</summary>
    public required IReadOnlyList<RoomPair> Contacts { get; init; }

    /// <summary>Gets the room the player starts in.</summary>
    public required int Start { get; init; }

    /// <summary>Gets the key rooms, the breaker room or the generator rooms, nearest first.</summary>
    public required IReadOnlyList<int> Keys { get; init; }

    /// <summary>Gets the room behind the gate into the next phase.</summary>
    public int? GateRoom { get; init; }

    /// <summary>Gets the room whose entering triggers the blackout.</summary>
    public int? Trigger { get; init; }

    /// <summary>Gets the room the program ends the level in, when the program sets it.</summary>
    public int? End { get; init; }

    /// <summary>Gets the locked contacts into later phases, from the earlier room.</summary>
    public required IReadOnlyList<(int From, int To)> Locks { get; init; }

    /// <summary>Gets the locked contacts inside a phase that open with its key.</summary>
    public required IReadOnlyList<RoomPair> Shortcuts { get; init; }

    /// <summary>Gets the walls two rooms share that the design keeps shut, never given a door.</summary>
    public IReadOnlyList<RoomPair> ClosedWalls { get; init; } = [];

    /// <summary>Gets the walls two rooms share that the design shuts with a jammed door.</summary>
    public IReadOnlyList<RoomPair> JammedWalls { get; init; } = [];

    /// <summary>Gets the walls two rooms share that the design opens along their whole length.</summary>
    public IReadOnlyList<RoomPair> OpenWalls { get; init; } = [];

    /// <summary>Gets the rooms of a lab's second wing, the only ones monsters and dynamite sites stand in, any room when empty.</summary>
    public IReadOnlyList<int> SecondWing { get; init; } = [];

    /// <summary>Gets the site rooms locked until the key, some of the sites standing in them.</summary>
    public IReadOnlyList<int> LockedSiteRooms { get; init; } = [];

    /// <summary>
    /// Returns the building with every neck that carries no door dropped, a neck being the throat of a door and nothing else.
    /// </summary>
    public BuildingPlan DropNecksWithoutDoors(IReadOnlyCollection<RoomPair> doors)
    {
        var rooms = Rooms.ToArray();
        for (int index = 0; index < rooms.Length; index++)
        {
            var room = rooms[index];
            var kept = room.Necks
                .Where(neck => rooms.Any(other => other.Id != room.Id && other.Rects.Any(rect => rect.GetSharedEdges(neck).Length > 0)
                    && doors.Contains(new RoomPair(room.Id, other.Id))))
                .ToArray();

            if (room.Necks.Count > 0)
            {
                rooms[index] = room with { Rects = room.Rects.Where(rect => !room.Necks.Contains(rect) || kept.Contains(rect)).ToArray(), Necks = kept };
            }
        }

        return this with { Rooms = rooms };
    }
}
