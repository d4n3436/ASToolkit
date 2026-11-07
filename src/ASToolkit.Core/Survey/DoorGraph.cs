using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents rooms joined by the doors and openings a player walks through.
/// </summary>
public sealed class DoorGraph
{
    private const int ShapeHashRounds = 3;

    private readonly int[] _rooms;
    private readonly Dictionary<int, HashSet<int>> _neighbors;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoorGraph"/> class.
    /// </summary>
    public DoorGraph(int[] rooms, Dictionary<int, HashSet<int>> neighbors)
    {
        _rooms = rooms;
        _neighbors = neighbors;
    }

    /// <summary>
    /// Gets the rooms of the graph.
    /// </summary>
    public IReadOnlyList<int> Rooms => _rooms;

    /// <summary>
    /// Gets the rooms each room of the graph opens onto.
    /// </summary>
    public IReadOnlyDictionary<int, HashSet<int>> Neighbors => _neighbors;

    /// <summary>
    /// Creates the door graph of a surveyed level's rooms other than closets, joined through every connection a player walks.
    /// </summary>
    public static DoorGraph Create(LevelSurvey survey)
    {
        int closet = GetClosetLimit(survey);
        var entered = FindWallEnteredRooms(survey, closet);

        var hidden = survey.Places
            .Where(place => survey.WallSet != WallSet.Outdoor && IsHiddenRoom(place, closet) && !entered.Contains(place.Id))
            .Select(place => place.Id)
            .ToHashSet();

        var sealedRooms = hidden
            .Where(room => survey.Connections
                .Where(connection => (connection.A == room && !hidden.Contains(connection.B)) || (connection.B == room && !hidden.Contains(connection.A)))
                .All(connection => connection.Kind == ConnectionKind.SecretWall))
            .ToHashSet();

        int[] rooms = survey.Places
            .Where(place => (place.Kind is not (PlaceKind.SecretCloset or PlaceKind.SpawnCloset) || entered.Contains(place.Id)) && !sealedRooms.Contains(place.Id))
            .Select(place => place.Id)
            .ToArray();
        
        var roomSet = rooms.ToHashSet();
        var neighbors = rooms.ToDictionary(room => room, _ => new HashSet<int>());
        foreach (var connection in survey.Connections)
        {
            bool walked = connection.Kind is not (ConnectionKind.SecretWall or ConnectionKind.SpawnWall or ConnectionKind.MonsterHole or ConnectionKind.JammedDoor)
                || (connection.Kind == ConnectionKind.SecretWall && (entered.Contains(connection.A) || entered.Contains(connection.B)));

            if (!walked || connection.A == connection.B || !roomSet.Contains(connection.A) || !roomSet.Contains(connection.B))
                continue;
            
            neighbors[connection.A].Add(connection.B);
            neighbors[connection.B].Add(connection.A);
        }

        return new DoorGraph(rooms, neighbors);
    }

    // Enter a room bigger than a closet through its breakable wall when its door is jammed
    private static int GetClosetLimit(LevelSurvey survey)
    {
        int[] closets = survey.Places
            .Where(place => place.Kind == PlaceKind.SecretCloset)
            .Select(place => place.Cells.Count)
            .Order()
            .ToArray();

        return closets.Length > 0 ? 2 * closets[closets.Length / 2] : 0;
    }

    private static bool IsHiddenRoom(LevelPlace place, int closet) => place.Zone == GateZone.Secret && place.Kind != PlaceKind.SpawnCloset && place.Cells.Count > closet;

    private static HashSet<int> FindWallEnteredRooms(LevelSurvey survey, int closet)
    {
        var jammed = GetEnds(survey, ConnectionKind.JammedDoor);
        var walled = GetEnds(survey, ConnectionKind.SecretWall);
        return survey.Places
            .Where(place => IsHiddenRoom(place, closet) && jammed.Contains(place.Id) && walled.Contains(place.Id))
            .Select(place => place.Id)
            .ToHashSet();
    }

    private static HashSet<int> GetEnds(LevelSurvey survey, ConnectionKind kind) => survey.Connections
        .Where(connection => connection.Kind == kind)
        .SelectMany(connection => new[] { connection.A, connection.B })
        .ToHashSet();

    /// <summary>
    /// Returns the place the tour of a surveyed level starts in and the place it ends in.
    /// </summary>
    public static (int Start, int End) FindEnds(LevelSurvey survey) =>
        (survey.Landmarks.First(landmark => landmark.Kind == LandmarkKind.PlayerStart).Place,
            survey.Landmarks.FirstOrDefault(landmark => landmark.Kind == LandmarkKind.RouteEnd)?.Place ?? survey.Route[^1].Place);

    /// <summary>
    /// Returns the _rooms of the biconnected blocks on the way from one room to another.
    /// </summary>
    public HashSet<int> FindMainBody(int start, int end)
    {
        if (start == end)
        {
            return [start];
        }

        var blocks = FindBlocks();
        var tree = new Dictionary<(bool Block, int Id), List<(bool Block, int Id)>>();
        for (int index = 0; index < blocks.Count; index++)
        {
            foreach (int room in blocks[index])
            {
                Link((true, index), (false, room));
                Link((false, room), (true, index));
            }
        }

        var previous = new Dictionary<(bool Block, int Id), (bool Block, int Id)>();
        var queue = new Queue<(bool Block, int Id)>([(false, start)]);
        previous[(false, start)] = (false, start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var next in tree.GetValueOrDefault(node, []))
            {
                if (previous.TryAdd(next, node))
                {
                    queue.Enqueue(next);
                }
            }
        }

        var body = new HashSet<int> { start };
        if (!previous.ContainsKey((false, end)))
        {
            return body;
        }

        for (var node = (false, end); node != (false, start); node = previous[node])
        {
            if (node.Item1)
            {
                body.UnionWith(blocks[node.Item2]);
            }
            else
            {
                body.Add(node.Item2);
            }
        }

        return body;

        void Link((bool, int) from, (bool, int) to)
        {
            if (!tree.TryGetValue(from, out var list))
            {
                list = [];
                tree[from] = list;
            }

            list.Add(to);
        }
    }

    /// <summary>
    /// Returns the groups of _rooms off a main body, each joined to the body through one of its _rooms.
    /// </summary>
    public List<List<int>> FindBranches(HashSet<int> body)
    {
        var branches = new List<List<int>>();
        var seen = new HashSet<int>(body);
        foreach (int room in _rooms)
        {
            if (!seen.Add(room))
                continue;
            
            var branch = FillComponent(room, seen);
            if (branch.Any(member => _neighbors[member].Overlaps(body)))
            {
                branches.Add(branch);
            }
        }

        return branches;
    }

    /// <summary>
    /// Returns the number of connected parts of the graph.
    /// </summary>
    public int CountComponents()
    {
        var seen = new HashSet<int>();
        int count = 0;
        foreach (int room in _rooms)
        {
            if (!seen.Add(room))
                continue;
            
            count++;
            FillComponent(room, seen);
        }

        return count;
    }

    /// <summary>
    /// Returns the graph of the given rooms and the doors between them.
    /// </summary>
    public DoorGraph GetSubgraph(IReadOnlySet<int> rooms)
    {
        int[] kept = _rooms.Where(rooms.Contains).ToArray();
        return new DoorGraph(kept, kept.ToDictionary(room => room, room => _neighbors[room].Where(rooms.Contains).ToHashSet()));
    }

    /// <summary>
    /// Returns the number of independent cycles of the graph.
    /// </summary>
    public int CountLoops() => _neighbors.Values.Sum(neighbors => neighbors.Count) / 2 - _rooms.Length + CountComponents();

    /// <summary>
    /// Returns a hash of the graph's shape that equal graphs share, from three rounds of Weisfeiler-Lehman relabeling.
    /// </summary>
    public ulong ComputeShapeHash()
    {
        var labels = _rooms.ToDictionary(room => room, room => (ulong)_neighbors[room].Count);
        for (int round = 0; round < ShapeHashRounds; round++)
        {
            labels = _rooms.ToDictionary(room => room, room => _neighbors[room]
                .Select(next => labels[next])
                .Order()
                .Aggregate(Mix(labels[room]), (hash, label) => Mix(hash ^ label)));
        }

        return labels.Values.Order().Aggregate(Mix((ulong)_rooms.Length), (hash, label) => Mix(hash ^ label));
    }

    /// <summary>
    /// Returns the number of neighbors of each room, from fewest to most.
    /// </summary>
    public int[] GetDegrees() => _rooms.Select(room => _neighbors[room].Count).Order().ToArray();

    /// <summary>
    /// Returns the rooms of each biconnected block of the graph.
    /// </summary>
    public List<HashSet<int>> FindBlocks()
    {
        var blocks = new List<HashSet<int>>();
        var order = new Dictionary<int, int>();
        var low = new Dictionary<int, int>();
        var stack = new Stack<(int, int)>();
        foreach (int room in _rooms)
        {
            if (!order.ContainsKey(room))
            {
                Visit(room, -1);
            }
        }

        return blocks;

        void Visit(int room, int parent)
        {
            order[room] = low[room] = order.Count;
            foreach (int next in _neighbors[room])
            {
                if (!order.TryGetValue(next, out int value))
                {
                    stack.Push((room, next));
                    Visit(next, room);
                    low[room] = Math.Min(low[room], low[next]);
                    if (low[next] < order[room])
                        continue;
                    
                    var block = new HashSet<int>();
                    (int, int) edge;
                    do
                    {
                        edge = stack.Pop();
                        block.Add(edge.Item1);
                        block.Add(edge.Item2);
                    }
                    while (edge != (room, next));

                    blocks.Add(block);
                }
                else if (next != parent && value < order[room])
                {
                    stack.Push((room, next));
                    low[room] = Math.Min(low[room], value);
                }
            }
        }
    }

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
        return value ^ (value >> 31);
    }

    private List<int> FillComponent(int first, HashSet<int> seen)
    {
        var component = new List<int> { first };
        var queue = new Queue<int>([first]);
        while (queue.Count > 0)
        {
            foreach (int next in _neighbors[queue.Dequeue()])
            {
                if (seen.Add(next))
                {
                    component.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return component;
    }
}
