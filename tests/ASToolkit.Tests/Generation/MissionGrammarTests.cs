using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Generation;

public class MissionGrammarTests
{
    private const int Seeds = 200;

    private static readonly LevelStyle[] Gates = [LevelStyle.LockedDoors, LevelStyle.Blackout, LevelStyle.Fenced, LevelStyle.Ungated];

    [Fact]
    public void Draw_SameSeed_DrawsTheSameGraph()
    {
        var grammar = new MissionGrammar(CreateModel());

        foreach (var gate in Gates)
        {
            var first = grammar.Draw(new Random(11), CreateBrief(gate), GetObjectives(gate));
            var second = grammar.Draw(new Random(11), CreateBrief(gate), GetObjectives(gate));

            Assert.Equal(first.Links, second.Links);
            Assert.Equal(first.Roles.OrderBy(pair => pair.Key), second.Roles.OrderBy(pair => pair.Key));
        }
    }

    [Fact]
    public void Draw_AnyGate_ReachesEveryRoomWithTheLocksOpen()
    {
        foreach (var (_, graph) in DrawAll())
        {
            var reached = Reach(graph, graph.Links);

            Assert.Equal(graph.Roles.Keys.Order(), reached.Order());
        }
    }

    [Fact]
    public void Draw_AnyGate_JoinsPhasesOnlyThroughLocks()
    {
        foreach (var (gate, graph) in DrawAll())
        {
            var phaseOf = GetPhases(graph);
            foreach (var link in graph.Links.Where(link => phaseOf[link.From] != phaseOf[link.To] && !link.IsLocked))
            {
                bool intoBlackout = gate == LevelStyle.Blackout && Math.Min(phaseOf[link.From], phaseOf[link.To]) == 0 && Math.Max(phaseOf[link.From], phaseOf[link.To]) == 1;
                Assert.True(intoBlackout, $"{gate} links phases {phaseOf[link.From]} and {phaseOf[link.To]} without a lock");
            }
        }
    }

    [Fact]
    public void Draw_AnyGate_KeepsLoopsInsideOnePhase()
    {
        foreach (var (_, graph) in DrawAll())
        {
            var phaseOf = GetPhases(graph);

            Assert.All(graph.Links.Where(link => link.Kind == MissionLinkKind.Loop), link => Assert.Equal(phaseOf[link.From], phaseOf[link.To]));
        }
    }

    [Fact]
    public void Draw_GatedLevel_EntersFromOnOrNextToThePreviousPath()
    {
        foreach (var (_, graph) in DrawAll())
        {
            var phases = graph.Progression.Phases;
            var phaseOf = GetPhases(graph);
            foreach (var link in graph.Links.Where(link => link.Kind == MissionLinkKind.Entrance))
            {
                var phase = phases[phaseOf[link.To]];
                var path = phase.IsReturnPass ? phase.Path : phases[phase.Index - 1].Path;
                bool near = path.Contains(link.From) || graph.Links.Any(other => (other.From == link.From && path.Contains(other.To)) || (other.To == link.From && path.Contains(other.From)));

                Assert.True(near, $"entrance from {link.From} into phase {phase.Index}");
            }
        }
    }

    [Fact]
    public void Draw_GateOtherThanBlackout_LocksNothingOffTheStart()
    {
        foreach (var (_, graph) in DrawAll().Where(entry => entry.Gate != LevelStyle.Blackout))
        {
            int start = graph.Roles.Single(pair => pair.Value == MissionRole.Start).Key;

            Assert.DoesNotContain(graph.Links, link => link.IsLocked && (link.From == start || link.To == start));
        }
    }

    [Fact]
    public void Draw_AnchorPositionsAtTheEntrance_AnchorsBranchesOnTheRoomAfterIt()
    {
        var model = CreateModel();
        var structure = model.Structures[LevelTheme.Lab] with { AnchorPositions = new FittedValue<IReadOnlyList<double>>([0.0], 9, false) };
        var grammar = new MissionGrammar(model with { Structures = new Dictionary<LevelTheme, MissionStructure> { [LevelTheme.Lab] = structure, [LevelTheme.Military] = structure } });
        foreach (var gate in Gates)
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var graph = grammar.Draw(new Random(seed), CreateBrief(gate), GetObjectives(gate));
                foreach (var phase in graph.Progression.Phases.Where(phase => phase is { IsReturnPass: false, Path.Count: > 2 }))
                {
                    Assert.All(phase.Branches, branch => Assert.Equal(phase.Path[1], branch.Anchors[0]));
                }
            }
        }
    }

    [Fact]
    public void Draw_BlackoutWithoutNewAreas_AlwaysEndsInAReturnPass()
    {
        var model = CreateModel() with { LastReturn = new Dictionary<LevelStyle, FittedValue<double>> { [LevelStyle.Blackout] = new(1, 1, false), [LevelStyle.Fenced] = new(0, 5, false) } };
        var grammar = new MissionGrammar(model);

        for (int seed = 0; seed < Seeds; seed++)
        {
            var blackout = grammar.Draw(new Random(seed), CreateBrief(LevelStyle.Blackout), GetObjectives(LevelStyle.Blackout));
            var fenced = grammar.Draw(new Random(seed), CreateBrief(LevelStyle.Fenced), GetObjectives(LevelStyle.Fenced));

            Assert.True(blackout.Progression.Phases[^1].IsReturnPass);
            Assert.DoesNotContain(fenced.Progression.Phases, phase => phase.IsReturnPass);
        }
    }

    private static IEnumerable<(LevelStyle Gate, MissionGraph Graph)> DrawAll()
    {
        var grammar = new MissionGrammar(CreateModel());
        foreach (var gate in Gates)
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                yield return (gate, grammar.Draw(new Random(seed), CreateBrief(gate), GetObjectives(gate)));
            }
        }
    }

    private static Dictionary<int, int> GetPhases(MissionGraph graph)
    {
        var phaseOf = new Dictionary<int, int>();
        foreach (var phase in graph.Progression.Phases)
        {
            foreach (int place in phase.Places)
            {
                phaseOf.TryAdd(place, phase.Index);
            }
        }

        return phaseOf;
    }

    private static HashSet<int> Reach(MissionGraph graph, IReadOnlyList<MissionLink> links)
    {
        int start = graph.Roles.Single(pair => pair.Value == MissionRole.Start).Key;
        var reached = new HashSet<int> { start };
        var queue = new Queue<int>([start]);
        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            foreach (var link in links.Where(link => link.From == room || link.To == room))
            {
                int other = link.From == room ? link.To : link.From;
                if (reached.Add(other))
                {
                    queue.Enqueue(other);
                }
            }
        }

        return reached;
    }

    private static MissionObjectives GetObjectives(LevelStyle gate) => gate switch
    {
        LevelStyle.Blackout => new MissionObjectives(5, 0),
        LevelStyle.Fenced => new MissionObjectives(0, 2),
        _ => new MissionObjectives(0, 0)
    };

    private static LevelBrief CreateBrief(LevelStyle gate) => new()
    {
        Level = 5,
        Theme = gate == LevelStyle.Fenced ? LevelTheme.Military : LevelTheme.Lab,
        GateStyle = gate,
        HitPoints = 20000,
        SpawnedShare = 0.4f,
        FloorPixels = 3_000_000,
        Rooms = 24,
        SecretClosets = 0,
        Arsenal = [],
        NewWeapons = [],
        Weapons = [],
        Pickups = new Dictionary<KnownObject, int>(),
        EndMoney = 0,
        DonorLevel = "Level_05.map"
    };

    private static MissionModel CreateModel()
    {
        var structure = new MissionStructure
        {
            PathShare = Fitted(0.45, 0.3, 0.2, 0.9),
            TreeSizes = new FittedValue<IReadOnlyList<int>>([1, 1, 1, 2, 2, 3, 5], 7, false),
            TreeChains = new FittedValue<IReadOnlyList<double>>([0.5, 0.67, 1.0], 3, false),
            AnchorPositions = new FittedValue<IReadOnlyList<double>>([], 0, true),
            FirstPhaseShare = Fitted(0.5, 0.3, 0.3, 0.7),
            LaterPhaseShare = Fitted(0.4, 0.3, 0.2, 0.6),
            OpenedPhaseShare = Fitted(0.2, 0.3, 0.1, 0.4),
            EntranceShare = Fitted(0.3, 0.5, 0.1, 0.6),
            EnteredCounts = new FittedValue<IReadOnlyList<int>>([1, 3, 5], 3, false),
            EntrancesPerSource = Fitted(1.5, 0.3, 1, 3),
            InnerLockRate = new FittedValue<double>(0.15, 9, false),
            LoopRate = new FittedValue<double>(0.1, 9, false)
        };

        return new MissionModel
        {
            Structures = new Dictionary<LevelTheme, MissionStructure> { [LevelTheme.Lab] = structure, [LevelTheme.Military] = structure },
            TriggerSlope = new FittedValue<double>(1.2, 9, false),
            BlackoutSeal = new FittedValue<double>(0.5, 2, false),
            RubbleSeal = new FittedValue<double>(0.5, 2, false),
            LastReturn = new Dictionary<LevelStyle, FittedValue<double>> { [LevelStyle.Blackout] = new(0.5, 2, false), [LevelStyle.Fenced] = new(0.5, 5, false) }
        };
    }

    private static FittedValue<LogNormalFit> Fitted(double median, double sigma, double low, double high) =>
        new(new LogNormalFit(Math.Log(median), sigma, Math.Log(low), Math.Log(high)), 9, false);
}
