using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Provides the mission of a building from its plan and its chosen doors, with phases, keys, locks, seals, links and room roles.
/// </summary>
public sealed class MissionWriter
{
    /// <summary>
    /// Creates the mission of a building from the doors chosen for it.
    /// </summary>
    public static BuildingMission Write(DoorPlan plan)
    {
        var layout = new DoorLayout(plan.Gate, plan.Phases, plan.Doors, plan.Start, plan.Targets, plan.KeyCount);
        var walked = layout.TraceWalk();

        var spine = walked
            .Select(step => step.Room)
            .ToHashSet();

        var parent = new Dictionary<int, int>();
        for (int index = 1; index < walked.Count; index++)
        {
            if (walked[index].Room != plan.Start)
            {
                parent.TryAdd(walked[index].Room, walked[index - 1].Room);
            }
        }

        var walkable = new RoomGraph(layout.PhaseOf.Keys, plan.Doors.Where(pair => pair.Value != DoorKind.Shortcut).Select(pair => pair.Key));
        var queue = new Queue<int>(walked.Select(step => step.Room).Distinct());
        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            foreach (int next in walkable[room])
            {
                if (next != plan.Start && !spine.Contains(next) && parent.TryAdd(next, room))
                {
                    queue.Enqueue(next);
                }
            }
        }

        int? quietSource = null;
        int trigger = plan.Gate == LevelStyle.Blackout ? plan.Targets[0] : -1;
        if (plan.Gate == LevelStyle.Blackout)
        {
            quietSource = plan.Doors.Keys
                .Where(pair => pair.A == trigger || pair.B == trigger)
                .Select(pair => pair.A == trigger ? pair.B : pair.A)
                .Where(room => layout.PhaseOf[room] == 0)
                .Select(room => (int?)room)
                .FirstOrDefault();
        }

        var treeEdges = parent
            .Select(pair => new RoomPair(pair.Key, pair.Value))
            .ToHashSet();

        var links = new List<MissionLink>();
        var realized = new List<(int From, int To, MissionLinkKind Kind)>();
        foreach (((int a, int b), var kind) in plan.Doors.OrderBy(pair => pair.Key))
        {
            bool isTree = treeEdges.Contains(new RoomPair(a, b));
            (int from, int to) = isTree ? parent.GetValueOrDefault(b, -1) == a ? (a, b) : (b, a) : layout.PhaseOf[a] <= layout.PhaseOf[b] ? (a, b) : (b, a);
            bool isTriggerDoor = quietSource is not null && new RoomPair(quietSource.Value, trigger) == new RoomPair(a, b);
            var role = kind switch
            {
                DoorKind.Shortcut => MissionLinkKind.Loop,
                DoorKind.Lock when !isTree && !isTriggerDoor => MissionLinkKind.Entrance,
                _ => isTree || isTriggerDoor ? MissionLinkKind.Tree : MissionLinkKind.Loop
            };
            bool isLocked = kind == DoorKind.Shortcut || (kind == DoorKind.Lock && !isTriggerDoor);
            links.Add(new MissionLink(from, to, isLocked, role));
            realized.Add((from, to, role));
        }

        var roles = new Dictionary<int, MissionRole>();
        foreach (int room in layout.PhaseOf.Keys.Order())
        {
            roles[room] = spine.Contains(room) ? MissionRole.Path : MissionRole.Branch;
        }

        int[] keyRooms = plan.Gate switch
        {
            LevelStyle.Blackout => [plan.Targets[1]],
            LevelStyle.LockedDoors or LevelStyle.Fenced => plan.Targets.Take(plan.KeyCount).ToArray(),
            _ => []
        };

        foreach (int key in keyRooms)
        {
            roles[key] = MissionRole.Key;
        }

        roles[plan.End] = MissionRole.End;
        roles[plan.Start] = MissionRole.Start;
        var keys = new List<PhaseKey>();
        if (plan.Gate == LevelStyle.Blackout)
        {
            keys.Add(new PhaseKey(LandmarkKind.BlackoutTrigger, trigger));
        }

        var keyKind = plan.Gate == LevelStyle.Fenced ? LandmarkKind.Generator : LandmarkKind.Breaker;
        keys.AddRange(keyRooms.Select(room => new PhaseKey(keyKind, room)));
        var seals = new List<LevelSeal>();
        if (quietSource is not null)
        {
            seals.Add(new LevelSeal(SealKind.DoorsClose, trigger, trigger, quietSource.Value, 1));
        }

        var phases = CreatePhases(plan, walked, parent, spine, keyRooms);
        int[] flow = walked
            .Select(step => step.Room)
            .Concat(phases.SelectMany(phase => phase.Branches.SelectMany(branch => branch.Places)))
            .Distinct()
            .ToArray();

        int[] locks = links
            .Where(link => link.IsLocked && layout.PhaseOf[link.From] != layout.PhaseOf[link.To])
            .Select(link => link.To)
            .Distinct()
            .Order()
            .ToArray();

        var progression = new LevelProgression(phases, keys, locks, plan.End, flow, [], seals);
        int[] route = walked.Select(step => step.Room).ToArray();
        var chokeDoors = route
            .Zip(route.Skip(1), (from, to) => new RoomPair(from, to))
            .Where(pair => plan.Doors.ContainsKey(pair) && !Reaches(pair))
            .ToHashSet();

        return new BuildingMission
        {
            Mission = new MissionGraph(progression, links, roles),
            Parent = parent,
            Spine = spine,
            ChokeDoors = chokeDoors,
            End = plan.End,
            Realized = realized
        };

        bool Reaches(RoomPair skipped) => new RoomGraph(layout.PhaseOf.Keys, plan.Doors.Keys.Where(pair => pair != skipped))
            .Measure(plan.Start)
            .ContainsKey(plan.End);
    }

    private static LevelPhase[] CreatePhases(DoorPlan plan, List<(int Room, int Leg)> walked, Dictionary<int, int> parent, HashSet<int> spine,
        int[] keyRooms)
    {
        int keyedPhase = plan.Gate == LevelStyle.Blackout ? 1 : 0;
        var inner = plan.Doors
            .Where(pair => pair.Value == DoorKind.Door)
            .Select(pair => pair.Key)
            .ToArray();

        var phases = new LevelPhase[plan.Phases.Count];
        for (int index = 0; index < plan.Phases.Count; index++)
        {
            var places = plan.Phases[index];
            var path = new List<int>();
            foreach ((int room, _) in walked)
            {
                if (places.Contains(room) && (path.Count == 0 || path[^1] != room))
                {
                    path.Add(room);
                }
            }

            int entrance = index == 0 ? plan.Start : path.Count > 0 ? path[0] : places.DefaultIfEmpty(plan.Start).Min();
            bool hasKey = plan.Gate != LevelStyle.Ungated && index == keyedPhase;
            var back = hasKey && plan.WalkBack is not null ? plan.WalkBack.Rooms : [];

            int[] keys = keyRooms
                .Where(places.Contains)
                .ToArray();

            var revisits = hasKey ? plan.ReturnPass : [];
            phases[index] = new LevelPhase(index, places.Order().ToArray(), entrance, keys, path, back, CreateBranches(places, parent, spine),
                RoomGraph.CountCycles(places, inner), false, revisits, []);
        }

        return phases;
    }

    private static PhaseBranch[] CreateBranches(IReadOnlySet<int> places, Dictionary<int, int> parent, HashSet<int> spine)
    {
        var branches = new List<PhaseBranch>();

        var side = places
            .Where(room => !spine.Contains(room))
            .ToHashSet();

        var children = parent
            .Where(pair => side.Contains(pair.Key))
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).Order().ToArray());

        foreach (int root in side.Where(room => parent.TryGetValue(room, out int up) && !side.Contains(up)).Order())
        {
            var depths = new Dictionary<int, int> { [root] = 1 };
            var queue = new Queue<int>([root]);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                foreach (int child in children.GetValueOrDefault(room) ?? [])
                {
                    depths[child] = depths[room] + 1;
                    queue.Enqueue(child);
                }
            }

            branches.Add(new PhaseBranch(depths.Keys.Order().ToArray(), [parent[root]], depths, 0, false, 0));
        }

        return branches.ToArray();
    }
}
