using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Survey;

namespace ASToolkit.Tests.Generation;

internal static class BuildingChecks
{
    public static List<string> Find(BuildingPlan building)
    {
        var failures = new List<string>();
        var nodes = building.Rooms
            .Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room)
            .ToDictionary(room => room.Id, room => room.Phase);

        var closed = building.ClosedWalls
            .Select(wall => Order(wall.A, wall.B))
            .ToHashSet();

        var contacts = building.Contacts
            .Where(contact => contact.A != contact.B && nodes.ContainsKey(contact.A) && nodes.ContainsKey(contact.B))
            .Select(contact => Order(contact.A, contact.B))
            .Where(contact => !closed.Contains(contact))
            .ToHashSet();

        failures.AddRange(nodes.Where(pair => pair.Value < 0).Select(pair => $"room {pair.Key} without a phase"));
        int count = nodes.Values.DefaultIfEmpty(0).Max() + 1;
        var phases = Enumerable.Range(0, count)
            .Select(phase => nodes.Where(pair => pair.Value == phase).Select(pair => pair.Key).ToHashSet())
            .ToArray();

        var locks = building.Locks
            .Where(item => nodes.ContainsKey(item.From) && nodes.ContainsKey(item.To))
            .ToArray();

        foreach ((int from, int to) in locks)
        {
            if (!contacts.Contains(Order(from, to)))
            {
                failures.Add($"lock {from}-{to} on no open wall");
            }

            if (nodes[from] < 0 || nodes[from] >= nodes[to])
            {
                failures.Add($"lock {from}-{to} not forward");
            }
        }

        var entered = locks
            .Select(item => item.To)
            .ToHashSet();

        if (building is { Gate: LevelStyle.Blackout, Trigger: not null })
        {
            entered.Add(building.Trigger.Value);
        }

        for (int index = 0; index < phases.Length; index++)
        {
            foreach (var piece in FindPieces(phases[index], contacts))
            {
                if (!(index == 0 && piece.Contains(building.Start)) && !piece.Overlaps(entered))
                {
                    failures.Add($"phase {index} piece of {piece.Count} rooms entered by no lock");
                }
            }
        }

        var keyPhase = phases[building.Gate == LevelStyle.Blackout && phases.Length > 1 ? 1 : 0];
        foreach ((int from, int to) in building.Shortcuts.Where(item => nodes.ContainsKey(item.From) && nodes.ContainsKey(item.To)))
        {
            if (!contacts.Contains(Order(from, to)))
            {
                failures.Add($"shortcut {from}-{to} on no open wall");
            }

            if (!keyPhase.Contains(from) || !keyPhase.Contains(to))
            {
                failures.Add($"shortcut {from}-{to} outside its key's phase");
            }
        }

        int wanted = building.Gate switch
        {
            LevelStyle.Ungated => 1,
            LevelStyle.Blackout => 3,
            _ => 2
        };

        if (phases.Length != wanted)
        {
            failures.Add($"{phases.Length} phases for {building.Gate}");
        }

        if (!phases[0].Contains(building.Start))
        {
            failures.Add("start outside the first phase");
        }

        int[] keys = building.Keys
            .Where(nodes.ContainsKey)
            .ToArray();

        if (building.Gate is LevelStyle.LockedDoors or LevelStyle.Fenced && (keys.Length == 0 || keys.Any(key => !phases[0].Contains(key))))
        {
            failures.Add("key missing or outside the first phase");
        }

        if (building.Gate == LevelStyle.Blackout && phases.Length == 3)
        {
            int? trigger = building.Trigger;
            if (trigger is null || !phases[1].Contains(trigger.Value) || keys.Length == 0 || !phases[1].Contains(keys[0]) || keys[0] == trigger.Value)
            {
                failures.Add("blackout trigger or breaker outside the blackout phase");
            }

            if (trigger is null || !contacts.Any(pair => (pair.A == trigger.Value && phases[0].Contains(pair.B)) || (pair.B == trigger.Value && phases[0].Contains(pair.A))))
            {
                failures.Add("blackout trigger touching no quiet room");
            }
        }

        return failures;
    }

    private static List<HashSet<int>> FindPieces(HashSet<int> rooms, HashSet<(int A, int B)> contacts)
    {
        var pieces = new List<HashSet<int>>();
        var left = new HashSet<int>(rooms);
        while (left.Count > 0)
        {
            var piece = new HashSet<int> { left.First() };
            var todo = new Stack<int>(piece);
            while (todo.Count > 0)
            {
                int room = todo.Pop();
                foreach ((int a, int b) in contacts.Where(pair => pair.A == room || pair.B == room))
                {
                    int next = a == room ? b : a;
                    if (left.Contains(next) && piece.Add(next))
                    {
                        todo.Push(next);
                    }
                }
            }

            left.ExceptWith(piece);
            pieces.Add(piece);
        }

        return pieces;
    }

    private static (int A, int B) Order(int a, int b) => a < b ? (a, b) : (b, a);
}
