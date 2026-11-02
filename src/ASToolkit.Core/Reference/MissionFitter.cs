using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the fitting of a style's mission grammar to the progression of original levels, pooling structure per theme and fitting the trigger rate
/// on the style's own levels.
/// </summary>
public sealed class MissionFitter
{
    private const double DefaultPhaseShare = 0.3;

    private readonly GenerationStyle _style;

    /// <summary>
    /// Initializes a new instance of the <see cref="MissionFitter"/> class.
    /// </summary>
    /// <param name="style">The style the mission model is fitted for.</param>
    public MissionFitter(GenerationStyle style) => _style = style;

    /// <summary>
    /// Fits the mission model to the surveys of the original levels, leaving out the outdoor first levels.
    /// </summary>
    public MissionModel Fit(IReadOnlyList<LevelSurvey> surveys)
    {
        var samples = surveys
            .Where(survey => GetLevelNumber(survey.Name) > 1)
            .Select(Measure)
            .ToArray();

        var own = samples
            .Where(sample => sample.Style == _style)
            .ToArray();

        var structures = new Dictionary<LevelTheme, MissionStructure>();
        var lab = samples
            .Where(sample => sample.Theme == LevelTheme.Lab)
            .ToArray();

        foreach (var theme in (LevelTheme[])[LevelTheme.Lab, LevelTheme.Military])
        {
            structures[theme] = FitStructure(samples.Where(sample => sample.Theme == theme).ToArray(), lab);
        }

        var triggerRows = own
            .Select(sample => sample.TriggerRow)
            .ToArray();

        double crossed = triggerRows.Sum(row => row.Spawned * row.Share);
        double squared = triggerRows.Sum(row => row.Spawned * row.Spawned);

        var blackouts = samples
            .Where(sample => sample.Gate == LevelStyle.Blackout)
            .ToArray();

        var rubbleLevels = samples
            .Where(sample => sample is { Gate: LevelStyle.Fenced, GeneratorLandmarks: > 1 })
            .ToArray();

        return new MissionModel
        {
            Structures = structures,
            TriggerSlope = new FittedValue<double>(crossed / Math.Max(1e-6, squared), triggerRows.Length, IsBackedOff: false),
            BlackoutSeal = new FittedValue<double>(GetShare(blackouts.Select(sample => sample.Seals.Contains(SealKind.DoorsClose))), blackouts.Length, IsBackedOff: false),
            RubbleSeal = new FittedValue<double>(GetShare(rubbleLevels.Select(sample => sample.Seals.Contains(SealKind.Rubble))), rubbleLevels.Length, IsBackedOff: false),
            LastReturn = ((LevelStyle[])[LevelStyle.Blackout, LevelStyle.Fenced]).ToDictionary(gate => gate, gate => FitLastReturn(samples, gate))
        };
    }

    private static MissionStructure FitStructure(IReadOnlyList<LevelSample> levels, IReadOnlyList<LevelSample> lab)
    {
        var phases = levels
            .SelectMany(level => level.Phases)
            .ToArray();

        var trees = phases
            .SelectMany(phase => phase.Trees)
            .ToArray();

        double[] chains = trees
            .Where(tree => tree.Size > 1)
            .Select(tree => tree.Depth / (double)tree.Size)
            .ToArray();

        double[] anchors = GetAnchorPositions(levels);
        bool anchorsBackedOff = anchors.Length < LinearFit.MinSamples;
        if (anchorsBackedOff)
        {
            anchors = GetAnchorPositions(levels.Concat(lab).Distinct().ToArray());
        }

        int[] entered = phases
            .Where(phase => phase is { Entered: > 0, IsReturnPass: false })
            .Select(phase => phase.Entered)
            .ToArray();

        var gated = phases
            .Where(phase => phase.IsGated)
            .ToArray();

        var labPhases = lab
            .SelectMany(level => level.Phases)
            .ToArray();

        return new MissionStructure
        {
            PathShare = FitLogNormal(phases.Where(phase => !phase.IsReturnPass).Select(phase => phase.PathShare).ToArray(), []),
            TreeSizes = new FittedValue<IReadOnlyList<int>>(trees.Length > 0 ? trees.Select(tree => tree.Size).ToArray() : [1], trees.Length, trees.Length == 0),
            TreeChains = new FittedValue<IReadOnlyList<double>>(chains.Length > 0 ? chains : [1.0], chains.Length, chains.Length == 0),
            AnchorPositions = new FittedValue<IReadOnlyList<double>>(anchors, anchors.Length, anchorsBackedOff),
            FirstPhaseShare = FitPhaseShare(phases, labPhases, PhaseKind.First),
            LaterPhaseShare = FitPhaseShare(phases, labPhases, PhaseKind.Later),
            OpenedPhaseShare = FitPhaseShare(phases, labPhases, PhaseKind.Opened),
            EntranceShare = FitLogNormal(phases.Where(phase => phase.Entered > 0).Select(phase => phase.Entered / (double)phase.Places).ToArray(),
                labPhases.Where(phase => phase.Entered > 0).Select(phase => phase.Entered / (double)phase.Places).ToArray()),
            EnteredCounts = new FittedValue<IReadOnlyList<int>>(entered.Length > 0 ? entered : [1], entered.Length, entered.Length == 0),
            EntrancesPerSource = FitLogNormal(phases.Where(phase => phase.Entered > 0).Select(phase => phase.Entered / (double)phase.Sources).ToArray(),
                labPhases.Where(phase => phase.Entered > 0).Select(phase => phase.Entered / (double)phase.Sources).ToArray()),
            InnerLockRate = new FittedValue<double>(gated.Sum(phase => phase.InnerLocks) / (double)Math.Max(1, gated.Sum(phase => phase.Places)), gated.Length, IsBackedOff: false),
            LoopRate = new FittedValue<double>(levels.Sum(level => level.Loops) / (double)Math.Max(1, levels.Sum(level => level.Rooms)), levels.Count, IsBackedOff: false)
        };
    }

    private static FittedValue<LogNormalFit> FitPhaseShare(IReadOnlyList<PhaseSample> phases, IReadOnlyList<PhaseSample> labPhases, PhaseKind kind)
    {
        double[] own = phases.Where(phase => phase.Kind == kind).Select(phase => phase.LevelShare).ToArray();
        double[] lab = labPhases.Where(phase => phase.Kind == kind).Select(phase => phase.LevelShare).ToArray();
        return FitLogNormal(own, lab.Length > 0 ? lab : [DefaultPhaseShare]);
    }

    private static FittedValue<LogNormalFit> FitLogNormal(IReadOnlyCollection<double> own, IReadOnlyCollection<double> fallback) => own.Count > 0
        ? new FittedValue<LogNormalFit>(LogNormalFit.Fit(own), own.Count, IsBackedOff: false)
        : new FittedValue<LogNormalFit>(LogNormalFit.Fit(fallback.Count > 0 ? fallback : [DefaultPhaseShare]), fallback.Count, IsBackedOff: true);

    // Draw only the forms the fitting levels show, with a uniform prior when they show both
    private static FittedValue<double> FitLastReturn(IReadOnlyList<LevelSample> levels, LevelStyle gate)
    {
        bool[] forms = levels
            .Where(level => level.Gate == gate && level.Phases.Count > 1)
            .Select(level => level.Phases[^1].IsReturnPass)
            .ToArray();

        int returns = forms.Count(form => form);
        double share = returns == forms.Length ? 1 : returns == 0 ? 0 : (returns + 1) / (double)(forms.Length + 2);
        return new FittedValue<double>(forms.Length == 0 ? 0 : share, forms.Length, IsBackedOff: false);
    }

    private static double GetShare(IEnumerable<bool> values)
    {
        bool[] array = values.ToArray();
        return array.Length == 0 ? 0 : array.Count(value => value) / (double)array.Length;
    }

    private static int GetLevelNumber(string name) => int.Parse(name.AsSpan(name.LastIndexOf('_') + 1, 2));

    private static LevelSample Measure(LevelSurvey survey)
    {
        var graph = DoorGraph.Create(survey);
        var progression = new ProgressionMeasurer(survey).Measure();
        var rooms = graph.Rooms.ToHashSet();

        var locked = survey.Connections
            .Where(connection => connection.Kind is ConnectionKind.LockedDoor or ConnectionKind.Fence && rooms.Contains(connection.A) && rooms.Contains(connection.B))
            .Select(connection => (connection.A, connection.B))
            .ToArray();

        var phaseOf = new Dictionary<int, int>();
        foreach (var phase in progression.Phases)
        {
            foreach (int place in phase.Places)
            {
                phaseOf.TryAdd(place, phase.Index);
            }
        }

        var triggers = progression.Triggers.Select(trigger => trigger.Trigger).ToHashSet();
        int total = progression.Phases.Sum(phase => phase.Places.Count);
        int pathRooms = 0;
        int triggered = 0;
        var phases = new List<PhaseSample>();
        foreach (var phase in progression.Phases)
        {
            var places = phase.Places.ToHashSet();
            var into = locked
                .Where(link => places.Contains(link.A) != places.Contains(link.B) && Math.Max(phaseOf.GetValueOrDefault(link.A, -1), phaseOf.GetValueOrDefault(link.B, -1)) == phase.Index)
                .ToArray();

            int entered = phase.Index > 0 ? into.Select(link => places.Contains(link.B) ? link.B : link.A).Distinct().Count() : 0;
            int sources = into.Select(link => places.Contains(link.B) ? link.A : link.B).Distinct().Count();
            int innerLocks = locked.Count(link => places.Contains(link.A) && places.Contains(link.B));

            int[] path = phase.Path.Distinct().ToArray();
            pathRooms += path.Length;
            triggered += path.Count(triggers.Contains);

            var kind = phase.IsReturnPass ? PhaseKind.Opened : progression.Phases.Count == 1 ? PhaseKind.Single : phase.Index == 0 ? PhaseKind.First : PhaseKind.Later;
            var trees = phase.Branches
                .Where(branch => branch.Anchors.Count > 0)
                .Select(branch => new TreeSample(branch.Places.Count, branch.Depth, GetAnchorPosition(phase, branch)))
                .ToArray();

            phases.Add(new PhaseSample(kind, phase.IsReturnPass, phase.Places.Count, phase.Places.Count / (double)Math.Max(1, total),
                path.Count(places.Contains) / (double)Math.Max(1, phase.Places.Count), trees, entered, Math.Max(1, sources), innerLocks, survey.Style != LevelStyle.Ungated));
        }

        long spawned = survey.Encounters.Where(encounter => encounter.Kind is EncounterKind.Ambush or EncounterKind.Spawner).Sum(encounter => (long)encounter.HitPoints);
        long hitPoints = survey.Encounters.Sum(encounter => (long)encounter.HitPoints);
        var generators = survey.Landmarks
            .Where(landmark => landmark.Kind == LandmarkKind.Generator)
            .ToArray();

        return new LevelSample(
            survey.Name.StartsWith("ADDON", StringComparison.Ordinal) ? GenerationStyle.AddOn : GenerationStyle.Campaign,
            survey.Setting,
            survey.Style,
            graph.Rooms.Count,
            graph.CountLoops(),
            phases,
            (spawned / (double)Math.Max(1, hitPoints), triggered / (double)Math.Max(1, pathRooms)),
            progression.Seals.Select(seal => seal.Kind).ToHashSet(),
            generators.Length);
    }

    private enum PhaseKind
    {
        Single,
        First,
        Later,
        Opened
    }

    private static double[] GetAnchorPositions(IReadOnlyList<LevelSample> levels) => levels
        .SelectMany(level => level.Phases)
        .SelectMany(phase => phase.Trees)
        .Where(tree => tree.AnchorPosition is not null)
        .Select(tree => tree.AnchorPosition!.Value)
        .ToArray();

    // Place a branch's anchor along its phase's critical path, leaving out return passes and paths of two rooms or fewer
    private static double? GetAnchorPosition(LevelPhase phase, PhaseBranch branch)
    {
        int index = phase.Path.ToList().IndexOf(branch.Anchors[0]);
        return phase.IsReturnPass || phase.Path.Count <= 2 || index < 0 ? null : index / (double)(phase.Path.Count - 1);
    }

    private readonly record struct TreeSample(int Size, int Depth, double? AnchorPosition);

    private sealed record PhaseSample(PhaseKind Kind, bool IsReturnPass, int Places, double LevelShare, double PathShare, IReadOnlyList<TreeSample> Trees, int Entered, int Sources,
        int InnerLocks, bool IsGated);

    private sealed record LevelSample(GenerationStyle Style, LevelTheme Theme, LevelStyle Gate, int Rooms, int Loops, IReadOnlyList<PhaseSample> Phases,
        (double Spawned, double Share) TriggerRow, IReadOnlySet<SealKind> Seals, int GeneratorLandmarks);
}
