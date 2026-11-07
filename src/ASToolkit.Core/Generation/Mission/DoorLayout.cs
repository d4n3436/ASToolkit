using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Mission;

// Walks a level's doors state by state, leg i holding while the player walks to target i
internal sealed class DoorLayout
{
    public DoorLayout(LevelStyle gate, IReadOnlyList<IReadOnlySet<int>> phases, IReadOnlyDictionary<RoomPair, DoorKind> doors, int start, IReadOnlyList<int> targets,
        int keyCount)
    {
        Gate = gate;
        Phases = phases;
        Doors = doors;
        Start = start;
        Targets = targets;
        KeyCount = keyCount;
        PhaseOf = new Dictionary<int, int>();
        for (int index = 0; index < phases.Count; index++)
        {
            foreach (int room in phases[index])
            {
                PhaseOf[room] = index;
            }
        }
    }

    public LevelStyle Gate { get; }

    public IReadOnlyList<IReadOnlySet<int>> Phases { get; }

    public IReadOnlyDictionary<RoomPair, DoorKind> Doors { get; }

    public int Start { get; }

    public IReadOnlyList<int> Targets { get; }

    public int KeyCount { get; }

    public Dictionary<int, int> PhaseOf { get; }

    public bool IsKeyed => Gate is LevelStyle.LockedDoors or LevelStyle.Fenced;

    public IEnumerable<RoomPair> GetOpenDoors(int leg)
    {
        foreach (((int a, int b), var kind) in Doors)
        {
            int phaseA = PhaseOf[a];
            int phaseB = PhaseOf[b];
            if (IsKeyed && leg < KeyCount && kind != DoorKind.Door)
            {
                continue;
            }

            if (Gate == LevelStyle.Blackout)
            {
                bool open = leg switch
                {
                    0 => (phaseA == 0 && phaseB == 0) || (Math.Min(phaseA, phaseB) == 0 && Math.Max(phaseA, phaseB) == 1),
                    1 => phaseA == 1 && phaseB == 1,
                    _ => phaseA != 0 && phaseB != 0
                };
                if (!open)
                {
                    continue;
                }
            }

            yield return new RoomPair(a, b);
        }
    }

    public IReadOnlySet<int> GetLegRooms(int leg)
    {
        if (Gate == LevelStyle.Blackout)
        {
            return leg switch
            {
                0 => Phases[0].Union(Phases[1]).ToHashSet(),
                1 => Phases[1],
                _ => Phases[1].Union(Phases[2]).ToHashSet()
            };
        }

        return IsKeyed && leg < KeyCount ? Phases[0] : Phases.SelectMany(phase => phase).ToHashSet();
    }

    public RoomGraph GetLegGraph(int leg) => new(GetLegRooms(leg), GetOpenDoors(leg));

    public List<DoorLeg> Walk()
    {
        var legs = new List<DoorLeg>();
        int source = Start;
        for (int leg = 0; leg < Targets.Count; leg++)
        {
            int target = Targets[leg];
            var graph = GetLegGraph(leg);
            var distance = graph.Measure(source);
            int? length = distance.TryGetValue(target, out int found) ? found : null;
            legs.Add(new DoorLeg(source, target, length, graph.FindShortestPathRooms(source, target), graph));
            source = target;
        }

        return legs;
    }

    // Walks each leg on one of its shortest paths, taking unvisited rooms first and then the smaller room, and stops at a target the leg cannot
    // reach
    public List<(int Room, int Leg)> TraceWalk()
    {
        var walked = new List<(int Room, int Leg)> { (Start, 0) };
        var seen = new HashSet<int> { Start };
        int at = Start;
        for (int leg = 0; leg < Targets.Count; leg++)
        {
            var graph = GetLegGraph(leg);
            var toTarget = graph.Measure(Targets[leg]);
            if (!toTarget.ContainsKey(at))
            {
                break;
            }

            while (at != Targets[leg])
            {
                int step = at;
                at = graph[step]
                    .Where(room => toTarget.TryGetValue(room, out int left) && left == toTarget[step] - 1)
                    .OrderBy(room => seen.Contains(room) ? 1 : 0)
                    .ThenBy(room => room)
                    .First();

                seen.Add(at);
                walked.Add((at, leg));
            }
        }

        return walked;
    }

    // Walks from the last key to the first room of a later phase on the shortest ways to the end, or to the end when it lies in the key's phase
    public WalkBack? FindWalkBack()
    {
        if (Gate == LevelStyle.Ungated)
        {
            return null;
        }

        int keyLeg = Gate == LevelStyle.Blackout ? 1 : KeyCount - 1;
        var legs = Walk();
        int key = legs[keyLeg].Target;
        int end = Targets[^1];
        var graph = legs[keyLeg + 1].Graph;
        var fromKey = graph.Measure(key);
        var fromEnd = graph.Measure(end);
        if (!fromKey.TryGetValue(end, out int length))
        {
            return null;
        }

        var later = Phases
            .Skip(Gate == LevelStyle.Blackout ? 2 : 1)
            .SelectMany(phase => phase)
            .ToHashSet();

        int[] way = fromKey.Keys
            .Where(room => fromEnd.TryGetValue(room, out int back) && fromKey[room] + back == length)
            .OrderBy(room => fromKey[room])
            .ThenBy(room => room)
            .ToArray();

        int gateRoom = way
            .Where(later.Contains)
            .DefaultIfEmpty(end)
            .First();

        int[] rooms = way
            .Where(room => fromKey[room] < fromKey[gateRoom])
            .ToArray();

        var forward = legs
            .Take(keyLeg + 1)
            .SelectMany(leg => leg.Path)
            .Where(room => room != key)
            .ToHashSet();

        bool recrosses = rooms.Length > 1 && rooms[1..^1].Any(forward.Contains);
        return new WalkBack(fromKey[gateRoom], rooms, recrosses);
    }
}
