using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Provides the doors stage that lays the doors, end and walk back of a building whose phases, locks and open walls are given.
/// </summary>
public sealed class DoorStage
{
    private readonly Dictionary<BuildingFault, int> _faults = [];
    private HashSet<RoomPair> _contacts = [];

    /// <summary>
    /// Chooses the doors of a building from its given phases, locks and open walls.
    /// </summary>
    public DoorPlan Choose(BuildingPlan building)
    {
        _faults.Clear();
        var walkable = building.Rooms
            .Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room)
            .ToArray();

        Count(BuildingFault.RoomWithoutPhase, walkable.Count(room => room.Phase < 0));
        var nodes = walkable
            .Where(room => room.Phase >= 0)
            .Select(room => room.Id)
            .ToHashSet();

        var closed = building.ClosedWalls
            .Select(wall => new RoomPair(wall.A, wall.B))
            .ToHashSet();

        _contacts = building.Contacts
            .Where(contact => contact.A != contact.B && nodes.Contains(contact.A) && nodes.Contains(contact.B))
            .Select(contact => new RoomPair(contact.A, contact.B))
            .Where(contact => !closed.Contains(contact))
            .ToHashSet();

        int lastPhase = building.Rooms
            .Where(room => nodes.Contains(room.Id))
            .Select(room => room.Phase)
            .DefaultIfEmpty(0)
            .Max();

        var phases = Enumerable.Range(0, Math.Max(1, lastPhase + 1))
            .Select(_ => new HashSet<int>())
            .ToArray();

        foreach (var room in building.Rooms.Where(room => nodes.Contains(room.Id)))
        {
            phases[room.Phase].Add(room.Id);
        }

        var locks = building.Locks
            .Where(item => nodes.Contains(item.From) && nodes.Contains(item.To))
            .ToArray();

        var shortcuts = building.Shortcuts
            .Where(item => nodes.Contains(item.From) && nodes.Contains(item.To))
            .Select(item => new RoomPair(item.From, item.To))
            .ToArray();

        int[] keys = building.Keys
            .Where(nodes.Contains)
            .Distinct()
            .ToArray();

        var gate = ChooseGate(building, phases, keys);
        return Lay(building, gate, phases, locks, shortcuts, keys);
    }

    private static LevelStyle ChooseGate(BuildingPlan building, HashSet<int>[] phases, int[] keys)
    {
        if (building.Gate == LevelStyle.Blackout)
        {
            int? trigger = building.Trigger;
            bool layable = phases.Length == 3 && trigger is not null && phases[1].Contains(trigger.Value) && keys.Length > 0 && phases[1].Contains(keys[0]);
            return layable ? LevelStyle.Blackout : LevelStyle.Ungated;
        }

        if (building.Gate is LevelStyle.LockedDoors or LevelStyle.Fenced)
        {
            return keys.Any(phases[0].Contains) ? building.Gate : LevelStyle.Ungated;
        }

        return LevelStyle.Ungated;
    }

    private DoorPlan Lay(BuildingPlan building, LevelStyle gate, HashSet<int>[] phases, (int From, int To)[] locks, RoomPair[] shortcuts, int[] keys)
    {
        int start = building.Start;
        var doors = new Dictionary<RoomPair, DoorKind>();
        var phaseOf = new Dictionary<int, int>();
        for (int index = 0; index < phases.Length; index++)
        {
            foreach (int room in phases[index])
            {
                phaseOf[room] = index;
            }
        }

        foreach ((int from, int to) in locks)
        {
            doors[new RoomPair(from, to)] = DoorKind.Lock;
        }

        foreach (var pair in shortcuts)
        {
            doors[pair] = DoorKind.Shortcut;
        }

        // Lay a door in every other wall the building leaves open, locked when it leads into another phase
        foreach (var contact in _contacts.Order())
        {
            if (!doors.ContainsKey(contact) && phaseOf.TryGetValue(contact.A, out int phaseA) && phaseOf.TryGetValue(contact.B, out int phaseB))
            {
                doors[contact] = phaseA == phaseB ? DoorKind.Door : DoorKind.Lock;
            }
        }

        bool keyed = gate is LevelStyle.LockedDoors or LevelStyle.Fenced;
        var inner = new RoomGraph(phases[0], doors.Where(pair => pair.Value == DoorKind.Door).Select(pair => pair.Key));
        List<int> targets = [];
        if (keyed)
        {
            // Fetch the keys nearest first, the farthest key from the start last
            var fromStart = inner.Measure(start);
            int key = keys
                .Where(phases[0].Contains)
                .OrderByDescending(room => fromStart.GetValueOrDefault(room, -1))
                .ThenBy(room => room)
                .First();

            var left = keys
                .Where(room => room != key && phases[0].Contains(room))
                .ToList();

            int at = start;
            while (left.Count > 0)
            {
                var fromHere = inner.Measure(at);
                at = left
                    .OrderBy(room => fromHere.GetValueOrDefault(room, RoomGraph.Unreached))
                    .ThenBy(room => room)
                    .First();

                targets.Add(at);
                left.Remove(at);
            }

            targets.Add(key);
        }
        else if (gate == LevelStyle.Blackout)
        {
            targets = [building.Trigger.GetValueOrDefault(), keys[0]];
        }

        if (keyed || gate == LevelStyle.Blackout)
        {
            int keyedPhase = keyed ? 0 : 1;
            CountLocksOffPath(phases[keyedPhase], doors, locks, keyed ? targets.Prepend(start).ToArray() : [targets[0], targets[1]]);
        }

        if (gate == LevelStyle.Blackout)
        {
            // Count a dark run whose breaker is not its deepest room
            var depth = new RoomGraph(phases[1], doors.Keys).Measure(targets[0]);
            Count(BuildingFault.DarkRoomPastBreaker, depth.Count > 0 && (!depth.TryGetValue(targets[1], out int breakerDepth) || depth.Values.Max() > breakerDepth) ? 1 : 0);
        }

        var phaseSets = phases
            .Select(IReadOnlySet<int> (phase) => phase)
            .ToArray();

        int keyCount = keyed ? targets.Count : 1;
        targets.Add(PlaceEnd(building, gate, phaseSets, doors, start, targets, keyCount));
        var layout = new DoorLayout(gate, phaseSets, doors, start, targets, keyCount);
        var walkBack = layout.FindWalkBack();
        IReadOnlyList<int> returnPass = [];
        if (gate == LevelStyle.Blackout && walkBack is not null)
        {
            if (building.HasSites)
            {
                returnPass = walkBack.Rooms;
            }
            else if (walkBack.Recrosses)
            {
                Count(BuildingFault.CrossedBackWithoutSites);
            }
        }

        return new DoorPlan
        {
            Gate = gate,
            Phases = phaseSets,
            Doors = doors,
            Start = start,
            Targets = targets,
            KeyCount = keyCount,
            WalkBack = walkBack,
            ReturnPass = returnPass,
            Faults = new Dictionary<BuildingFault, int>(_faults)
        };
    }

    private static int PlaceEnd(BuildingPlan building, LevelStyle gate, IReadOnlySet<int>[] phases, Dictionary<RoomPair, DoorKind> doors, int start, List<int> targets,
        int keyCount)
    {
        int source = targets.Count == 0 ? start : targets[^1];
        var layout = new DoorLayout(gate, phases, doors, start, targets.Append(source).ToArray(), keyCount);
        var distance = layout.GetLegGraph(targets.Count).Measure(source);

        // End the level in a room, never in a hall or a passage
        int[] reached = phases[^1]
            .Where(room => distance.ContainsKey(room) && room != source)
            .Order()
            .ToArray();

        int[] rooms = reached
            .Where(room => building.Rooms[room].Kind == BuildingRoomKind.Room && building.Rooms[room].Type != RoomType.Passage)
            .ToArray();

        int[] last = rooms.Length > 0 ? rooms : reached;
        if (building.End is not null && last.Contains(building.End.Value))
        {
            return building.End.Value;
        }

        if (last.Length == 0)
        {
            return source;
        }

        // Where the building names no end, end in the last phase's room farthest from the source, the walk back crossing all it can
        int farthest = last.Max(room => distance[room]);
        return last.First(room => distance[room] == farthest);
    }

    // Counts each gate lock whose room lies more than one door off every shortest way along the stops from the phase's entrance to its last key
    private void CountLocksOffPath(HashSet<int> phase, Dictionary<RoomPair, DoorKind> doors, (int From, int To)[] locks, int[] stops)
    {
        var inside = new RoomGraph(phase, doors.Where(pair => pair.Value == DoorKind.Door).Select(pair => pair.Key));
        var way = new HashSet<int>();
        for (int leg = 1; leg < stops.Length; leg++)
        {
            var fromSource = inside.Measure(stops[leg - 1]);
            var fromTarget = inside.Measure(stops[leg]);
            int length = fromSource.GetValueOrDefault(stops[leg], -1);
            way.UnionWith(fromSource.Keys.Where(room => fromTarget.TryGetValue(room, out int back) && fromSource[room] + back == length));
        }

        var near = inside.Measure(way);
        Count(BuildingFault.LockSourceOffPath, locks.Count(item => phase.Contains(item.From) && near.GetValueOrDefault(item.From, int.MaxValue) > 1));
    }

    private void Count(BuildingFault fault, int amount = 1)
    {
        if (amount > 0)
        {
            _faults[fault] = _faults.GetValueOrDefault(fault) + amount;
        }
    }
}
