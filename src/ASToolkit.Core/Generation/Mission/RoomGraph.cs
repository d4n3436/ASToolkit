using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Mission;

// Holds rooms joined by doors or contacts, each with its neighbors in ascending order
internal sealed class RoomGraph
{
    public const int Unreached = 99;

    private readonly Dictionary<int, List<int>> _neighbors;

    public RoomGraph(IEnumerable<int> nodes, IEnumerable<RoomPair> edges)
    {
        _neighbors = new Dictionary<int, List<int>>();
        foreach (int node in nodes)
        {
            _neighbors[node] = [];
        }

        foreach ((int a, int b) in edges)
        {
            if (_neighbors.TryGetValue(a, out var fromA) && _neighbors.TryGetValue(b, out var fromB))
            {
                fromA.Add(b);
                fromB.Add(a);
            }
        }

        foreach (var neighbors in _neighbors.Values)
        {
            neighbors.Sort();
        }
    }

    public List<int> this[int room] => _neighbors[room];

    public static int CountCycles(IReadOnlySet<int> nodes, IEnumerable<RoomPair> edges)
    {
        var inside = edges
            .Where(edge => nodes.Contains(edge.A) && nodes.Contains(edge.B))
            .ToArray();

        return inside.Length - nodes.Count + new RoomGraph(nodes, inside).FindComponents(nodes).Count;
    }

    public Dictionary<int, int> Measure(int source) => Measure([source]);

    public Dictionary<int, int> Measure(IEnumerable<int> sources) => _neighbors.MeasureHops(sources);

    public List<HashSet<int>> FindComponents(IReadOnlySet<int> nodes) => _neighbors.FindComponents(nodes);

    public HashSet<int> FindShortestPathRooms(int source, int target)
    {
        var fromSource = Measure(source);
        var fromTarget = Measure(target);
        if (!fromSource.TryGetValue(target, out int length))
        {
            return [];
        }

        return fromSource.Keys
            .Where(room => fromTarget.TryGetValue(room, out int back) && fromSource[room] + back == length)
            .ToHashSet();
    }
}
