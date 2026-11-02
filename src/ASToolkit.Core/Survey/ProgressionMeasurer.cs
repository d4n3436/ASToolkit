using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the progression of a surveyed level, one phase per state between the keys that change it with its critical path, branches and loops,
/// and the spawn triggers and the ways back the triggers close.
/// </summary>
public sealed class ProgressionMeasurer
{
    private const float MaxDeadEndShare = 0.05f;
    private const int Unvisited = int.MaxValue;

    private static readonly ItemCategory[] Rewards = [ItemCategory.Health, ItemCategory.Armor, ItemCategory.Ammo, ItemCategory.Money, ItemCategory.Implant, ItemCategory.Bonus, ItemCategory.Weapon];

    private readonly LevelSurvey _survey;
    private readonly IReadOnlyDictionary<int, HashSet<int>> _neighbors;
    private readonly int[] _rooms;
    private readonly int[] _route;
    private readonly Dictionary<int, int> _firstVisit = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionMeasurer"/> class.
    /// </summary>
    /// <param name="survey">The surveyed level to read the progression of.</param>
    public ProgressionMeasurer(LevelSurvey survey)
    {
        _survey = survey;
        var graph = DoorGraph.Create(survey);
        _neighbors = graph.Neighbors;
        _rooms = graph.Rooms.ToArray();

        _route = survey.Route
            .Select(visit => visit.Place)
            .Where(_neighbors.ContainsKey)
            .ToArray();

        for (int index = 0; index < _route.Length; index++)
        {
            _firstVisit.TryAdd(_route[index], index);
        }
    }

    /// <summary>
    /// Returns the progression of the level.
    /// </summary>
    public LevelProgression Measure()
    {
        var phases = FindPhases();
        int end = _survey.Landmarks.FirstOrDefault(landmark => landmark.Kind is LandmarkKind.ExitTrigger or LandmarkKind.RouteEnd && _neighbors.ContainsKey(landmark.Place))?.Place ?? _route[^1];
        var passes = SplitReturnPasses(phases, end);

        var keys = _survey.Landmarks
            .Where(landmark => landmark.Kind is LandmarkKind.Breaker or LandmarkKind.Generator or LandmarkKind.BlackoutTrigger && _neighbors.ContainsKey(landmark.Place))
            .Select(landmark => new PhaseKey(landmark.Kind, landmark.Place))
            .ToArray();

        var built = new List<LevelPhase>();
        for (int index = 0; index < passes.Count; index++)
        {
            built.Add(passes[index].IsReturnPass
                ? BuildReturnPass(index, passes, end)
                : BuildPhase(index, passes, end, keys));
        }

        int[] locks = _survey.Gates
            .SelectMany(gate => gate.GatePlaces)
            .Where(_neighbors.ContainsKey)
            .Distinct()
            .Order()
            .ToArray();

        var triggers = _survey.Encounters
            .Where(encounter => encounter.Kind is EncounterKind.Ambush or EncounterKind.Spawner && encounter.TriggerPlace is not null && _neighbors.ContainsKey(encounter.TriggerPlace.Value))
            .Select(encounter => new PhaseTrigger(encounter.TriggerPlace!.Value, encounter.Place, encounter.Source, encounter.HitPoints))
            .ToArray();

        int[] flow = _firstVisit.Keys.OrderBy(place => _firstVisit[place]).ToArray();
        var progression = new LevelProgression(built, keys, locks, end, flow, triggers, []);
        return progression with { Seals = FindSeals(progression) };
    }

    private List<HashSet<int>> FindPhases()
    {
        bool gated = _rooms
            .Select(room => GetPlace(room).Zone)
            .Where(zone => zone != GateZone.Secret)
            .Distinct()
            .Count() > 1;

        var phases = gated ? FindStateParts() : [_rooms.ToHashSet()];
        if (gated)
        {
            var first = phases.First(part => part.Contains(_route[0]));
            int? following = _route.Where(place => !first.Contains(place)).Select(place => (int?)place).FirstOrDefault();
            if (following is not null && first.Count == 1)
            {
                phases.First(part => part.Contains(following.Value)).UnionWith(first);
                phases.Remove(first);
            }
        }

        int start = _survey.Route[0].Place;

        var isolated = phases
            .Where(part => !part.Any(place => _neighbors[place].Any(neighbor => !part.Contains(neighbor))) && !part.Contains(start))
            .ToArray();

        foreach (var part in isolated)
        {
            if (part.Any(_firstVisit.ContainsKey))
            {
                continue;
            }

            int[] others = phases.Where(other => other != part).SelectMany(other => other).ToArray();
            int nearest = others.MinBy(place => part.Min(room => GetSquaredDistance(place, room)));
            phases.First(other => other.Contains(nearest)).UnionWith(part);
            part.Clear();
        }

        phases.RemoveAll(part => part.Count == 0);
        return phases;
    }

    // Group zone parts by where the tour enters them, fold dead ends into their host, then hidden rooms into the part their wall opens from
    private List<HashSet<int>> FindStateParts()
    {
        int start = _route[0];
        int end = _route[^1];
        var zone = _rooms.ToDictionary(room => room, GetOpenZone);
        int floor = _rooms.Sum(room => GetPlace(room).Cells.Count);

        var parts = _rooms
            .Where(room => room != start)
            .GroupBy(room => zone[room])
            .OrderBy(group => group.Key)
            .SelectMany(group => FindComponents(group.ToHashSet()))
            .ToList();

        parts.Add([start]);
        var owner = new Dictionary<int, int>();
        for (int index = 0; index < parts.Count; index++)
        {
            foreach (int place in parts[index])
            {
                owner[place] = index;
            }
        }

        var entered = new Dictionary<int, int>();
        for (int index = 1; index < _route.Length; index++)
        {
            int part = owner[_route[index]];
            if (!entered.ContainsKey(part) && owner[_route[index - 1]] != part)
            {
                entered[part] = owner[_route[index - 1]];
            }
        }

        for (int index = 0; index < parts.Count; index++)
        {
            if (entered.ContainsKey(index) || parts[index].Contains(start))
            {
                continue;
            }

            int[] outside = parts[index]
                .SelectMany(place => _neighbors[place])
                .Select(neighbor => owner[neighbor])
                .Where(other => other != index)
                .ToArray();

            if (outside.Length > 0)
            {
                entered[index] = outside.MinBy(other => parts[other].Min(GetFirstVisit));
            }
        }

        int[] root = Enumerable.Range(0, parts.Count).ToArray();
        var groups = new Dictionary<(GateZone Zone, int From), List<int>>();
        for (int index = 0; index < parts.Count; index++)
        {
            if (!parts[index].Contains(start) && entered.TryGetValue(index, out int from))
            {
                var key = (zone[parts[index].Min()], from);
                if (!groups.TryGetValue(key, out var members))
                {
                    members = [];
                    groups[key] = members;
                }

                members.Add(index);
            }
        }

        foreach (var members in groups.Values)
        {
            foreach (int member in members.Skip(1))
            {
                root[Find(member)] = Find(members[0]);
            }
        }

        var merged = new Dictionary<int, HashSet<int>>();
        for (int index = 0; index < parts.Count; index++)
        {
            int group = Find(index);
            if (!merged.TryGetValue(group, out var places))
            {
                places = [];
                merged[group] = places;
            }

            places.UnionWith(parts[index]);
        }

        foreach ((int index, var part) in merged.ToArray())
        {
            if (part.Count == 0 || part.Contains(start) || part.Contains(end) || !entered.TryGetValue(index, out int from))
            {
                continue;
            }

            var around = part
                .SelectMany(place => _neighbors[place])
                .Select(neighbor => Find(owner[neighbor]))
                .Where(other => other != index)
                .ToHashSet();

            int host = Find(from);
            bool closes = zone[part.Min()] == GateZone.Blackout;
            if (around.Count == 1 && around.Contains(host) && (closes || part.Sum(place => GetPlace(place).Cells.Count) / (float)floor < MaxDeadEndShare))
            {
                merged[host].UnionWith(part);
                for (int other = 0; other < root.Length; other++)
                {
                    if (Find(other) == index)
                    {
                        root[other] = host;
                    }
                }

                merged[index] = [];
            }
        }

        var states = merged.Values.Where(part => part.Count > 0).ToList();

        var hidden = states
            .Where(part => part.All(place => GetPlace(place).Zone == GateZone.Secret))
            .ToArray();

        foreach (var part in hidden)
        {
            int[] walls = _survey.Connections
                .Where(connection => part.Contains(connection.A) != part.Contains(connection.B))
                .Select(connection => part.Contains(connection.A) ? connection.B : connection.A)
                .ToArray();

            var host = states.FirstOrDefault(other => other != part && !hidden.Contains(other) && walls.Any(other.Contains));
            if (host is not null)
            {
                host.UnionWith(part);
                states.Remove(part);
            }
        }

        return states.OrderBy(part => part.Min(GetFirstVisit)).ToList();

        int Find(int index)
        {
            while (root[index] != index)
            {
                index = root[index];
            }

            return index;
        }
    }

    // Split off the rooms a key opens inside its own phase when objectives lie there, as a return pass after the part the key is reached in
    private List<PhaseDraft> SplitReturnPasses(List<HashSet<int>> phases, int end)
    {
        var locked = _survey.Connections
            .Where(connection => connection.Kind is ConnectionKind.LockedDoor or ConnectionKind.Fence)
            .Select(connection => (Math.Min(connection.A, connection.B), Math.Max(connection.A, connection.B)))
            .ToHashSet();

        var drafts = new List<PhaseDraft>();
        foreach (var part in phases)
        {
            int? key = _survey.Landmarks.FirstOrDefault(landmark => landmark.Kind is LandmarkKind.Breaker or LandmarkKind.Generator && part.Contains(landmark.Place))?.Place;
            int[] sites = FindLandmarks(part, LandmarkKind.DynamiteSite, LandmarkKind.Boss);
            int[] charges = FindLandmarks(part, LandmarkKind.DynamiteCharges);
            if (key is null || (sites.Length == 0 && !part.Contains(end)))
            {
                drafts.Add(new PhaseDraft(part, false, 0, [], []));
                continue;
            }

            var region = FillComponent(key.Value, place => part.Contains(place), (from, to) => !locked.Contains((Math.Min(from, to), Math.Max(from, to))));
            var opened = part.Where(place => !region.Contains(place)).ToHashSet();
            if (opened.Count == 0)
            {
                drafts.Add(new PhaseDraft(part, false, 0, [], []));
                continue;
            }

            drafts.Add(new PhaseDraft(region, false, 0, [], []));
            drafts.Add(new PhaseDraft(opened, true, key.Value, sites, charges));
        }

        return drafts;
    }

    private LevelPhase BuildReturnPass(int index, List<PhaseDraft> passes, int end)
    {
        var draft = passes[index];
        var reachable = GetUnion(passes, index);
        var path = new List<int> { draft.Key };
        foreach (int charge in draft.Charges.Where(charge => charge != path[^1]))
        {
            Walk(path, reachable, charge);
        }

        var todo = draft.Sites.Where(site => site != draft.Key).ToList();
        while (todo.Count > 0)
        {
            var lengths = MeasureDistances([path[^1]], reachable);
            int nearest = todo.MinBy(place => lengths.GetValueOrDefault(place, Unvisited));
            Walk(path, reachable, nearest);
            todo.Remove(nearest);
        }

        if (reachable.Contains(end) && end != path[^1])
        {
            Walk(path, reachable, end);
        }

        int[] revisits = path
            .Where(place => !draft.Places.Contains(place))
            .Distinct()
            .Order()
            .ToArray();

        return new LevelPhase(index, draft.Places.Order().ToArray(), draft.Key, [], path.ToArray(), [], FindBranches(draft.Places, path), 0, true, revisits, draft.Sites);
    }

    private LevelPhase BuildPhase(int index, List<PhaseDraft> passes, int end, PhaseKey[] keys)
    {
        var part = passes[index].Places;
        bool returnsNext = index + 1 < passes.Count && passes[index + 1].IsReturnPass;
        int entrance = _route.Where(part.Contains).Select(place => (int?)place).FirstOrDefault() ?? part.Min();

        var ownKeys = keys
            .Select(key => key.Place)
            .Where(part.Contains)
            .Distinct()
            .OrderBy(GetFirstVisit)
            .ThenBy(place => place)
            .ToList();

        var targets = new List<int>(ownKeys);
        if (part.Contains(end) || returnsNext)
        {
            if (part.Contains(end) && !returnsNext)
            {
                targets.Add(end);
            }
        }
        else
        {
            var later = passes.Skip(index + 1).SelectMany(pass => pass.Places).ToHashSet();
            for (int step = 1; step < _route.Length; step++)
            {
                if (later.Contains(_route[step]) && part.Contains(_route[step - 1]))
                {
                    targets.Add(_route[step - 1]);
                    break;
                }
            }
        }

        var reachable = GetUnion(passes, index);
        var path = new List<int> { entrance };
        bool hasCharges = _survey.Landmarks.All(landmark => landmark.Kind != LandmarkKind.DynamiteCharges)
                          || _survey.Landmarks.Any(landmark => landmark.Kind == LandmarkKind.DynamiteCharges && reachable.Contains(landmark.Place));

        int[] charges = FindLandmarks(part, LandmarkKind.DynamiteCharges);

        int[] objectives = _survey.Landmarks
            .Where(landmark => (landmark.Kind == LandmarkKind.Boss || (landmark.Kind == LandmarkKind.DynamiteSite && hasCharges)) && part.Contains(landmark.Place))
            .Select(landmark => landmark.Place)
            .ToArray();

        foreach (int[] group in new[] { charges, objectives })
        {
            var todo = group.Where(reachable.Contains).ToList();
            while (todo.Count > 0)
            {
                var lengths = MeasureDistances([path[^1]], reachable);
                int nearest = todo.MinBy(place => lengths.GetValueOrDefault(place, Unvisited));
                todo.Remove(nearest);
                if (lengths.ContainsKey(nearest) && nearest != path[^1])
                {
                    Walk(path, reachable, nearest);
                }
            }
        }

        foreach (int target in targets.Where(reachable.Contains))
        {
            Walk(path, reachable, target);
        }

        int last = ownKeys.Count > 0 ? path.LastIndexOf(ownKeys[^1]) : -1;
        int[] back = last >= 0 && !part.Contains(end) ? path.Skip(last + 1).ToArray() : [];
        int edges = part.Sum(place => _neighbors[place].Count(part.Contains)) / 2;
        int loops = edges - part.Count + FindComponents(part).Count;
        return new LevelPhase(index, part.Order().ToArray(), entrance, ownKeys.ToArray(), path.ToArray(), back, FindBranches(part, path), loops, false, [], []);
    }

    private PhaseBranch[] FindBranches(HashSet<int> part, List<int> path)
    {
        var onPath = path.ToHashSet();
        var branches = new List<PhaseBranch>();
        foreach (var component in FindComponents(part.Where(place => !onPath.Contains(place)).ToHashSet()))
        {
            var anchors = component.SelectMany(place => _neighbors[place]).Where(onPath.Contains).ToHashSet();
            if (anchors.Count == 0)
            {
                anchors = component.SelectMany(place => _neighbors[place]).Where(neighbor => !component.Contains(neighbor)).ToHashSet();
            }

            var within = component.Concat(anchors).ToHashSet();

            var depths = MeasureDistances(anchors, within)
                .Where(entry => component.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            int rewards = _survey.Items.Count(item => component.Contains(item.Place) && item.Category is not null && Rewards.Contains(item.Category.Value));
            int threat = _survey.Encounters.Where(encounter => component.Contains(encounter.Place)).Sum(encounter => encounter.HitPoints);
            branches.Add(new PhaseBranch(component.Order().ToArray(), anchors.Order().ToArray(), depths, rewards, component.Any(_firstVisit.ContainsKey), threat));
        }

        return branches.ToArray();
    }

    private LevelSeal[] FindSeals(LevelProgression progression)
    {
        var seals = new List<LevelSeal>();
        var all = _rooms.ToHashSet();
        var firstRooms = progression.Phases.Count > 0 ? progression.Phases[0].Places : [];

        var rubble = _survey.Builds
            .Where(build => build.Id == KnownObject.RubblePile && _neighbors.ContainsKey(build.Place))
            .Select(build => (build.Trigger, build.Place))
            .Distinct()
            .ToArray();

        foreach ((int trigger, int spot) in rubble)
        {
            var lengths = MeasureDistances([spot], all);
            int target = firstRooms.MinBy(place => lengths.GetValueOrDefault(place, Unvisited));
            var back = FindPath(spot, target, all);
            seals.Add(back.Count > 1
                ? new LevelSeal(SealKind.Rubble, trigger, back[0], back[1], progression.GetPhase(trigger))
                : new LevelSeal(SealKind.Rubble, trigger, spot, spot, progression.GetPhase(trigger)));
        }

        foreach (var gate in _survey.Gates.Where(gate => gate is { Kind: LevelStyle.Blackout, TriggerPlace: not null } && _neighbors.ContainsKey(gate.TriggerPlace.Value)))
        {
            int trigger = gate.TriggerPlace!.Value;
            int? phase = progression.GetPhase(trigger);
            foreach (int neighbor in _neighbors[trigger])
            {
                int? behind = progression.GetPhase(neighbor);
                if (behind is not null && behind.Value < (phase ?? 0))
                {
                    seals.Add(new LevelSeal(SealKind.DoorsClose, trigger, trigger, neighbor, phase));
                }
            }
        }

        return seals.ToArray();
    }

    private GateZone GetOpenZone(int room)
    {
        var zone = GetPlace(room).Zone;
        if (zone != GateZone.Secret)
        {
            return zone;
        }

        foreach (int neighbor in _neighbors[room])
        {
            var other = GetPlace(neighbor).Zone;
            if (other != GateZone.Secret)
            {
                return other;
            }
        }

        return GateZone.Open;
    }

    private int[] FindLandmarks(HashSet<int> part, params LandmarkKind[] kinds) => _survey.Landmarks
        .Where(landmark => kinds.Contains(landmark.Kind) && part.Contains(landmark.Place))
        .Select(landmark => landmark.Place)
        .ToArray();

    private static HashSet<int> GetUnion(List<PhaseDraft> passes, int index) => passes
        .Take(index + 1)
        .SelectMany(pass => pass.Places)
        .ToHashSet();

    private int GetFirstVisit(int place) => _firstVisit.GetValueOrDefault(place, Unvisited);

    private LevelPlace GetPlace(int id) => _survey.Places[id - 1];

    private float GetSquaredDistance(int first, int second)
    {
        var a = GetCellCenter(first);
        var b = GetCellCenter(second);
        return (a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V);
    }

    private (float U, float V) GetCellCenter(int place)
    {
        var cells = GetPlace(place).Cells;
        return ((float)cells.Average(cell => cell.U), (float)cells.Average(cell => cell.V));
    }

    private void Walk(List<int> path, HashSet<int> within, int target)
    {
        var steps = FindPath(path[^1], target, within);
        path.AddRange(steps.Skip(1));
    }

    private List<int> FindPath(int from, int to, HashSet<int> within)
    {
        var previous = new Dictionary<int, int> { [from] = from };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            int place = queue.Dequeue();
            foreach (int neighbor in _neighbors[place])
            {
                if (within.Contains(neighbor) && previous.TryAdd(neighbor, place))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (!previous.ContainsKey(to))
        {
            return [from];
        }

        var steps = new List<int> { to };
        while (steps[^1] != from)
        {
            steps.Add(previous[steps[^1]]);
        }

        steps.Reverse();
        return steps;
    }

    private Dictionary<int, int> MeasureDistances(IEnumerable<int> sources, HashSet<int> within)
    {
        var lengths = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (int source in sources)
        {
            lengths[source] = 0;
            queue.Enqueue(source);
        }

        while (queue.Count > 0)
        {
            int place = queue.Dequeue();
            foreach (int neighbor in _neighbors[place])
            {
                if (within.Contains(neighbor) && lengths.TryAdd(neighbor, lengths[place] + 1))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return lengths;
    }

    private HashSet<int> FillComponent(int first, Func<int, bool> includes, Func<int, int, bool> crosses)
    {
        var seen = new HashSet<int> { first };
        var queue = new Queue<int>();
        queue.Enqueue(first);
        while (queue.Count > 0)
        {
            int place = queue.Dequeue();
            foreach (int neighbor in _neighbors[place])
            {
                if (includes(neighbor) && crosses(place, neighbor) && seen.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return seen;
    }

    private List<HashSet<int>> FindComponents(HashSet<int> places)
    {
        var components = new List<HashSet<int>>();
        var seen = new HashSet<int>();
        foreach (int place in places.Order())
        {
            if (!seen.Add(place))
                continue;
            
            var component = FillComponent(place, places.Contains, (_, _) => true);
            seen.UnionWith(component);
            components.Add(component);
        }

        return components;
    }

    private sealed record PhaseDraft(HashSet<int> Places, bool IsReturnPass, int Key, int[] Sites, int[] Charges);
}
