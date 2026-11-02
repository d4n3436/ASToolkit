using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using MathNet.Numerics.Distributions;

namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Provides the drawing of a mission graph from a level brief, its phases set by the gate style and joined by locks.
/// </summary>
public sealed class MissionGrammar
{
    private const int MinPhaseRooms = 2;
    private const int MinPathRooms = 2;
    private const int HubChoices = 3;
    private const double MaxTriggerShare = 0.9;
    private const int MinLoopDoors = 2;
    private const int MaxLoopDoors = 4;

    private readonly MissionModel _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="MissionGrammar"/> class.
    /// </summary>
    /// <param name="model">The fitted mission model of the style.</param>
    public MissionGrammar(MissionModel model) => _model = model;

    /// <summary>
    /// Draws the mission graph of a level with the given objectives.
    /// </summary>
    public MissionGraph Draw(Random rng, LevelBrief brief, MissionObjectives objectives) =>
        new Drawing(_model, _model.Structures[brief.Theme == LevelTheme.Military ? LevelTheme.Military : LevelTheme.Lab], rng, brief, objectives).Run();

    private enum PhaseForm
    {
        Single,
        Quiet,
        Key,
        Locked,
        Opened,
        Return
    }

    private sealed record Tree(int Anchor, List<(int From, int To)> Edges, List<int> Rooms);

    private sealed class Drawing
    {
        private readonly MissionModel _model;
        private readonly MissionStructure _structure;
        private readonly Random _rng;
        private readonly LevelBrief _brief;
        private readonly MissionObjectives _objectives;
        private readonly List<(int From, int To)> _edges = [];
        private readonly List<(int From, int To)> _locked = [];
        private readonly List<(int From, int To)> _entrances = [];
        private readonly List<(int From, int To)> _loops = [];
        private readonly Dictionary<int, MissionRole> _roles = [];
        private readonly List<PhaseTrigger> _triggers = [];
        private readonly List<LevelSeal> _seals = [];
        private readonly List<PhaseKey> _keys = [];
        private readonly List<LevelPhase> _phases = [];
        private int _nextRoom = 1;
        private int? _end;

        public Drawing(MissionModel model, MissionStructure structure, Random rng, LevelBrief brief, MissionObjectives objectives)
        {
            _model = model;
            _structure = structure;
            _rng = rng;
            _brief = brief;
            _objectives = objectives;
        }

        public MissionGraph Run()
        {
            var forms = DrawForms();
            double[] shares = forms.Count == 1
                ? [1.0]
                : forms.Select((form, index) => (form == PhaseForm.Return ? _structure.OpenedPhaseShare : index == 0 ? _structure.FirstPhaseShare : _structure.LaterPhaseShare).Value.Draw(_rng)).ToArray();
            double total = shares.Sum();
            int[] counts = shares.Select(share => Math.Max(MinPhaseRooms, (int)Math.Round(Math.Max(1, _brief.Rooms) * share / total, MidpointRounding.ToEven))).ToArray();

            List<int> previousPath = [];
            List<int> previousRooms = [];
            int? keyRoom = null;
            for (int index = 0; index < forms.Count; index++)
            {
                if (forms[index] == PhaseForm.Return && keyRoom is not null)
                {
                    DrawReturnPass(index, counts[index], keyRoom.Value, previousPath);
                    continue;
                }

                (previousPath, previousRooms, keyRoom) = DrawPhase(index, forms, counts[index], previousPath, previousRooms, keyRoom);
            }

            int end = _end ?? SetEnd(_phases[^1].Path[^1]);
            return CreateGraph(end);
        }

        private List<PhaseForm> DrawForms()
        {
            var forms = _brief.GateStyle switch
            {
                LevelStyle.LockedDoors => [PhaseForm.Key, PhaseForm.Locked],
                LevelStyle.Blackout => [PhaseForm.Quiet, PhaseForm.Key, PhaseForm.Opened],
                LevelStyle.Fenced => Enumerable.Repeat(PhaseForm.Key, Math.Max(1, _objectives.GeneratorRooms)).Append(PhaseForm.Locked).ToList(),
                _ => [PhaseForm.Single]
            };

            if (forms[^1] == PhaseForm.Opened || (_brief.GateStyle == LevelStyle.Fenced && forms.Count > 1))
            {
                double returns = _model.LastReturn.TryGetValue(_brief.GateStyle, out var fitted) ? fitted.Value : 0;
                forms[^1] = _rng.NextDouble() < returns ? PhaseForm.Return : PhaseForm.Locked;
            }

            return forms;
        }

        private int CreateRoom() => _nextRoom++;

        // Keep the start room's role when the level ends where it began
        private int SetEnd(int room)
        {
            _end = room;
            if (!_roles.TryGetValue(room, out var role) || role != MissionRole.Start)
            {
                _roles[room] = MissionRole.End;
            }

            return room;
        }

        private (List<int> Path, List<int> Rooms, int? KeyRoom) DrawPhase(int index, IReadOnlyList<PhaseForm> forms, int count, List<int> previousPath, List<int> previousRooms, int? keyRoom)
        {
            var form = forms[index];
            int pathLength = Math.Max(MinPathRooms, Math.Min(count, (int)Math.Round(count * _structure.PathShare.Value.Draw(_rng), MidpointRounding.ToEven)));
            var path = Enumerable.Range(0, pathLength).Select(_ => CreateRoom()).ToList();
            _edges.AddRange(path.Zip(path.Skip(1)));
            if (index == 0)
            {
                _roles[path[0]] = MissionRole.Start;
            }

            bool gated = false;
            int hub = 0;
            if (previousPath.Count > 0)
            {
                var before = forms[index - 1];
                var hubs = previousPath
                    .Where(room => !IsLockedOffStart(room))
                    .Take(HubChoices)
                    .ToList();

                hub = hubs.Count > 0 ? hubs[_rng.Next(hubs.Count)] : previousPath[0];
                gated = (form is PhaseForm.Locked or PhaseForm.Key && before != PhaseForm.Quiet) || (before == PhaseForm.Key && form == PhaseForm.Locked);
                (gated ? _locked : _edges).Add((hub, path[0]));
                if (before == PhaseForm.Quiet)
                {
                    _keys.Add(new PhaseKey(LandmarkKind.BlackoutTrigger, path[0]));
                    if (_rng.NextDouble() < _model.BlackoutSeal.Value)
                    {
                        _seals.Add(new LevelSeal(SealKind.DoorsClose, path[0], path[0], hub, index));
                    }
                }

                if (form == PhaseForm.Key && before == PhaseForm.Key && _rng.NextDouble() < _model.RubbleSeal.Value)
                {
                    _seals.Add(new LevelSeal(SealKind.Rubble, path[^1], path[0], hub, index));
                }
            }

            var anchors = (form == PhaseForm.Key ? path.Skip(1).SkipLast(1) : path.Skip(1)).ToList();
            if (anchors.Count == 0)
            {
                anchors = [path[0]];
            }

            var trees = GrowTrees(count - pathLength, anchors, path);
            foreach (var tree in trees)
            {
                _edges.AddRange(tree.Edges);
            }

            var members = path.Concat(trees.SelectMany(tree => tree.Rooms)).ToList();
            if (previousPath.Count > 0 && gated)
            {
                var near = previousPath.ToHashSet();
                foreach ((int from, int to) in _edges)
                {
                    if (previousPath.Contains(to) && previousRooms.Contains(from))
                    {
                        near.Add(from);
                    }
                    else if (previousPath.Contains(from) && previousRooms.Contains(to))
                    {
                        near.Add(to);
                    }
                }

                AddEntrances(members, previousRooms.Where(near.Contains).ToList(), hub);
            }

            if (_brief.GateStyle != LevelStyle.Ungated)
            {
                AddInnerLocks(members, path);
            }

            int? key = keyRoom;
            if (form == PhaseForm.Key)
            {
                key = path[^1];
                _roles[path[^1]] = MissionRole.Key;
                _keys.Add(new PhaseKey(_brief.GateStyle == LevelStyle.Fenced ? LandmarkKind.Generator : LandmarkKind.Breaker, path[^1]));
            }

            List<int> sites = [];
            if (form is PhaseForm.Locked or PhaseForm.Single)
            {
                if (_objectives.Sites > 0)
                {
                    var candidates = path.Count > 2 ? path.Skip(1).SkipLast(1).ToList() : path;
                    foreach (int site in Sample(candidates, Math.Min(_objectives.Sites, Math.Max(1, path.Count - 2))))
                    {
                        if (_roles.TryAdd(site, MissionRole.Site))
                        {
                            sites.Add(site);
                        }
                    }
                }

                SetEnd(path[^1]);
            }

            AddTriggers(path);
            AddLoops(members, trees);
            _phases.Add(new LevelPhase(index, members.Order().ToArray(), path[0], form == PhaseForm.Key ? [path[^1]] : [], path.ToArray(), [], CreateBranches(trees), 0, false, [], sites));
            return (path, members, key);
        }

        private void DrawReturnPass(int index, int count, int keyRoom, IReadOnlyList<int> previousPath)
        {
            var path = previousPath.Reverse().Where(room => room != keyRoom).Prepend(keyRoom).ToList();
            int charges = CreateRoom();
            _locked.Add((keyRoom, charges));
            _roles[charges] = _objectives.Sites > 0 ? MissionRole.Charges : MissionRole.Reward;
            var anchors = path.Count > 1 ? path.Skip(1).Where(room => !IsLockedOffStart(room)).ToList() : path;
            if (anchors.Count == 0)
            {
                anchors = path;
            }

            var grown = GrowTrees(Math.Max(1, count - 1), anchors, []);
            int entrances = Math.Max(grown.Count, (int)Math.Round(_structure.EntranceShare.Value.Draw(_rng) * (count - 1), MidpointRounding.ToEven));
            List<int> opened = [];
            foreach (var tree in grown)
            {
                _locked.AddRange(tree.Edges.Where(edge => edge.From == tree.Anchor));
                _edges.AddRange(tree.Edges.Where(edge => edge.From != tree.Anchor));
                opened.AddRange(tree.Rooms);
            }

            var firsts = grown.Select(tree => tree.Rooms[0]).ToHashSet();
            var extra = opened.Where(room => !firsts.Contains(room)).ToList();
            foreach (int room in Sample(extra, Math.Min(extra.Count, Math.Max(0, entrances - grown.Count))))
            {
                _entrances.Add((anchors[_rng.Next(anchors.Count)], room));
            }

            var sites = Sample(opened, Math.Min(opened.Count, _objectives.Sites));
            foreach (int site in sites)
            {
                _roles[site] = MissionRole.Site;
            }

            SetEnd(path[^1]);
            var trees = grown.Prepend(new Tree(keyRoom, [(keyRoom, charges)], [charges])).ToList();
            int[] places = opened.Append(charges).Order().ToArray();
            int[] walk = path.Skip(1).Prepend(keyRoom).Prepend(charges).Prepend(keyRoom).ToArray();
            _phases.Add(new LevelPhase(index, places, keyRoom, [], walk, [], CreateBranches(trees), 0, true, path.ToArray(), sites.ToArray()));
        }

        // Anchor each tree at the candidate nearest a fitted position along the critical path, or at any candidate without a path
        private List<Tree> GrowTrees(int count, IReadOnlyList<int> anchors, IReadOnlyList<int> path)
        {
            var positions = _structure.AnchorPositions.Value;
            var trees = new List<Tree>();
            var sizes = _structure.TreeSizes.Value;
            var chains = _structure.TreeChains.Value;
            for (int left = count; left > 0;)
            {
                int size = Math.Min(left, Math.Max(1, sizes[_rng.Next(sizes.Count)]));
                left -= size;
                int anchor = DrawAnchor(anchors, path, positions);
                double chain = chains[_rng.Next(chains.Count)];
                var members = new List<int> { anchor };
                var edges = new List<(int From, int To)>();
                for (int added = 0; added < size; added++)
                {
                    int room = CreateRoom();
                    int parent = members.Count == 1 || _rng.NextDouble() < chain ? members[^1] : members[_rng.Next(members.Count)];
                    edges.Add((parent, room));
                    members.Add(room);
                }

                trees.Add(new Tree(anchor, edges, members.Skip(1).ToList()));
            }

            return trees;
        }

        private void AddEntrances(IReadOnlyList<int> members, IReadOnlyList<int> previousRooms, int hub)
        {
            var counts = _structure.EnteredCounts.Value;
            int entered = Math.Max(1, Math.Min(members.Count, counts[_rng.Next(counts.Count)]));
            var others = previousRooms.Where(room => room != hub && !IsLockedOffStart(room)).ToList();
            int extraSources = Math.Min(others.Count, Math.Max(0, (int)Math.Round(entered / _structure.EntrancesPerSource.Value.Draw(_rng), MidpointRounding.ToEven) - 1));
            var sources = Sample(others, extraSources).Prepend(hub).ToList();
            foreach (int room in Sample(members.Skip(1).ToList(), Math.Min(members.Count - 1, entered - 1)))
            {
                _entrances.Add((sources[_rng.Next(sources.Count)], room));
            }
        }

        // Lock only doors into rooms the phase's key does not need
        private void AddInnerLocks(IReadOnlyList<int> members, IReadOnlyList<int> path)
        {
            var inside = members.ToHashSet();
            var needed = path.ToHashSet();
            var own = _edges
                .Where(edge => inside.Contains(edge.From) && inside.Contains(edge.To))
                .Where(edge => !(needed.Contains(edge.From) && needed.Contains(edge.To)) && !IsObjective(edge.From) && !IsObjective(edge.To))
                .ToList();

            int count = Math.Min(own.Count, DrawPoisson(_structure.InnerLockRate.Value * members.Count));
            foreach (var edge in Sample(own, count))
            {
                _edges.Remove(edge);
                _locked.Add(edge);
            }
        }

        // Keep locks off the start room except where a blackout closes rooms around it
        private bool IsLockedOffStart(int room) => _brief.GateStyle != LevelStyle.Blackout && _roles.TryGetValue(room, out var role) && role == MissionRole.Start;

        private bool IsObjective(int room) => _roles.TryGetValue(room, out var role) && role is MissionRole.Key or MissionRole.Site or MissionRole.Charges or MissionRole.End or MissionRole.Start;

        private void AddTriggers(IReadOnlyList<int> path)
        {
            double share = Math.Min(MaxTriggerShare, _model.TriggerSlope.Value * _brief.SpawnedShare);
            for (int index = 1; index < path.Count; index++)
            {
                if (_rng.NextDouble() < share)
                {
                    int target = path[Math.Min(path.Count - 1, index + _rng.Next(2))];
                    _triggers.Add(new PhaseTrigger(path[index], target, target != path[index] ? SpawnSource.Ahead : SpawnSource.AtTrigger, 0));
                }
            }
        }

        // Close branch chains of three rooms or more back onto their anchor first, then join other rooms of the phase
        private void AddLoops(IReadOnlyList<int> members, IReadOnlyList<Tree> trees)
        {
            int count = DrawPoisson(_structure.LoopRate.Value * members.Count);
            var chains = trees
                .Where(tree => tree.Rooms.Count > MinLoopDoors && tree.Edges.GroupBy(edge => edge.From).All(group => group.Count() == 1))
                .ToList();

            for (int added = 0; added < count; added++)
            {
                if (chains.Count > 0)
                {
                    var tree = chains[_rng.Next(chains.Count)];
                    chains.Remove(tree);
                    var loop = (tree.Anchor, tree.Edges[^1].To);
                    _edges.Add(loop);
                    _loops.Add(loop);
                    continue;
                }

                int from = members[_rng.Next(members.Count)];
                var distances = GetDistances(from);
                var far = members.Where(room => distances.GetValueOrDefault(room, 0) is >= MinLoopDoors and <= MaxLoopDoors).ToList();
                if (far.Count > 0)
                {
                    var loop = (from, far[_rng.Next(far.Count)]);
                    _edges.Add(loop);
                    _loops.Add(loop);
                }
            }
        }

        private Dictionary<int, int> GetDistances(int from)
        {
            var neighbors = new Dictionary<int, List<int>>();
            foreach ((int a, int b) in _edges.Concat(_locked).Concat(_entrances))
            {
                neighbors.TryAdd(a, []);
                neighbors.TryAdd(b, []);
                neighbors[a].Add(b);
                neighbors[b].Add(a);
            }

            var distances = new Dictionary<int, int>();
            if (!neighbors.ContainsKey(from))
            {
                return distances;
            }

            distances[from] = 0;
            var queue = new Queue<int>([from]);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                foreach (int next in neighbors[room])
                {
                    if (distances.TryAdd(next, distances[room] + 1))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return distances;
        }

        private int DrawPoisson(double mean) => mean <= 0 ? 0 : Poisson.Sample(_rng, mean);

        private int DrawAnchor(IReadOnlyList<int> anchors, IReadOnlyList<int> path, IReadOnlyList<double> positions)
        {
            if (path.Count <= 1 || positions.Count == 0)
            {
                return anchors[_rng.Next(anchors.Count)];
            }

            double position = positions[_rng.Next(positions.Count)];
            var nearest = anchors
                .GroupBy(anchor => Math.Abs(path.ToList().IndexOf(anchor) / (double)(path.Count - 1) - position))
                .MinBy(group => group.Key)!
                .ToArray();

            return nearest[_rng.Next(nearest.Length)];
        }

        private List<T> Sample<T>(IReadOnlyList<T> items, int count)
        {
            var pool = items.ToArray();
            int taken = Math.Clamp(count, 0, pool.Length);
            for (int index = 0; index < taken; index++)
            {
                int swap = _rng.Next(index, pool.Length);
                (pool[index], pool[swap]) = (pool[swap], pool[index]);
            }

            return pool.Take(taken).ToList();
        }

        private static PhaseBranch[] CreateBranches(IEnumerable<Tree> trees) => trees
            .Select(tree =>
            {
                var depths = new Dictionary<int, int>();
                foreach ((int from, int to) in tree.Edges)
                {
                    depths[to] = depths.GetValueOrDefault(from, 0) + 1;
                }

                return new PhaseBranch(tree.Rooms.Order().ToArray(), [tree.Anchor], depths, 0, false, 0);
            })
            .ToArray();

        private MissionGraph CreateGraph(int end)
        {
            var loops = _loops.ToHashSet();
            var links = _edges.Where(edge => !loops.Contains(edge)).Select(edge => new MissionLink(edge.From, edge.To, false, MissionLinkKind.Tree))
                .Concat(_locked.Select(edge => new MissionLink(edge.From, edge.To, true, MissionLinkKind.Tree)))
                .Concat(_entrances.Select(edge => new MissionLink(edge.From, edge.To, true, MissionLinkKind.Entrance)))
                .Concat(_loops.Select(edge => new MissionLink(edge.From, edge.To, false, MissionLinkKind.Loop)))
                .ToArray();

            var path = _phases.SelectMany(phase => phase.Path).ToHashSet();
            for (int room = 1; room < _nextRoom; room++)
            {
                _roles.TryAdd(room, path.Contains(room) ? MissionRole.Path : MissionRole.Branch);
            }

            var phases = _phases
                .Select(phase => phase with { Loops = CountLoops(phase.Places, links) })
                .ToArray();

            int[] flow = phases
                .SelectMany(phase => phase.Path.Concat(phase.Branches.SelectMany(branch => branch.Places)))
                .Distinct()
                .ToArray();

            int[] locks = links
                .Where(link => link.IsLocked)
                .Select(link => link.To)
                .Distinct()
                .Order()
                .ToArray();

            return new MissionGraph(new LevelProgression(phases, _keys.ToArray(), locks, end, flow, _triggers.ToArray(), _seals.ToArray()), links, _roles);
        }

        private static int CountLoops(IReadOnlyList<int> places, IReadOnlyList<MissionLink> links)
        {
            var inside = places.ToHashSet();
            var pairs = links
                .Where(link => inside.Contains(link.From) && inside.Contains(link.To))
                .Select(link => (Math.Min(link.From, link.To), Math.Max(link.From, link.To)))
                .ToHashSet();

            var parent = places.ToDictionary(place => place, place => place);
            int components = places.Count;
            foreach ((int a, int b) in pairs)
            {
                int rootA = FindRoot(parent, a);
                int rootB = FindRoot(parent, b);
                if (rootA != rootB)
                {
                    parent[rootA] = rootB;
                    components--;
                }
            }

            return pairs.Count - places.Count + components;
        }

        private static int FindRoot(Dictionary<int, int> parent, int place)
        {
            while (parent[place] != place)
            {
                place = parent[place];
            }

            return place;
        }
    }
}
