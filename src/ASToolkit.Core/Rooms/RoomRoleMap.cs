using ASToolkit.Core.Design;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents the rooms of a surveyed level by their role on its door graph.
/// </summary>
public sealed class RoomRoleMap
{
    private readonly Dictionary<int, HashSet<int>> _neighbors;

    private RoomRoleMap(Dictionary<int, HashSet<int>> neighbors)
    {
        _neighbors = neighbors;
    }

    /// <summary>
    /// Gets the start room.
    /// </summary>
    public required int Start { get; init; }

    /// <summary>
    /// Gets the first hall on the way from the start to the end, or the start when there is none.
    /// </summary>
    public required int Hub { get; init; }

    /// <summary>
    /// Gets a value indicating whether the hub is a hall.
    /// </summary>
    public required bool HasHall { get; init; }

    /// <summary>
    /// Gets the room after the hub on the way to the end.
    /// </summary>
    public required int Neck { get; init; }

    /// <summary>
    /// Gets the largest hall of the main body other than the hub, when there is one.
    /// </summary>
    public required int? SecondHall { get; init; }

    /// <summary>
    /// Gets the main body rooms on the sides of the hub.
    /// </summary>
    public required IReadOnlyList<int> HubRooms { get; init; }

    /// <summary>
    /// Gets the main body rooms on the sides of the second hall.
    /// </summary>
    public required IReadOnlyList<int> HallRooms { get; init; }

    /// <summary>
    /// Gets the other rooms of the main body, with its halls when the hub is not one.
    /// </summary>
    public required IReadOnlyList<int> BodyRooms { get; init; }

    /// <summary>
    /// Gets the rooms off the main body by branch, from the rooms that open onto it outward.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<int>> Branches { get; init; }

    /// <summary>
    /// Gets the room that holds the key of a gate, when there is one.
    /// </summary>
    public required int? Key { get; init; }

    /// <summary>
    /// Gets the rooms on the shortest way from the start to the end.
    /// </summary>
    public required IReadOnlyList<int> Path { get; init; }

    /// <summary>
    /// Measures the roles of the rooms of a surveyed level.
    /// </summary>
    /// <param name="survey">The survey of the level.</param>
    public static RoomRoleMap Measure(LevelSurvey survey)
    {
        var places = survey.Places.ToDictionary(place => place.Id);
        (int[] rooms, var neighbors) = RoomGraph.FindDoorGraph(survey);
        (int start, int end) = RoomGraph.FindEnds(survey);
        int? key = survey.Gates.FirstOrDefault(gate => gate.KeyPlace is not null)?.KeyPlace;
        return Measure(rooms, neighbors, start, end, room => places[room].Kind == PlaceKind.Hall, room => places[room].AreaPixels,
            room => places[room].FirstVisit ?? int.MaxValue, key);
    }

    /// <summary>
    /// Measures the roles of the rooms of a door graph.
    /// </summary>
    /// <param name="rooms">The rooms.</param>
    /// <param name="joins">The pairs of rooms that open onto each other.</param>
    /// <param name="start">The start room.</param>
    /// <param name="end">The room the level ends in.</param>
    /// <param name="isHall">Returns whether a room is a hall.</param>
    /// <param name="area">Returns the floor area of a room.</param>
    /// <param name="order">Returns the order the player reaches a room in.</param>
    /// <param name="key">The room that holds the key of a gate, when there is one.</param>
    public static RoomRoleMap Measure(IReadOnlyCollection<int> rooms, IEnumerable<(int A, int B)> joins, int start, int end, Func<int, bool> isHall, Func<int, int> area,
        Func<int, int> order, int? key)
    {
        var neighbors = rooms.ToDictionary(room => room, _ => new HashSet<int>());
        foreach ((int a, int b) in joins.Where(join => join.A != join.B && neighbors.ContainsKey(join.A) && neighbors.ContainsKey(join.B)))
        {
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        return Measure(rooms.ToArray(), neighbors, start, end, isHall, area, order, key);
    }

    private static RoomRoleMap Measure(int[] rooms, Dictionary<int, HashSet<int>> neighbors, int start, int end, Func<int, bool> isHall, Func<int, int> area,
        Func<int, int> order, int? key)
    {
        var body = RoomGraph.FindMainBody(rooms, neighbors, start, end);
        var path = neighbors.FindPath(start, end);
        if (path.Count == 0)
        {
            throw new InvalidOperationException("Expected the end room to be reachable from the start.");
        }

        int? hall = path.Cast<int?>().FirstOrDefault(room => isHall(room!.Value));
        int hub = hall ?? start;
        int neck = path[Math.Min(path.Count - 1, path.IndexOf(hub) + 1)];
        int? second = hall is null ? null : body.Where(room => room != hub && isHall(room)).OrderByDescending(area).Cast<int?>().FirstOrDefault();
        int[] hubRooms = hall is null ? [] : body.Where(room => room != start && room != neck && !isHall(room) && neighbors[hub].Contains(room)).ToArray();
        int[] hallRooms = second is null ? [] : body.Where(room => room != neck && !isHall(room) && neighbors[second.Value].Contains(room) && !hubRooms.Contains(room)).ToArray();
        return new RoomRoleMap(neighbors)
        {
            Start = start,
            Hub = hub,
            HasHall = hall is not null,
            Neck = neck,
            SecondHall = second,
            HubRooms = hubRooms,
            HallRooms = hallRooms,
            BodyRooms = body.Where(room => room != start && room != neck && (hall is null || !isHall(room)) && !hubRooms.Contains(room) && !hallRooms.Contains(room)).ToArray(),
            Branches = RoomGraph.FindBranches(rooms, neighbors, body).Select(branch => OrderFromRoot(branch, neighbors, body, order)).ToArray(),
            Key = key,
            Path = path
        };
    }

    /// <summary>
    /// Returns whether two rooms open onto each other.
    /// </summary>
    /// <param name="room">The one room.</param>
    /// <param name="other">The other room.</param>
    public bool AreJoined(int room, int other) => _neighbors.TryGetValue(room, out var next) && next.Contains(other);

    /// <summary>
    /// Returns the number of rooms a room opens onto.
    /// </summary>
    /// <param name="room">The room.</param>
    public int CountDoors(int room) => _neighbors.TryGetValue(room, out var next) ? next.Count : 0;

    /// <summary>
    /// Returns the role of a place.
    /// </summary>
    /// <param name="place">The place.</param>
    public RoomRole GetRole(LevelPlace place) =>
        place.Kind == PlaceKind.Nook ? RoomRole.Closet : place.Zone == GateZone.Secret && _neighbors.ContainsKey(place.Id) ? RoomRole.Secret : GetRole(place.Id, place.Kind == PlaceKind.Hall);

    /// <summary>
    /// Returns the role of a room.
    /// </summary>
    /// <param name="id">The room.</param>
    /// <param name="isHall">Whether the room is a hall.</param>
    public RoomRole GetRole(int id, bool isHall)
    {
        if (!_neighbors.ContainsKey(id))
        {
            return RoomRole.Closet;
        }

        if (id == Start)
        {
            return RoomRole.Start;
        }

        if (HasHall && id == Hub)
        {
            return RoomRole.Hub;
        }

        if (id == Key && Branches.Any(branch => branch is [var only] && only == id))
        {
            return RoomRole.Key;
        }

        if (id == Neck)
        {
            return RoomRole.Neck;
        }

        if (isHall && !Branches.Any(branch => branch.Contains(id)))
        {
            return RoomRole.Hall;
        }

        if (HubRooms.Contains(id))
        {
            return RoomRole.HubRow;
        }

        if (HallRooms.Contains(id))
        {
            return RoomRole.HallRow;
        }

        var owner = Branches.FirstOrDefault(branch => branch.Contains(id));
        return owner is null ? RoomRole.Body : owner[0] == id ? RoomRole.BranchRoot : RoomRole.Branch;
    }

    // Walk a branch outward from the rooms that open onto the main body
    private static List<int> OrderFromRoot(List<int> branch, Dictionary<int, HashSet<int>> neighbors, HashSet<int> body, Func<int, int> order)
    {
        var members = branch.ToHashSet();
        var ordered = branch.Where(room => neighbors[room].Overlaps(body)).OrderBy(order).ToList();
        for (int index = 0; index < ordered.Count; index++)
        {
            ordered.AddRange(neighbors[ordered[index]].Where(next => members.Contains(next) && !ordered.Contains(next)).OrderBy(order));
        }

        return ordered;
    }
}
