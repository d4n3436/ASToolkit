using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the fitting of room sizes by role, halls, courts, turns, row gaps, outline cuts, necks and connectors to a style's originals.
/// </summary>
public sealed class AreaFitter
{
    private const int MinSamples = 8;
    private const int MinScales = 3;
    private const float StraightCosine = 0.7f;
    private const float NeckRatio = 0.6f;
    private const int NeckWidthUnits = 4;
    private const int NeckLengthUnits = 5;
    private const int MaxProfileDepth = 80;
    private const int ConnectorUnits = 4;
    private const float ConnectorFill = 0.8f;
    private const int ProbeCells = 5;
    private const float LargeCutShare = 0.12f;
    private const float ClassPrior = 0.5f;
    private const double SingleSampleSigma = 0.5;
    private const double MinSigma = 0.05;
    private const double DrawSigmas = 2;

    private static readonly LevelTheme[] Themes = [LevelTheme.Lab, LevelTheme.Military];
    private static readonly MissionRole[] SizedRoles = [MissionRole.Start, MissionRole.End, MissionRole.Key, MissionRole.Path, MissionRole.Branch];
    private static readonly OutlineClass[] DrawnCuts = [OutlineClass.L, OutlineClass.NotchedCorner, OutlineClass.T, OutlineClass.Cross];
    private static readonly (int U, int V)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// Fits the architecture model of a style to the given originals.
    /// </summary>
    public static AreaModel Fit(IReadOnlyList<ReferenceLevel> levels, GenerationStyle style)
    {
        var indoor = levels
            .Where(level => !level.Name.EndsWith("01.map", StringComparison.Ordinal) && Themes.Contains(level.Survey.Setting))
            .Select(level => new Sample(level.Survey, IsAddOn(level.Name) == (style == GenerationStyle.AddOn)))
            .ToArray();

        return new AreaModel(Themes.ToDictionary(theme => theme, theme => FitTheme(indoor.Where(sample => sample.Survey.Setting == theme).ToArray(), theme)));
    }

    private static bool IsAddOn(string name) => name.StartsWith("ADDON", StringComparison.Ordinal);

    private static int GetUnitCells(LevelTheme theme) => theme == LevelTheme.Lab ? 2 : 4;

    private static bool IsSpine(PlaceKind kind) => kind is PlaceKind.Hall or PlaceKind.Corridor or PlaceKind.Yard;

    private static bool IsCloset(PlaceKind kind) => kind is PlaceKind.SecretCloset or PlaceKind.SpawnCloset or PlaceKind.Nook;

    private static bool IsSkipped(ConnectionKind kind) => kind is ConnectionKind.SecretWall or ConnectionKind.SpawnWall or ConnectionKind.MonsterHole or ConnectionKind.JammedDoor;

    private static bool IsDoor(ConnectionKind kind) => kind is ConnectionKind.Door or ConnectionKind.Doorway or ConnectionKind.LockedDoor;

    private static AreaThemeModel FitTheme(Sample[] samples, LevelTheme theme)
    {
        int k = GetUnitCells(theme);
        var own = new Measures();
        var other = new Measures();
        var turns = new List<int>();
        var branchTurns = new List<bool>();
        var profiles = new List<(DoorProfile Profile, bool Own)>();
        var cutCounts = new Dictionary<(int SizeClass, OutlineClass? Name, bool Cut), int>();
        foreach (var sample in samples)
        {
            var measures = sample.Own ? own : other;
            var levelProfiles = MeasureDoorProfiles(sample.Survey);
            profiles.AddRange(levelProfiles.Select(profile => (profile, sample.Own)));
            var neckCells = theme == LevelTheme.Lab
                ? levelProfiles.Where(profile => IsNeck(profile, k)).SelectMany(profile => profile.NeckCells).ToHashSet()
                : [];

            MeasureRoles(sample.Survey, k, measures, turns, branchTurns);
            measures.FloorPixels += sample.Survey.Places.Sum(place => place.AreaPixels);
            measures.FloorUnits += sample.Survey.Places.Sum(place => place.Cells.Count) / (float)(k * k);
            measures.RowGaps.AddRange(MeasureRowGaps(sample.Survey, k));
            measures.Courts.AddRange(MeasureCourts(sample.Survey, k));
            MeasureBodies(sample.Survey, k, neckCells, measures, cutCounts);
            if (theme == LevelTheme.Lab)
            {
                MeasureConnectors(sample.Survey, k, measures);
            }
        }

        return new AreaThemeModel
        {
            RoleSizes = SizedRoles.ToDictionary(role => role, role => (IReadOnlyList<float>)PoolRoleSizes(own, other, role)),
            Scales = (own.Scales.Count >= MinScales ? own.Scales : own.Scales.Concat(other.Scales)).ToArray(),
            PixelsPerUnit = (own.FloorPixels + other.FloorPixels) / Math.Max(1, own.FloorUnits + other.FloorUnits),
            Halls = (own.Halls.Count >= MinSamples ? own.Halls : own.Halls.Concat(other.Halls)).ToArray(),
            Courts = (own.Courts.Count >= MinSamples ? own.Courts : own.Courts.Concat(other.Courts)).ToArray(),
            HallShare = (own.PathHalls.Count(isHall => isHall) + 1f) / (own.PathHalls.Count + 2f),
            Turns = new TurnShares(GetLaplaceShare(turns, 0, 3), GetLaplaceShare(turns, 1, 3), GetLaplaceShare(turns, 2, 3)),
            BranchTurn = (branchTurns.Count(turned => turned) + 1f) / (branchTurns.Count + 2f),
            RowGaps = (own.RowGaps.Count >= MinSamples ? own.RowGaps : own.RowGaps.Concat(other.RowGaps)).ToArray(),
            CutShares = Enumerable.Range(0, AreaThemeModel.SizeClassCount)
                .Select(sizeClass => new CutShare(
                    (cutCounts.GetValueOrDefault((sizeClass, null, true)) + 1f) / (cutCounts.GetValueOrDefault((sizeClass, null, true)) + cutCounts.GetValueOrDefault((sizeClass, null, false)) + 2f),
                    DrawnCuts.ToDictionary(name => name, name => cutCounts.GetValueOrDefault((sizeClass, name, true)) + ClassPrior)))
                .ToArray(),
            CutSizes = own.Bodies
                .Where(name => name is not (OutlineClass.Box or OutlineClass.BoxWithPillars))
                .Distinct()
                .ToDictionary(name => name, name => FitLogNormal(GetCutSamples(own, name).Count >= MinSamples ? GetCutSamples(own, name) : GetCutSamples(own, name).Concat(GetCutSamples(other, name)).ToList())),
            Neck = theme == LevelTheme.Lab ? FitNeck(profiles, k) : null,
            Connector = theme == LevelTheme.Lab ? FitConnector(own, other) : null
        };
    }

    // Find the holes the floor encloses that reach more than one unit from every floor cell's edge, and return their bounding boxes
    private static List<CourtSize> MeasureCourts(LevelSurvey survey, int k)
    {
        var floor = survey.Places
            .Where(place => !IsCloset(place.Kind))
            .SelectMany(place => place.Cells)
            .ToHashSet();

        var courts = new List<CourtSize>();
        if (floor.Count == 0)
        {
            return courts;
        }

        int u0 = floor.Min(cell => cell.U) - 1, v0 = floor.Min(cell => cell.V) - 1, u1 = floor.Max(cell => cell.U) + 1, v1 = floor.Max(cell => cell.V) + 1;
        var outside = Flood(new Cell(u0, v0), cell => cell.U >= u0 && cell.U <= u1 && cell.V >= v0 && cell.V <= v1 && !floor.Contains(cell));
        var seen = new HashSet<Cell>();
        for (int u = u0; u <= u1; u++)
        {
            for (int v = v0; v <= v1; v++)
            {
                var cell = new Cell(u, v);
                if (floor.Contains(cell) || outside.Contains(cell) || seen.Contains(cell))
                {
                    continue;
                }

                var hole = Flood(cell, other => !floor.Contains(other) && !outside.Contains(other));
                seen.UnionWith(hole);
                if (MeasureDepth(hole) > k)
                {
                    courts.Add(new CourtSize((hole.Max(other => other.U) - hole.Min(other => other.U) + 1) / (float)k,
                        (hole.Max(other => other.V) - hole.Min(other => other.V) + 1) / (float)k));
                }
            }
        }

        return courts;
    }

    private static HashSet<Cell> Flood(Cell start, Func<Cell, bool> inside)
    {
        var reached = new HashSet<Cell> { start };
        var queue = new Queue<Cell>([start]);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var next in new[] { cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 } })
            {
                if (inside(next) && reached.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return reached;
    }

    // Return the most steps any cell of the hole lies from the hole's edge
    private static int MeasureDepth(HashSet<Cell> hole)
    {
        var depth = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var cell in hole)
        {
            if (new[] { cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 } }.Any(next => !hole.Contains(next)))
            {
                depth[cell] = 1;
                queue.Enqueue(cell);
            }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var next in new[] { cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 } })
            {
                if (hole.Contains(next) && depth.TryAdd(next, depth[cell] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return depth.Values.DefaultIfEmpty(0).Max();
    }

    private static float GetLaplaceShare(List<int> outcomes, int outcome, int kinds) => (outcomes.Count(value => value == outcome) + 1f) / (outcomes.Count + kinds);

    private static List<float> GetCutSamples(Measures measures, OutlineClass name) => measures.CutSizes.GetValueOrDefault(name) ?? [];

    private static List<float> PoolRoleSizes(Measures own, Measures other, MissionRole role)
    {
        var values = own.Sizes.GetValueOrDefault(role)?.ToList() ?? [];
        if (values.Count < MinSamples)
        {
            values.AddRange(own.Sizes.GetValueOrDefault(role is MissionRole.Start or MissionRole.End ? MissionRole.Path : MissionRole.Branch) ?? []);
        }

        if (values.Count < MinSamples)
        {
            values.AddRange(other.Sizes.GetValueOrDefault(role) ?? []);
        }

        return values;
    }

    // Fit the logarithms of sizes of at least 1, drawn within two standard deviations
    private static LogNormalFit FitLogNormal(IReadOnlyCollection<float> samples)
    {
        double[] logs = samples.Select(sample => Math.Log(Math.Max(1, sample))).ToArray();
        double mu = logs.Length > 0 ? logs.Average() : 0;
        double sigma = logs.Length > 1 ? Math.Max(MinSigma, Math.Sqrt(logs.Sum(value => (value - mu) * (value - mu)) / (logs.Length - 1))) : SingleSampleSigma;
        return new LogNormalFit(mu, sigma, mu - DrawSigmas * sigma, mu + DrawSigmas * sigma);
    }

    private static NeckFit FitNeck(List<(DoorProfile Profile, bool Own)> profiles, int k)
    {
        var doors = profiles
            .Where(row => IsDoor(row.Profile.Kind))
            .ToArray();

        var necked = doors
            .Where(row => IsNeck(row.Profile, k))
            .Select(row => row.Profile)
            .ToArray();

        bool[] links = doors
            .Where(row => row.Own)
            .GroupBy(row => (row.Profile.Level, row.Profile.Link))
            .Select(group => group.Any(row => IsNeck(row.Profile, k)))
            .ToArray();

        return new NeckFit(
            links.Count(isNecked => isNecked) / (float)Math.Max(1, links.Length),
            necked.Count(profile => profile.Entered) / (float)Math.Max(1, necked.Length),
            FitLogNormal(necked.Select(profile => profile.Width / (float)k).ToArray()),
            FitLogNormal(necked.Select(profile => Math.Max(1f, profile.Length / (float)k)).ToArray()));
    }

    private static ConnectorFit FitConnector(Measures own, Measures other)
    {
        var widths = own.ConnectorWidths.Count >= MinSamples ? own.ConnectorWidths : own.ConnectorWidths.Concat(other.ConnectorWidths).ToList();
        var lengths = own.ConnectorLengths.Count >= MinSamples ? own.ConnectorLengths : own.ConnectorLengths.Concat(other.ConnectorLengths).ToList();
        return new ConnectorFit((own.Connectors + 1f) / (own.TwoDoorRooms + 2f), widths.ToArray(), lengths.ToArray());
    }

    private static void MeasureRoles(LevelSurvey survey, int k, Measures measures, List<int> turns, List<bool> branchTurns)
    {
        var progression = new ProgressionMeasurer(survey).Measure();
        var graph = DoorGraph.Create(survey);
        var places = survey.Places.ToDictionary(place => place.Id);
        var center = places.Values
            .Where(place => place.Cells.Count > 0)
            .ToDictionary(place => place.Id, place => (U: place.Cells.Average(cell => (double)cell.U), V: place.Cells.Average(cell => (double)cell.V)));

        var roles = new Dictionary<int, MissionRole>();
        foreach (var phase in progression.Phases)
        {
            foreach (int place in phase.Path.Where(phase.Places.Contains))
            {
                roles.TryAdd(place, MissionRole.Path);
            }

            foreach (int place in phase.Branches.SelectMany(branch => branch.Places))
            {
                roles.TryAdd(place, MissionRole.Branch);
            }
        }

        foreach (var key in progression.Keys.Where(key => key.Kind != LandmarkKind.BlackoutTrigger))
        {
            roles[key.Place] = MissionRole.Key;
        }

        if (survey.Route.Count > 0)
        {
            roles[survey.Route[0].Place] = MissionRole.Start;
        }

        roles[progression.End] = MissionRole.End;
        foreach (var phase in progression.Phases)
        {
            var onPath = phase.Path.ToHashSet();
            foreach (var branch in phase.Branches)
            {
                int? anchor = branch.Anchors.Where(onPath.Contains).Cast<int?>().FirstOrDefault() ?? branch.Anchors.Cast<int?>().FirstOrDefault();
                if (anchor is null)
                {
                    continue;
                }

                var parent = FindParents(graph, branch.Places.Append(anchor.Value).ToHashSet(), anchor.Value);
                foreach (int room in branch.Places)
                {
                    if (!parent.TryGetValue(room, out int middle) || !parent.TryGetValue(middle, out int first) || !center.TryGetValue(first, out var value) ||
                        !center.TryGetValue(middle, out var value1) || !center.TryGetValue(room, out var value2))
                        continue;
                    
                    double? cosine = GetCosine(value, value1, value2);
                    if (cosine is not null)
                    {
                        branchTurns.Add(cosine.Value <= StraightCosine);
                    }
                }
            }

            int[] path = phase.Path
                .Distinct()
                .Where(center.ContainsKey)
                .ToArray();

            for (int index = 2; index < path.Length; index++)
            {
                double? cosine = GetCosine(center[path[index - 2]], center[path[index - 1]], center[path[index]]);
                if (cosine is not null)
                {
                    turns.Add(cosine.Value > StraightCosine ? 0 : cosine.Value < -StraightCosine ? 2 : 1);
                }
            }
        }

        var inGraph = graph.Neighbors;
        var sized = roles
            .Where(pair => inGraph.ContainsKey(pair.Key) && places.ContainsKey(pair.Key))
            .ToArray();

        float[] levelSizes = sized
            .Where(pair => !IsSpine(places[pair.Key].Kind))
            .Select(pair => places[pair.Key].Cells.Count / (float)(k * k))
            .Order()
            .ToArray();

        float scale = levelSizes.Length > 0 ? levelSizes[levelSizes.Length / 2] : 1f;
        measures.Scales.Add(scale);
        foreach ((int room, var role) in sized)
        {
            var place = places[room];
            float units = place.Cells.Count / (float)(k * k);
            bool isHall = IsSpine(place.Kind);
            if (role == MissionRole.Path)
            {
                measures.PathHalls.Add(isHall);
            }

            if (isHall && role is MissionRole.Path or MissionRole.Start or MissionRole.End)
            {
                float a = (place.Cells.Max(cell => cell.U) - place.Cells.Min(cell => cell.U) + 1) / (float)k;
                float b = (place.Cells.Max(cell => cell.V) - place.Cells.Min(cell => cell.V) + 1) / (float)k;
                measures.Halls.Add(new HallSize(Math.Max(a, b), Math.Min(a, b)));
                continue;
            }

            if (!measures.Sizes.TryGetValue(role, out var list))
            {
                list = [];
                measures.Sizes[role] = list;
            }

            list.Add(units / scale);
        }
    }

    private static Dictionary<int, int> FindParents(DoorGraph graph, HashSet<int> members, int root)
    {
        var parent = new Dictionary<int, int>();
        var seen = new HashSet<int> { root };
        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            foreach (int next in graph.Neighbors.GetValueOrDefault(room, []).Where(members.Contains).Order())
            {
                if (seen.Add(next))
                {
                    parent[next] = room;
                    queue.Enqueue(next);
                }
            }
        }

        return parent;
    }

    private static double? GetCosine((double U, double V) a, (double U, double V) b, (double U, double V) c)
    {
        (double u1, double v1) = (b.U - a.U, b.V - a.V);
        (double u2, double v2) = (c.U - b.U, c.V - b.V);
        double n1 = Math.Sqrt(u1 * u1 + v1 * v1);
        double n2 = Math.Sqrt(u2 * u2 + v2 * v2);
        return n1 > 0 && n2 > 0 ? (u1 * u2 + v1 * v2) / n1 / n2 : null;
    }

    // Measure the gaps between consecutive rooms opening onto each long side of a hall or corridor
    private static IEnumerable<float> MeasureRowGaps(LevelSurvey survey, int k)
    {
        var places = survey.Places.ToDictionary(place => place.Id);
        var cells = places.Values.ToDictionary(place => place.Id, place => place.Cells.ToHashSet());
        foreach (var hall in places.Values.Where(place => place.Kind is PlaceKind.Hall or PlaceKind.Corridor && place.Cells.Count > 0))
        {
            var hallCells = cells[hall.Id];
            bool alongU = hall.Cells.Max(cell => cell.U) - hall.Cells.Min(cell => cell.U) >= hall.Cells.Max(cell => cell.V) - hall.Cells.Min(cell => cell.V);
            var linked = survey.Connections
                .Where(connection => (connection.A == hall.Id || connection.B == hall.Id) && !IsSkipped(connection.Kind))
                .Select(connection => connection.A == hall.Id ? connection.B : connection.A)
                .Distinct();

            var sides = new Dictionary<int, List<(int Low, int High)>>();
            foreach (int room in linked)
            {
                if (!places.TryGetValue(room, out var place) || IsCloset(place.Kind) || IsSpine(place.Kind))
                {
                    continue;
                }

                var across = new List<int>();
                foreach (var cell in cells[room])
                {
                    foreach ((int du, int dv) in Steps)
                    {
                        if (hallCells.Contains(new Cell(cell.U + du, cell.V + dv)))
                        {
                            across.Add(alongU ? dv : du);
                        }
                    }
                }

                if (across.Count == 0 || across.All(value => value == 0))
                {
                    continue;
                }

                int side = Math.Sign(across.Average());
                int[] span = cells[room].Select(cell => alongU ? cell.U : cell.V).ToArray();
                if (!sides.TryGetValue(side, out var spans))
                {
                    spans = [];
                    sides[side] = spans;
                }

                spans.Add((span.Min(), span.Max()));
            }

            foreach (var spans in sides.Values)
            {
                spans.Sort();
                for (int index = 1; index < spans.Count; index++)
                {
                    yield return Math.Max(0, (spans[index].Low - spans[index - 1].High - 1) / (float)k);
                }
            }
        }
    }

    private static void MeasureBodies(LevelSurvey survey, int k, HashSet<Cell> neckCells, Measures measures, Dictionary<(int, OutlineClass?, bool), int> cutCounts)
    {
        foreach (var place in survey.Places.Where(place => place.Cells.Count > 0 && place.Kind is not (PlaceKind.Corridor or PlaceKind.SecretCloset or PlaceKind.SpawnCloset)))
        {
            var all = place.Cells.ToHashSet();
            var body = all.Where(cell => !neckCells.Contains(cell)).ToHashSet();
            if (body.Count == 0)
            {
                body = all;
            }

            (var name, float cutShare) = Classify(body, k);
            if (place.Kind is not PlaceKind.Nook && !IsSpine(place.Kind) && name != OutlineClass.Irregular)
            {
                measures.Bodies.Add(name);
                if (name is not (OutlineClass.Box or OutlineClass.BoxWithPillars))
                {
                    if (!measures.CutSizes.TryGetValue(name, out var list))
                    {
                        list = [];
                        measures.CutSizes[name] = list;
                    }

                    list.Add(100 * cutShare);
                }
            }

            float shortSide = Math.Min(body.Max(cell => cell.U) - body.Min(cell => cell.U) + 1, body.Max(cell => cell.V) - body.Min(cell => cell.V) + 1) / (float)k;
            int sizeClass = AreaThemeModel.GetSizeClass(shortSide);
            bool cut = name is not (OutlineClass.Box or OutlineClass.BoxWithPillars);
            Increment(cutCounts, (sizeClass, null, cut));
            if (cut && name != OutlineClass.Irregular)
            {
                Increment(cutCounts, (sizeClass, name, true));
            }
        }

        static void Increment(Dictionary<(int, OutlineClass?, bool), int> counts, (int, OutlineClass?, bool) key) => counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// Returns the outline class of a set of cells and the share of its bounding box its outer missing parts take.
    /// </summary>
    public static (OutlineClass Name, float CutShare) Classify(IReadOnlySet<Cell> cells, int k)
    {
        int u0 = cells.Min(cell => cell.U), u1 = cells.Max(cell => cell.U);
        int v0 = cells.Min(cell => cell.V), v1 = cells.Max(cell => cell.V);
        int box = (u1 - u0 + 1) * (v1 - v0 + 1);
        var missing = new HashSet<Cell>();
        for (int u = u0; u <= u1; u++)
        {
            for (int v = v0; v <= v1; v++)
            {
                var cell = new Cell(u, v);
                if (!cells.Contains(cell))
                {
                    missing.Add(cell);
                }
            }
        }

        var parts = new List<(string Kind, float Share, int Touches)>();
        var seen = new HashSet<Cell>();
        foreach (var first in missing)
        {
            if (!seen.Add(first))
            {
                continue;
            }

            var component = new List<Cell> { first };
            for (int index = 0; index < component.Count; index++)
            {
                foreach ((int du, int dv) in Steps)
                {
                    var next = new Cell(component[index].U + du, component[index].V + dv);
                    if (missing.Contains(next) && seen.Add(next))
                    {
                        component.Add(next);
                    }
                }
            }

            if (component.Count < k * k)
            {
                continue;
            }

            int touches = (component.Any(cell => cell.U == u0) ? 1 : 0) | (component.Any(cell => cell.U == u1) ? 2 : 0) | (component.Any(cell => cell.V == v0) ? 4 : 0)
                | (component.Any(cell => cell.V == v1) ? 8 : 0);
            int count = System.Numerics.BitOperations.PopCount((uint)touches);
            bool opposite = (touches & 3) == 3 || (touches & 12) == 12;
            string kind = touches == 0 ? "hole" : count == 2 && !opposite ? "corner" : count == 1 ? "side" : "cut";
            parts.Add((kind, component.Count / (float)box, touches));
        }

        var corners = parts.Where(part => part.Kind == "corner").ToArray();
        var sides = parts.Where(part => part.Kind == "side").ToArray();
        var outer = parts.Where(part => part.Kind != "hole").ToArray();
        float share = outer.Sum(part => part.Share);
        var name = outer.Length switch
        {
            0 => parts.Count == 0 ? OutlineClass.Box : OutlineClass.BoxWithPillars,
            1 when corners.Length == 1 => corners[0].Share >= LargeCutShare ? OutlineClass.L : OutlineClass.NotchedCorner,
            1 when sides.Length == 1 => sides[0].Share >= LargeCutShare ? OutlineClass.Bay : OutlineClass.NotchedSide,
            2 when corners.Length == 2 && (corners[0].Touches & corners[1].Touches) != 0 => OutlineClass.T,
            _ when corners.Length >= 3 && outer.Length == corners.Length => OutlineClass.Cross,
            _ => OutlineClass.Irregular
        };
        return (name, share);
    }

    private static void MeasureConnectors(LevelSurvey survey, int k, Measures measures)
    {
        var degree = new Dictionary<int, HashSet<int>>();
        foreach (var connection in survey.Connections.Where(connection => connection.Kind is ConnectionKind.Door or ConnectionKind.Doorway or ConnectionKind.LockedDoor or ConnectionKind.Opening))
        {
            Add(connection.A, connection.B);
            Add(connection.B, connection.A);
        }

        var twoDoor = degree.Where(pair => pair.Value.Count == 2).Select(pair => pair.Key).ToHashSet();
        var places = survey.Places.ToDictionary(place => place.Id);
        int[] connectors = FindConnectors(survey.Places, survey.Connections.Select(connection => (connection.A, connection.B, connection.Kind)), k)
            .Where(twoDoor.Contains)
            .ToArray();

        measures.TwoDoorRooms += twoDoor.Count;
        measures.Connectors += connectors.Length;
        foreach (int room in connectors)
        {
            var cells = places[room].Cells;
            int w = cells.Max(cell => cell.U) - cells.Min(cell => cell.U) + 1;
            int h = cells.Max(cell => cell.V) - cells.Min(cell => cell.V) + 1;
            measures.ConnectorWidths.Add(Math.Min(w, h) / (float)k);
            measures.ConnectorLengths.Add(Math.Max(w, h) / (float)k);
        }

        void Add(int a, int b)
        {
            if (a == b)
            {
                return;
            }

            if (!degree.TryGetValue(a, out var set))
            {
                set = [];
                degree[a] = set;
            }

            set.Add(b);
        }
    }

    /// <summary>
    /// Returns the rooms at most four layout units across, nearly box-shaped, with walked connections to different places on two opposite sides.
    /// </summary>
    public static IReadOnlyList<int> FindConnectors(IReadOnlyList<LevelPlace> places, IEnumerable<(int A, int B, ConnectionKind Kind)> connections, int k)
    {
        var cells = places.ToDictionary(place => place.Id, place => place.Cells.ToHashSet());
        var owner = new Dictionary<Cell, int>();
        foreach (var place in places)
        {
            foreach (var cell in place.Cells)
            {
                owner[cell] = place.Id;
            }
        }

        var sides = new Dictionary<(int Place, int U, int V), HashSet<int>>();
        foreach ((int a, int b, var kind) in connections)
        {
            if (IsSkipped(kind) || a == b)
            {
                continue;
            }

            foreach ((int from, int to) in new[] { (a, b), (b, a) })
            {
                foreach (var cell in cells.GetValueOrDefault(from) ?? [])
                {
                    foreach ((int du, int dv) in Steps)
                    {
                        if (cells[from].Contains(new Cell(cell.U + du, cell.V + dv)))
                            continue;

                        for (int step = 1; step <= ProbeCells; step++)
                        {
                            if (!owner.TryGetValue(new Cell(cell.U + du * step, cell.V + dv * step), out int hit))
                                continue;
                            
                            if (hit == to)
                            {
                                if (!sides.TryGetValue((from, du, dv), out var set))
                                {
                                    set = [];
                                    sides[(from, du, dv)] = set;
                                }

                                set.Add(to);
                            }

                            break;
                        }
                    }
                }
            }
        }

        var found = new List<int>();
        foreach (var place in places.Where(place => place.Cells.Count > 0 && !IsCloset(place.Kind) && !IsSpine(place.Kind)))
        {
            int w = place.Cells.Max(cell => cell.U) - place.Cells.Min(cell => cell.U) + 1;
            int h = place.Cells.Max(cell => cell.V) - place.Cells.Min(cell => cell.V) + 1;
            if (Math.Min(w, h) > ConnectorUnits * k || place.Cells.Count < ConnectorFill * w * h)
            {
                continue;
            }

            foreach ((int du, int dv) in new[] { (1, 0), (0, 1) })
            {
                var ahead = sides.GetValueOrDefault((place.Id, du, dv));
                var behind = sides.GetValueOrDefault((place.Id, -du, -dv));
                if (ahead is not null && behind is not null && !ahead.SetEquals(behind))
                {
                    found.Add(place.Id);
                    break;
                }
            }
        }

        return found;
    }

    private static bool IsNeck(DoorProfile profile, int k) =>
        profile.Necked && IsDoor(profile.Kind) && profile.Width <= NeckWidthUnits * k && profile.Length <= NeckLengthUnits * k;

    // Read each door side's room width at every depth from the door, necked where the first slice is narrow against the widest
    private static List<DoorProfile> MeasureDoorProfiles(LevelSurvey survey)
    {
        var places = survey.Places
            .Where(place => !IsCloset(place.Kind) && place.Cells.Count > 0)
            .ToDictionary(place => place.Id);

        var cells = places.Values.ToDictionary(place => place.Id, place => place.Cells.ToHashSet());
        var links = survey.Connections
            .Where(connection => !IsSkipped(connection.Kind) && places.ContainsKey(connection.A) && places.ContainsKey(connection.B) && connection.A != connection.B)
            .ToArray();

        var order = new Dictionary<int, int>();
        if (survey.Route.Count > 0 && places.ContainsKey(survey.Route[0].Place))
        {
            var neighbors = links
                .SelectMany(link => new[] { (link.A, link.B), (link.B, link.A) })
                .GroupBy(pair => pair.Item1)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.Item2).Distinct().Order().ToArray());

            var queue = new Queue<int>([survey.Route[0].Place]);
            order[survey.Route[0].Place] = 0;
            while (queue.Count > 0)
            {
                foreach (int next in neighbors.GetValueOrDefault(queue.Dequeue()) ?? [])
                {
                    if (order.TryAdd(next, order.Count))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        var profiles = new List<DoorProfile>();
        foreach (var link in links)
        {
            var pairs = link.Edges
                .SelectMany(run => run.GetEdges())
                .Select(edge => edge.Cells)
                .ToArray();

            if (pairs.Length == 0)
            {
                continue;
            }

            foreach ((int room, int other) in new[] { (link.A, link.B), (link.B, link.A) })
            {
                if (IsSpine(places[room].Kind))
                {
                    continue;
                }

                var own = cells[room];
                var door = pairs
                    .Where(pair => own.Contains(pair.First) || own.Contains(pair.Second))
                    .Select(pair => own.Contains(pair.First) ? pair.First : pair.Second)
                    .ToArray();

                if (door.Length == 0)
                {
                    continue;
                }

                var inner = own.Contains(pairs[0].First) ? pairs[0].First : pairs[0].Second;
                var outer = inner == pairs[0].First ? pairs[0].Second : pairs[0].First;
                (int du, int dv) = (inner.U - outer.U, inner.V - outer.V);
                (int ou, int ov) = du != 0 ? (0, 1) : (1, 0);
                var middle = door[door.Length / 2];
                var widths = new List<int>();
                var slices = new List<List<Cell>>();
                for (int depth = 0; depth < MaxProfileDepth; depth++)
                {
                    var at = new Cell(middle.U + du * depth, middle.V + dv * depth);
                    if (!own.Contains(at))
                    {
                        break;
                    }

                    var row = new List<Cell> { at };
                    foreach (int sign in new[] { 1, -1 })
                    {
                        for (int step = 1; own.Contains(new Cell(at.U + sign * ou * step, at.V + sign * ov * step)); step++)
                        {
                            row.Add(new Cell(at.U + sign * ou * step, at.V + sign * ov * step));
                        }
                    }

                    widths.Add(row.Count);
                    slices.Add(row);
                }

                if (widths.Count < 2)
                {
                    continue;
                }

                int widest = widths.Max();
                bool necked = widths[0] <= NeckRatio * widest;
                int wide = widths.FindIndex(width => width > NeckRatio * widest);
                int length = !necked ? 0 : wide >= 0 ? wide : widths.Count;
                profiles.Add(new DoorProfile(survey.Name, (Math.Min(room, other), Math.Max(room, other)), link.Kind, necked, length, widths[0],
                    order.GetValueOrDefault(room) > order.GetValueOrDefault(other), necked ? slices.Take(length).SelectMany(row => row).ToArray() : []));
            }
        }

        return profiles;
    }

    private sealed record Sample(LevelSurvey Survey, bool Own);

    private sealed record DoorProfile(string Level, (int A, int B) Link, ConnectionKind Kind, bool Necked, int Length, int Width, bool Entered, IReadOnlyList<Cell> NeckCells);

    private sealed class Measures
    {
        public Dictionary<MissionRole, List<float>> Sizes { get; } = [];

        public List<float> Scales { get; } = [];

        public long FloorPixels { get; set; }

        public float FloorUnits { get; set; }

        public List<HallSize> Halls { get; } = [];

        public List<CourtSize> Courts { get; } = [];

        public List<bool> PathHalls { get; } = [];

        public List<float> RowGaps { get; } = [];

        public List<OutlineClass> Bodies { get; } = [];

        public Dictionary<OutlineClass, List<float>> CutSizes { get; } = [];

        public List<float> ConnectorWidths { get; } = [];

        public List<float> ConnectorLengths { get; } = [];

        public int TwoDoorRooms { get; set; }

        public int Connectors { get; set; }
    }
}
