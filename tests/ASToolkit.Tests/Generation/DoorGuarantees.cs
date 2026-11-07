using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Survey;

namespace ASToolkit.Tests.Generation;

internal static class DoorGuarantees
{
    public static List<string> Check(BuildingPlan building, DoorPlan plan)
    {
        var contacts = building.Contacts;
        var failures = new List<string>();
        var phaseOf = new Dictionary<int, int>();
        for (int index = 0; index < plan.Phases.Count; index++)
        {
            foreach (int room in plan.Phases[index])
            {
                phaseOf[room] = index;
            }
        }

        var rooms = phaseOf.Keys.ToHashSet();

        var contactSet = contacts
            .Select(pair => Order(pair.A, pair.B))
            .ToHashSet();

        if (Reach(rooms, plan.Doors.Keys.Select(pair => (pair.A, pair.B)), plan.Start).Count != rooms.Count)
        {
            failures.Add("room unreachable with every door open");
        }

        foreach (((int a, int b), var kind) in plan.Doors)
        {
            if (!contactSet.Contains((a, b)))
            {
                failures.Add($"door {a}-{b} off contact");
            }

            if (kind != DoorKind.Lock && phaseOf[a] != phaseOf[b])
            {
                failures.Add($"{kind} {a}-{b} across phases");
            }
        }

        int source = plan.Start;
        for (int leg = 0; leg < plan.Targets.Count; leg++)
        {
            var open = GetOpenDoors(plan, phaseOf, leg);
            var legRooms = GetLegRooms(plan, leg);
            var reached = Reach(legRooms, open, source);
            if (!reached.Contains(plan.Targets[leg]))
            {
                failures.Add($"target {plan.Targets[leg]} unreachable on leg {leg}");
            }

            var everywhere = Reach(rooms, open, source);
            var closed = GetClosedRooms(plan, leg);
            if (everywhere.Overlaps(closed))
            {
                failures.Add($"lock bypassed on leg {leg}");
            }

            source = plan.Targets[leg];
        }

        if (!plan.Faults.ContainsKey(BuildingFault.LockSourceOffPath))
        {
            failures.AddRange(CheckLocksSeen(plan, phaseOf, building.Locks));
        }

        if (plan.Gate != LevelStyle.Blackout || plan.Faults.ContainsKey(BuildingFault.DarkRoomPastBreaker))
            return failures;
        
        var dark = plan.Phases[1].ToHashSet();
        var depth = Measure(Connect(dark, plan.Doors.Keys.Select(pair => (pair.A, pair.B))), [plan.Targets[0]]);
        if (depth.Values.Max() > depth[plan.Targets[1]])
        {
            failures.Add("dark room deeper than the breaker");
        }
        
        return failures;
    }

    private static IEnumerable<string> CheckLocksSeen(DoorPlan plan, Dictionary<int, int> phaseOf, IReadOnlyList<(int From, int To)> given)
    {
        if (plan.Gate == LevelStyle.Ungated)
        {
            yield break;
        }

        int[] keyLegs = plan.Gate == LevelStyle.Blackout ? [1] : Enumerable.Range(0, plan.KeyCount).ToArray();
        var near = new HashSet<int>();
        int source = plan.Start;
        for (int leg = 0; leg <= keyLegs[^1]; leg++)
        {
            var open = GetOpenDoors(plan, phaseOf, leg);
            var graph = Connect(GetLegRooms(plan, leg), open);
            if (keyLegs.Contains(leg))
            {
                var fromSource = Measure(graph, [source]);
                var fromTarget = Measure(graph, [plan.Targets[leg]]);
                int length = fromSource.GetValueOrDefault(plan.Targets[leg], -1);

                int[] path = fromSource.Keys
                    .Where(room => fromTarget.TryGetValue(room, out int back) && fromSource[room] + back == length)
                    .ToArray();

                near.UnionWith(Measure(graph, path).Where(pair => pair.Value <= 1).Select(pair => pair.Key));
            }

            source = plan.Targets[leg];
        }

        int seenPhase = plan.Gate == LevelStyle.Blackout ? 1 : 0;
        foreach (((int a, int b), var kind) in plan.Doors)
        {
            // Check the building's gate locks; a wall the building leaves open between phases is locked as any door is
            if (kind != DoorKind.Lock || Math.Min(phaseOf[a], phaseOf[b]) != seenPhase || !given.Any(pair => (pair.From == a && pair.To == b) || (pair.From == b && pair.To == a)))
            {
                continue;
            }

            int lockSource = phaseOf[a] < phaseOf[b] ? a : b;
            if (!near.Contains(lockSource))
            {
                yield return $"lock {a}-{b} not seen before its key";
            }
        }
    }

    private static HashSet<int> GetClosedRooms(DoorPlan plan, int leg)
    {
        if (plan.Gate == LevelStyle.Blackout)
        {
            return leg < 2 ? plan.Phases[2].ToHashSet() : [];
        }

        return plan.Gate != LevelStyle.Ungated && leg < plan.KeyCount ? plan.Phases.Skip(1).SelectMany(phase => phase).ToHashSet() : [];
    }

    private static List<(int A, int B)> GetOpenDoors(DoorPlan plan, Dictionary<int, int> phaseOf, int leg)
    {
        var open = new List<(int A, int B)>();
        foreach (((int a, int b), var kind) in plan.Doors)
        {
            int low = Math.Min(phaseOf[a], phaseOf[b]);
            int high = Math.Max(phaseOf[a], phaseOf[b]);
            bool isOpen = plan.Gate switch
            {
                LevelStyle.LockedDoors or LevelStyle.Fenced => leg >= plan.KeyCount || kind == DoorKind.Door,
                LevelStyle.Blackout => leg switch
                {
                    0 => high == 0 || (low == 0 && high == 1),
                    1 => low == 1 && high == 1 && kind != DoorKind.Shortcut,
                    _ => low != 0
                },
                _ => true
            };
            if (isOpen)
            {
                open.Add((a, b));
            }
        }

        return open;
    }

    private static HashSet<int> GetLegRooms(DoorPlan plan, int leg)
    {
        if (plan.Gate == LevelStyle.Blackout)
        {
            return leg switch
            {
                0 => plan.Phases[0].Union(plan.Phases[1]).ToHashSet(),
                1 => plan.Phases[1].ToHashSet(),
                _ => plan.Phases[1].Union(plan.Phases[2]).ToHashSet()
            };
        }

        return plan.Gate != LevelStyle.Ungated && leg < plan.KeyCount ? plan.Phases[0].ToHashSet() : plan.Phases.SelectMany(phase => phase).ToHashSet();
    }

    private static HashSet<int> Reach(HashSet<int> rooms, IEnumerable<(int A, int B)> doors, int source) =>
        rooms.Contains(source) ? Measure(Connect(rooms, doors), [source]).Keys.ToHashSet() : [];

    private static Dictionary<int, List<int>> Connect(IReadOnlySet<int> rooms, IEnumerable<(int A, int B)> doors)
    {
        var graph = rooms.ToDictionary(room => room, _ => new List<int>());
        foreach ((int a, int b) in doors)
        {
            if (!graph.ContainsKey(a) || !graph.ContainsKey(b))
                continue;
            
            graph[a].Add(b);
            graph[b].Add(a);
        }

        return graph;
    }

    private static Dictionary<int, int> Measure(Dictionary<int, List<int>> graph, IEnumerable<int> sources)
    {
        var distance = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (int source in sources.Where(graph.ContainsKey))
        {
            distance[source] = 0;
            queue.Enqueue(source);
        }

        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            foreach (int next in graph[room])
            {
                if (distance.TryAdd(next, distance[room] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return distance;
    }

    private static (int, int) Order(int a, int b) => a < b ? (a, b) : (b, a);
}
