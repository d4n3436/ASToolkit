using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Provides the tour of a level. From the start it always goes on to the nearest objective reachable under the current
/// gates and completes everything it passes close enough to on the way.
/// </summary>
public static class RouteAnalyzer
{
    /// <summary>
    /// Analyzes a level, or returns null when the map has no floor, no player start or nothing to do.
    /// </summary>
    public static RouteAnalysis? Analyze(string name, GameMap map, LevelScriptSummary script, ObjectTable objects)
    {
        var model = LevelExtractor.Extract(name, map, objects);
        if (model is null)
        {
            return null;
        }

        var grid = RouteGridBuilder.Build(model, objects);
        var tour = new TourBuilder(model, grid, script);
        return tour.Run();
    }

    private sealed class Objective
    {
        public required ObjectiveKind Kind { get; init; }

        public required Cell Cell { get; init; }

        public required double X { get; init; }

        public required double Y { get; init; }

        public double Reach { get; set; }

        public List<(double X, double Y)> Points { get; init; } = [];

        public Objective? Need { get; init; }

        public int At { get; set; }
    }

    // Dense index over the grid's bounding rectangle to avoid hashing
    private sealed class CellIndex
    {
        private readonly int[] _slots;
        private readonly int _u0;
        private readonly int _v0;
        private readonly int _width;
        private readonly int _height;

        public CellIndex(IReadOnlyCollection<Cell> cells)
        {
            _u0 = cells.Count == 0 ? 0 : cells.Min(c => c.U);
            _v0 = cells.Count == 0 ? 0 : cells.Min(c => c.V);
            _width = cells.Count == 0 ? 0 : cells.Max(c => c.U) - _u0 + 1;
            _height = cells.Count == 0 ? 0 : cells.Max(c => c.V) - _v0 + 1;
            _slots = new int[_width * _height];
            _slots.AsSpan().Fill(-1);
            foreach (var cell in cells)
            {
                _slots[(cell.U - _u0) * _height + cell.V - _v0] = Count++;
            }
        }

        public int Count { get; }

        public int Find(Cell c)
        {
            int a = c.U - _u0;
            int b = c.V - _v0;
            return (uint)a < (uint)_width && (uint)b < (uint)_height ? _slots[a * _height + b] : -1;
        }
    }

    private sealed class DistanceMap
    {
        private readonly CellIndex _index;
        private readonly int[] _distance;

        public DistanceMap(CellIndex index)
        {
            _index = index;
            _distance = GC.AllocateUninitializedArray<int>(index.Count);
            _distance.AsSpan().Fill(-1);
        }

        public int Count { get; private set; }

        public int this[Cell c] => TryGetValue(c, out int d) ? d : throw new KeyNotFoundException();

        public bool IsUnreached(Cell c)
        {
            int i = _index.Find(c);
            return i >= 0 && _distance[i] < 0;
        }

        public bool TryAdd(Cell c, int distance)
        {
            int i = _index.Find(c);
            if (i < 0 || _distance[i] >= 0)
            {
                return false;
            }

            _distance[i] = distance;
            Count++;
            return true;
        }

        public bool ContainsKey(Cell c) => TryGetValue(c, out _);

        public bool TryGetValue(Cell c, out int distance)
        {
            int i = _index.Find(c);
            distance = i >= 0 ? _distance[i] : -1;
            return distance >= 0;
        }

        public int GetValueOrDefault(Cell c, int fallback = 0) => TryGetValue(c, out int d) ? d : fallback;
    }

    private sealed class TourBuilder
    {
        private static readonly Dictionary<ObjectiveKind, double> ReachByKind = new()
        {
            [ObjectiveKind.DynamiteSite] = 40,
            [ObjectiveKind.Breaker] = 40,
            [ObjectiveKind.Generator] = 250,
            [ObjectiveKind.Boss] = 350,
            [ObjectiveKind.Monsters] = 350,
            [ObjectiveKind.Spawned] = 350,
            [ObjectiveKind.Dynamite] = 40
        };

        private readonly LevelModel _model;
        private readonly RouteGrid _grid;
        private readonly LevelScriptSummary _script;
        private readonly Lattice _lattice;
        private readonly double _step;
        private readonly CellIndex _index;
        private readonly Queue<Cell> _queue = new();
        private DistanceMap? _fromStart;

        public TourBuilder(LevelModel model, RouteGrid grid, LevelScriptSummary script)
        {
            _model = model;
            _grid = grid;
            _script = script;
            _lattice = grid.Lattice;
            _step = _lattice.CellStep;
            _index = new CellIndex(grid.Cells);
        }

        public RouteAnalysis? Run()
        {
            MapSprite? start = null;
            foreach (var id in new[] { KnownObject.PlayerStart, KnownObject.PlayerStartGirl, KnownObject.Cursor })
            {
                start = _model.Sprites.FirstOrDefault(s => s.Id == id);
                if (start is not null)
                {
                    break;
                }
            }

            if (start is null)
            {
                return null;
            }

            var (startCell, _) = FindNearestReachableCell(start.X, start.Y);
            if (startCell is null)
            {
                return null;
            }

            var s0 = startCell.Value;
            var fromStart = Search([s0], k => IsBlocked(k, false, false, false));
            _fromStart = fromStart;
            var elements = CollectElements();
            var objectives = CollectObjectives(elements);
            if (objectives.Length == 0)
            {
                return null;
            }

            bool hasDoors = _grid.Edges.Values.Any(v => v == 'D');
            bool doorsLocked = _script.DoorsLocked && hasDoors && objectives.Any(o => o.Kind == ObjectiveKind.Breaker);
            (Cell Cell, int Radius)? blackout = null;
            if (_script.Blackout is not null && hasDoors)
            {
                var (bc, _) = FindNearestReachableCell(_script.Blackout.Value.X, _script.Blackout.Value.Y);
                if (bc is not null)
                {
                    blackout = (bc.Value, _script.Blackout.Value.Radius);
                }
            }

            bool fencesUp = _grid.FenceEdges.Count > 0;
            bool secretsAllowed = false;
            bool usedSecrets = false;
            bool forcedGates = false;
            var todo = new List<Objective>(objectives);
            var done = new List<(Objective? Objective, ObjectiveKind Kind, double X, double Y, int At)>();
            var path = new List<Cell> { s0 };
            var legs = new List<(RoutePhase Phase, int Cells)>();
            var current = s0;
            int carried = 0;

            while (todo.Count > 0)
            {
                Func<EdgeKey, bool> gate = k => IsBlocked(k, !secretsAllowed, doorsLocked, fencesUp);
                var fromCurrent = Search([current], gate);
                if (todo.All(o => o.Kind != ObjectiveKind.DynamiteSite))
                {
                    todo.RemoveAll(o => o.Kind == ObjectiveKind.Dynamite);
                }

                if (todo.Count == 0)
                {
                    break;
                }

                var reachable = todo.Where(o => fromCurrent.ContainsKey(o.Cell) && (o.Kind != ObjectiveKind.DynamiteSite || carried > 0) && IsOpened(o)).ToArray();
                if (reachable.Length == 0)
                {
                    if (!secretsAllowed)
                    {
                        secretsAllowed = true;
                        usedSecrets = true;
                        continue;
                    }

                    if (doorsLocked || fencesUp)
                    {
                        doorsLocked = false;
                        fencesUp = false;
                        forcedGates = true;
                        continue;
                    }

                    break;
                }

                var keys = reachable.Where(o => (doorsLocked && o.Kind == ObjectiveKind.Breaker) || (fencesUp && o.Kind == ObjectiveKind.Generator)).ToArray();
                var pool = keys.Length > 0 ? keys : reachable;
                var next = pool[0];
                foreach (var objective in pool)
                {
                    if (fromCurrent[objective.Cell] < fromCurrent[next.Cell])
                    {
                        next = objective;
                    }
                }

                var toNext = Search([next.Cell], gate);
                var leg = Walk(current, toNext, gate);
                var phase = doorsLocked ? RoutePhase.DoorsLocked : fencesUp ? RoutePhase.FencesUp : RoutePhase.Free;
                if (blackout is not null && !doorsLocked)
                {
                    (var bc, int radius) = blackout.Value;
                    int within = Math.Max(1, (int)Math.Round(radius / _step));
                    if (leg.Any(c => Math.Abs(c.U - bc.U) + Math.Abs(c.V - bc.V) <= within))
                    {
                        doorsLocked = true;
                        blackout = null;
                        done.Add((null, ObjectiveKind.Blackout, _script.Blackout!.Value.X, _script.Blackout.Value.Y, path.Count));
                    }
                }

                legs.Add((phase, leg.Length - 1));
                int legBase = path.Count - 1;
                path.AddRange(leg.Skip(1));
                current = next.Cell;
                var probes = new List<(int Index, double X, double Y)>();
                for (int legIndex = 0; legIndex < leg.Length; legIndex += 2)
                {
                    (double x, double y) = _lattice.GetCenter(leg[legIndex]);
                    probes.Add((legIndex, x, y));
                }

                (double lx, double ly) = _lattice.GetCenter(leg[^1]);
                probes.Add((leg.Length - 1, lx, ly));
                var passed = new List<(int Hit, Objective Objective)>();
                foreach (var objective in todo)
                {
                    if (ReferenceEquals(objective, next))
                    {
                        continue;
                    }

                    int? hit = FindHit(probes, objective);
                    bool needsReach = objective.Kind is ObjectiveKind.DynamiteSite or ObjectiveKind.Breaker or ObjectiveKind.Generator or ObjectiveKind.Dynamite;
                    if (hit is not null && (!needsReach || fromCurrent.ContainsKey(objective.Cell)))
                    {
                        passed.Add((hit.Value, objective));
                    }
                }

                foreach ((int hit, var objective) in passed.OrderBy(t => t.Hit))
                {
                    if (!IsOpened(objective))
                    {
                        continue;
                    }

                    if (objective.Kind == ObjectiveKind.DynamiteSite)
                    {
                        if (carried <= 0)
                        {
                            continue;
                        }

                        carried--;
                    }

                    if (objective.Kind == ObjectiveKind.Dynamite)
                    {
                        carried++;
                    }

                    objective.At = legBase + hit;
                    todo.Remove(objective);
                    done.Add((objective, objective.Kind, objective.X, objective.Y, objective.At));
                    if (objective.Kind == ObjectiveKind.Breaker)
                    {
                        doorsLocked = false;
                    }

                    if (objective.Kind == ObjectiveKind.Generator)
                    {
                        fencesUp = false;
                    }
                }

                todo.Remove(next);
                next.At = path.Count - 1;
                done.Add((next, next.Kind, next.X, next.Y, next.At));
                if (next.Kind == ObjectiveKind.Dynamite)
                {
                    carried++;
                }

                if (next.Kind == ObjectiveKind.DynamiteSite)
                {
                    carried--;
                }

                if (next.Kind == ObjectiveKind.Breaker)
                {
                    doorsLocked = false;
                }

                if (next.Kind == ObjectiveKind.Generator)
                {
                    fencesUp = false;
                }
            }

            int walked = path.Count - 1;
            var unique = new List<Cell>();
            var seen = new HashSet<Cell>();
            foreach (var cell in path)
            {
                if (seen.Add(cell))
                {
                    unique.Add(cell);
                }
            }

            var branch = Search(unique, k => IsBlocked(k, false, false, false));
            var position = new Dictionary<Cell, int>();
            for (int cellIndex = 0; cellIndex < path.Count; cellIndex++)
            {
                position.TryAdd(path[cellIndex], cellIndex);
            }

            var projection = Project(unique, position);

            foreach (var element in elements)
            {
                element.Progress = Progress(element.Cell);
                element.BranchDepth = (int)Math.Round(branch.GetValueOrDefault(element.Cell) * _step);
                element.Distance = (int)Math.Round(fromStart[element.Cell] * _step);
                var trigger = element.Kind == ElementKind.Spawner ? element.Program?.Trigger : null;
                if (trigger is not null)
                {
                    var (tc, _) = FindNearestReachableCell(trigger.Value.X, trigger.Value.Y);
                    if (tc is not null && fromStart.ContainsKey(tc.Value))
                    {
                        element.TriggerProgress = Progress(tc.Value);
                        element.TriggerBranchDepth = (int)Math.Round(branch.GetValueOrDefault(tc.Value) * _step);
                        element.TriggerDistance = (int)Math.Round(Math.Sqrt(Math.Pow(element.X - trigger.Value.X, 2) + Math.Pow(element.Y - trigger.Value.Y, 2)));
                    }
                }
            }

            var merged = new List<RouteLeg>();
            foreach ((var phase, int cells) in legs)
            {
                int px = (int)Math.Round(cells * _step);
                if (merged.Count > 0 && merged[^1].Phase == phase)
                {
                    merged[^1] = merged[^1] with { Pixels = merged[^1].Pixels + px };
                }
                else
                {
                    merged.Add(new RouteLeg(phase, px));
                }
            }

            var checkpoints = new List<RouteCheckpoint>();
            foreach (var checkpoint in _script.Checkpoints)
            {
                var (c, _) = FindNearestReachableCell(checkpoint.X, checkpoint.Y);
                if (c is not null && fromStart.ContainsKey(c.Value))
                {
                    checkpoints.Add(new RouteCheckpoint(checkpoint.X, checkpoint.Y, Progress(c.Value)));
                }
            }

            return new RouteAnalysis
            {
                Name = _model.Name,
                Theme = _model.Theme,
                Step = _step,
                ReachableCells = _grid.Cells.Count(fromStart.ContainsKey),
                RoutePixels = (int)Math.Round(walked * _step),
                SpinePixels = (int)Math.Round(Math.Max(1, path.Select(c => fromStart.GetValueOrDefault(c)).DefaultIfEmpty().Max()) * _step),
                RevisitShare = Math.Round(1 - unique.Count / (double)Math.Max(1, path.Count), 3),
                Legs = merged,
                Objectives = done.Select(d => new RouteObjective(d.Kind, d.X, d.Y, Math.Round(d.At / (double)Math.Max(1, walked), 3))).ToArray(),
                Elements = elements,
                Gating = MeasureGates(s0, elements, Progress),
                Checkpoints = checkpoints,
                FallbackUsage = new FallbackUsage(usedSecrets, forcedGates, todo.Count),
                Tour = path,
                Grid = _grid
            };

            bool IsOpened(Objective o) => o.Need is null || !todo.Contains(o.Need);

            double Progress(Cell c) => Math.Round(projection.GetValueOrDefault(c) / (double)Math.Max(1, walked), 3);
        }

        private bool IsBlocked(EdgeKey k, bool secretsClosed, bool doorsLocked, bool fencesUp)
        {
            if (fencesUp && _grid.FenceEdges.Contains(k))
            {
                return true;
            }

            if (!_grid.Edges.TryGetValue(k, out char kind))
            {
                return false;
            }

            return RouteGrid.IsBlocking(kind) || (secretsClosed && kind == 'S') || (doorsLocked && kind == 'D');
        }

        private DistanceMap Search(IEnumerable<Cell> sources, Func<EdgeKey, bool> isBlocked)
        {
            var distance = new DistanceMap(_index);
            foreach (var source in sources)
            {
                if (distance.TryAdd(source, 0))
                {
                    _queue.Enqueue(source);
                }
            }

            while (_queue.Count > 0)
            {
                var c = _queue.Dequeue();
                int d = distance[c];
                foreach (var (n, k) in EdgeKey.GetNeighbors(c))
                {
                    if (distance.IsUnreached(n) && !isBlocked(k))
                    {
                        distance.TryAdd(n, d + 1);
                        _queue.Enqueue(n);
                    }
                }
            }

            return distance;
        }

        private static Cell[] Walk(Cell from, DistanceMap toTarget, Func<EdgeKey, bool> isBlocked)
        {
            var walk = new List<Cell> { from };
            while (toTarget.GetValueOrDefault(walk[^1]) > 0)
            {
                int here = toTarget[walk[^1]];
                bool moved = false;
                foreach (var (n, k) in EdgeKey.GetNeighbors(walk[^1]))
                {
                    if (toTarget.TryGetValue(n, out int there) && there == here - 1 && !isBlocked(k))
                    {
                        walk.Add(n);
                        moved = true;
                        break;
                    }
                }

                if (!moved)
                {
                    break;
                }
            }

            return walk.ToArray();
        }

        private Dictionary<Cell, int> Project(List<Cell> unique, Dictionary<Cell, int> position)
        {
            var projection = new Dictionary<Cell, int>();
            var queue = new Queue<Cell>();
            foreach (var cell in unique)
            {
                projection[cell] = position[cell];
                queue.Enqueue(cell);
            }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var (n, k) in EdgeKey.GetNeighbors(c))
                {
                    if (_grid.Cells.Contains(n) && !projection.ContainsKey(n) && !(_grid.Edges.TryGetValue(k, out char kind) && RouteGrid.IsBlocking(kind)))
                    {
                        projection[n] = projection[c];
                        queue.Enqueue(n);
                    }
                }
            }

            return projection;
        }

        private static int? FindHit(List<(int Index, double X, double Y)> probes, Objective o)
        {
            double r = o.Reach;
            foreach ((int index, double x, double y) in probes)
            {
                foreach ((double px, double py) in o.Points)
                {
                    if (Math.Abs(x - px) <= r && Math.Abs(y - py) <= r && Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) <= r)
                    {
                        return index;
                    }
                }
            }

            return null;
        }

        private (Cell? Cell, double Offset) FindNearestReachableCell(double x, double y)
        {
            var c = _lattice.GetCell(x, y);
            if (IsInPool(c))
            {
                return (c, 0);
            }

            for (int index = 1; index < 8; index++)
            {
                Cell? best = null;
                double bestDistance = double.MaxValue;
                for (int du = -index; du <= index; du++)
                {
                    for (int dv = -index; dv <= index; dv++)
                    {
                        var n = new Cell(c.U + du, c.V + dv);
                        if (!IsInPool(n))
                        {
                            continue;
                        }

                        (double cx, double cy) = _lattice.GetCenter(n);
                        double d = Math.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y));
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = n;
                        }
                    }
                }

                if (best is not null)
                {
                    return (best, bestDistance);
                }
            }

            return (null, 0);
        }

        private bool IsInPool(Cell c) => _fromStart?.ContainsKey(c) ?? _grid.Cells.Contains(c);

        private RouteElement[] CollectElements()
        {
            var elements = new List<RouteElement>();

            foreach (var sprite in _model.Sprites)
            {
                var i = sprite.Id;
                if (KnownObject.Monsters.Contains(i) || i is KnownObject.Bat or KnownObject.Diver)
                {
                    Add(ElementKind.Monster, sprite);
                }
                else if (KnownObject.Bosses.Contains(i))
                {
                    Add(ElementKind.Boss, sprite);
                }
                else if (i == KnownObject.Spawner)
                {
                    var program = SpawnerProgram.Parse(sprite.Payload);
                    if (program is not null && program.Monsters.Any())
                    {
                        Add(ElementKind.Spawner, sprite, program: program);
                    }
                }
                else if (i.IsEndlessGenerator)
                {
                    Add(ElementKind.EndlessGenerator, sprite);
                }
                else if (i == KnownObject.TeleportPad)
                {
                    Add(ElementKind.TeleportPad, sprite);
                }
                else if (i == KnownObject.DynamiteSite)
                {
                    Add(ElementKind.DynamiteSite, sprite);
                }
                else if (i == KnownObject.Breaker)
                {
                    Add(ElementKind.Breaker, sprite);
                }
                else if (i == KnownObject.Generator)
                {
                    Add(ElementKind.Generator, sprite);
                }
                else if (i == KnownObject.Fence)
                {
                    Add(ElementKind.Fence, sprite);
                }
                else if (i == KnownObject.MountedCannon)
                {
                    Add(ElementKind.MountedCannon, sprite);
                }
                else if (i == KnownObject.BreakableWall)
                {
                    Add(ElementKind.SecretWall, sprite);
                }
                else if (i is KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack)
                {
                    Add(ElementKind.SpawnWall, sprite);
                }
                else if (i is KnownObject.SmallMonsterHole or KnownObject.BigMonsterHole)
                {
                    Add(ElementKind.MonsterHole, sprite);
                }
                else if (i == KnownObject.Crate)
                {
                    var item = KnownObject.GetCrateItem(sprite.Payload);
                    Add(ElementKind.Crate, sprite, item == 0 ? null : ItemCategorizer.GetCategory(item), item);
                }
                else if (ItemCategorizer.GetCategory(i) is not null)
                {
                    Add(ElementKind.Pickup, sprite, ItemCategorizer.GetCategory(i));
                }
            }

            var smallHoles = elements.Where(e => e.Id == KnownObject.SmallMonsterHole).ToArray();
            foreach (var element in elements)
            {
                if (element.Kind == ElementKind.SecretWall && smallHoles.Any(h => Math.Abs(element.X - h.X) + Math.Abs(element.Y - h.Y) < 10))
                {
                    element.Kind = ElementKind.PowerTrap;
                }
            }

            return elements.ToArray();

            void Add(ElementKind kind, MapSprite s, ItemCategory? category = null, KnownObject crateItem = 0, SpawnerProgram? program = null)
            {
                (var c, double offset) = FindNearestReachableCell(s.X, s.Y);
                if (c is null || !_fromStart!.ContainsKey(c.Value))
                {
                    return;
                }

                elements.Add(new RouteElement
                {
                    Kind = kind,
                    Id = s.Id,
                    X = (int)Math.Round(s.X),
                    Y = (int)Math.Round(s.Y),
                    Cell = c.Value,
                    Hidden = offset > 30,
                    Category = category,
                    CrateItem = crateItem,
                    Program = program
                });
            }
        }

        private Objective[] CollectObjectives(IReadOnlyList<RouteElement> elements)
        {
            var objectives = new List<Objective>();
            foreach (var element in elements)
            {
                ObjectiveKind? kind = element.Kind switch
                {
                    ElementKind.DynamiteSite => ObjectiveKind.DynamiteSite,
                    ElementKind.Breaker => ObjectiveKind.Breaker,
                    ElementKind.Generator => ObjectiveKind.Generator,
                    ElementKind.Boss => ObjectiveKind.Boss,
                    _ => null
                };
                if (kind is not null)
                {
                    objectives.Add(new Objective { Kind = kind.Value, Cell = element.Cell, X = element.X, Y = element.Y });
                    continue;
                }

                if (element.Kind != ElementKind.Spawner)
                {
                    continue;
                }

                // Triggered spawners keep building monsters, which walk the waypoints and wait at the last one
                Objective? trigger = null;
                var t = element.Program!.Trigger;
                if (t is not null)
                {
                    var (tc, _) = FindNearestReachableCell(t.Value.X, t.Value.Y);
                    if (tc is not null && _fromStart!.ContainsKey(tc.Value))
                    {
                        trigger = new Objective { Kind = ObjectiveKind.Trigger, Cell = tc.Value, X = t.Value.X, Y = t.Value.Y, Reach = t.Value.Radius };
                        objectives.Add(trigger);
                    }
                }

                var points = new List<(double X, double Y)> { (element.X, element.Y) };
                points.AddRange(element.Program.Waypoints.Select(w => ((double)w.X, (double)w.Y)));
                for (int index = points.Count - 1; index >= 0; index--)
                {
                    var (wc, _) = FindNearestReachableCell(points[index].X, points[index].Y);
                    if (wc is not null && _fromStart!.ContainsKey(wc.Value))
                    {
                        objectives.Add(new Objective
                        {
                            Kind = ObjectiveKind.Spawned,
                            Cell = wc.Value,
                            X = Math.Round(points[index].X),
                            Y = Math.Round(points[index].Y),
                            Points = points,
                            Need = trigger
                        });
                        break;
                    }
                }
            }

            foreach (var element in elements)
            {
                if (element.Kind == ElementKind.Monster)
                {
                    objectives.Add(new Objective { Kind = ObjectiveKind.Monsters, Cell = element.Cell, X = element.X, Y = element.Y });
                }

                if (element is { Kind: ElementKind.Pickup, Category: ItemCategory.Dynamite })
                {
                    objectives.Add(new Objective { Kind = ObjectiveKind.Dynamite, Cell = element.Cell, X = element.X, Y = element.Y });
                }
            }

            foreach (var objective in objectives)
            {
                if (objective.Reach == 0)
                {
                    objective.Reach = ReachByKind.GetValueOrDefault(objective.Kind);
                }

                if (objective.Points.Count == 0)
                {
                    objective.Points.Add((objective.X, objective.Y));
                }
            }

            return objectives.ToArray();
        }

        private GateMeasurement MeasureGates(Cell start, IReadOnlyList<RouteElement> elements, Func<Cell, double> progress)
        {
            var fromStart = _fromStart!;
            double? lockedShare = null;
            double? fencedOffShare = null;
            int? keyToGate = null;
            double? blackoutProgress = null;
            IReadOnlyList<int> breakerFromBlackout = [];
            var breakers = elements.Where(e => e.Kind == ElementKind.Breaker).ToArray();
            bool hasDoors = _grid.Edges.Values.Any(v => v == 'D');
            Func<EdgeKey, bool> doorsShut = k => IsBlocked(k, false, true, false);
            if ((_script.DoorsLocked || _script.Blackout is not null) && hasDoors)
            {
                var zone = Search([start], doorsShut);
                lockedShare = Math.Round(1 - zone.Count / (double)Math.Max(1, fromStart.Count), 3);
                var doorCells = _grid.Edges.Where(e => e.Value == 'D')
                    .SelectMany(e => new[] { e.Key.Cells.First, e.Key.Cells.Second })
                    .Where(zone.ContainsKey).ToArray();
                if (doorCells.Length > 0 && breakers.Length > 0)
                {
                    keyToGate = MeasureNearest(Search([breakers[0].Cell], doorsShut), doorCells);
                }

                if (_script.Blackout is not null)
                {
                    var (bc, _) = FindNearestReachableCell(_script.Blackout.Value.X, _script.Blackout.Value.Y);
                    var fromBlackout = bc is null ? new DistanceMap(_index) : Search([bc.Value], doorsShut);
                    blackoutProgress = bc is null ? 0 : progress(bc.Value);
                    breakerFromBlackout = breakers.Select(b => (int)Math.Round(fromBlackout.GetValueOrDefault(b.Cell, -1) * _step)).ToArray();
                }
            }

            if (elements.Any(e => e.Kind == ElementKind.Fence))
            {
                Func<EdgeKey, bool> fenced = k => IsBlocked(k, false, false, true);
                var zone = Search([start], fenced);
                fencedOffShare = Math.Round(1 - zone.Count / (double)Math.Max(1, fromStart.Count), 3);
                var fenceCells = _grid.FenceEdges.SelectMany(k => new[] { k.Cells.First, k.Cells.Second }).Where(zone.ContainsKey).ToArray();
                var generators = elements.Where(e => e.Kind == ElementKind.Generator && zone.ContainsKey(e.Cell)).Select(e => e.Cell).ToArray();
                if (fenceCells.Length > 0 && generators.Length > 0)
                {
                    keyToGate = MeasureNearest(Search(generators, fenced), fenceCells);
                }
            }

            return new GateMeasurement
            {
                LockedShare = lockedShare,
                FencedOffShare = fencedOffShare,
                KeyToGatePixels = keyToGate,
                BlackoutProgress = blackoutProgress,
                BreakerFromBlackoutPixels = breakerFromBlackout
            };
        }

        private int? MeasureNearest(DistanceMap distance, IReadOnlyList<Cell> targets)
        {
            int best = int.MaxValue;
            foreach (var cell in targets)
            {
                if (distance.TryGetValue(cell, out int d) && d < best)
                {
                    best = d;
                }
            }

            return best == int.MaxValue ? null : (int)Math.Round(best * _step);
        }
    }
}
