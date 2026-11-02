using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Generation;

[Trait("Category", "Integration")]
public class AreaBuilderIntegrationTests
{
    private const int Seeds = 3;
    private const int PlanSeeds = 10;
    private const float MaxRedrawShare = 0.05f;

    private static readonly Lazy<ReferenceLevelSet> References = new(() =>
    {
        string game = TestGameFolder.RequireFolder();
        return ReferenceLevelSet.Load(game, ObjectTable.Load(Path.Combine(game, "objects.res")));
    });

    private static readonly Lazy<Dictionary<GenerationStyle, AreaModel>> Models = new(() => Enum.GetValues<GenerationStyle>().ToDictionary(style => style,
        style => AreaFitter.Fit(StyleFitter.GetFittingLevels(style).Select(References.Value.GetLevel).ToArray(), style)));

    public static TheoryData<string> OriginalLevels()
    {
        var levels = new TheoryData<string>();
        for (int number = 2; number <= 10; number++)
        {
            levels.Add($"Level_{number:00}.map");
        }

        for (int number = 2; number <= 5; number++)
        {
            levels.Add($"ADDON/Level_{number:00}.map");
            levels.Add($"ADDON2/Level_{number:00}.map");
        }

        return levels;
    }

    [Theory]
    [MemberData(nameof(OriginalLevels))]
    public void Build_OriginalMission_PlacesEveryRoomReachablyWithoutOverlapsOrSteps(string level)
    {
        var survey = References.Value.GetSurvey(level);
        var mission = CreateMission(survey);
        var rooms = mission.Progression.Phases
            .SelectMany(phase => phase.Places)
            .ToHashSet();

        for (int seed = 1; seed <= Seeds; seed++)
        {
            var builder = new AreaBuilder(GetModel(level), new Random(seed));
            var plan = builder.Build(mission, new AreaRequest(survey.Setting, 2, 2, 0, survey.Places.Sum(place => place.AreaPixels)));

            Assert.NotNull(plan);
            Assert.Equal(rooms.Order(), plan.Regions.Where(region => region.Room is not null).Select(region => region.Room!.Value).Order());
            Assert.Equal(plan.Regions.Sum(region => region.Units.Count), plan.Occupancy.Count);
            Assert.All(plan.Links.Where(link => link.Kind is not (LinkKind.SecretWall or LinkKind.SpawnWall)),
                link => Assert.True(link.Boundary.Length >= GetDoorMin(survey), $"{level} seed {seed} link {link.From}-{link.To} shares {link.Boundary.Length}"));

            var reached = new HashSet<int> { plan.Stations[0].Id };
            var queue = new Queue<int>(reached);
            while (queue.Count > 0)
            {
                int region = queue.Dequeue();
                foreach (var link in plan.Links.Where(link => link.From == region || link.To == region))
                {
                    int other = link.From == region ? link.To : link.From;
                    if (reached.Add(other))
                    {
                        queue.Enqueue(other);
                    }
                }
            }

            Assert.Equal(plan.Regions.Count, reached.Count);
            if (plan.Gate is GateKind.LockedDoors or GateKind.Fences)
            {
                Assert.NotNull(plan.GateLink);
                Assert.True(plan.GateLink.Kind is LinkKind.Door or LinkKind.Fence, $"{level} seed {seed} gate link is {plan.GateLink.Kind}");
                Assert.NotEqual(plan.Regions[plan.GateLink.From].IsGated, plan.Regions[plan.GateLink.To].IsGated);
            }

            Assert.Equal(mission.Progression.Keys.Any(key => key.Kind == LandmarkKind.Generator), plan.GeneratorRooms.Count > 0);
            Assert.Equal(plan.Gate == GateKind.Blackout, plan.BlackoutLink is not null);
            if (survey.Setting == LevelTheme.Lab)
            {
                Assert.All(plan.Regions.Where(region => region.Room is not null), region => Assert.False(HasStep(region.Units), $"{level} seed {seed} room {region.Room} steps by one unit"));
            }
        }
    }

    [Fact]
    public void Build_OriginalMissionsWithLoops_BuildsTheLoopsAndKeepsEveryRoomReachable()
    {
        int drawn = 0, built = 0;
        foreach (string level in OriginalLevels().Select(row => row.Data))
        {
            var survey = References.Value.GetSurvey(level);
            var mission = CreateMission(survey, loops: true);
            var builder = new AreaBuilder(GetModel(level), new Random(13));
            var plan = builder.Build(mission, new AreaRequest(survey.Setting, 0, 0, 0, survey.Places.Sum(place => place.AreaPixels)));

            Assert.NotNull(plan);
            Assert.All(plan.Links, link => Assert.True(link.Boundary.Length >= GetDoorMin(survey), $"{level} link {link.From}-{link.To} shares {link.Boundary.Length}"));
            Assert.All(plan.Links.Where(link => link.Role == MissionLinkKind.Loop),
                link => Assert.Equal(plan.Regions[link.From].Phase, plan.Regions[link.To].Phase));
            drawn += builder.Counts.LoopsDrawn;
            built += builder.Counts.LoopsBuilt;
        }

        Assert.True(drawn > 0);
        Assert.InRange(built, 1, drawn);
    }

    [Fact]
    public void Build_SameSeed_LaysTheSamePlan()
    {
        var survey = References.Value.GetSurvey("Level_03.map");
        var mission = CreateMission(survey);

        var first = new AreaBuilder(GetModel("Level_03.map"), new Random(7)).Build(mission, new AreaRequest(survey.Setting, 2, 2, 0, survey.Places.Sum(place => place.AreaPixels)));
        var second = new AreaBuilder(GetModel("Level_03.map"), new Random(7)).Build(mission, new AreaRequest(survey.Setting, 2, 2, 0, survey.Places.Sum(place => place.AreaPixels)));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Occupancy.OrderBy(pair => pair.Key.U).ThenBy(pair => pair.Key.V), second.Occupancy.OrderBy(pair => pair.Key.U).ThenBy(pair => pair.Key.V));
        Assert.Equal(first.Links.Select(link => (link.From, link.To, link.Kind)), second.Links.Select(link => (link.From, link.To, link.Kind)));
    }

    [Fact]
    public void Build_Originals_RedrawsUnderTheCapAndBuildsWhatItDraws()
    {
        var builders = new List<AreaBuilder>();
        foreach (string level in OriginalLevels().Select(row => row.Data))
        {
            var survey = References.Value.GetSurvey(level);
            var mission = CreateMission(survey);
            var builder = new AreaBuilder(GetModel(level), new Random(11));
            for (int seed = 0; seed < Seeds; seed++)
            {
                builder.Build(mission, new AreaRequest(survey.Setting, 0, 0, 0, survey.Places.Sum(place => place.AreaPixels)));
            }

            builders.Add(builder);
        }

        var counts = builders.Select(builder => builder.Counts).ToArray();
        Assert.Equal(0, counts.Sum(count => count.FailedBuilds));
        Assert.InRange(counts.Sum(count => count.CompositeRedraws) / (float)counts.Sum(count => count.Composites), 0, MaxRedrawShare);
        Assert.InRange(counts.Sum(count => count.LayoutRedraws) / (float)counts.Sum(count => count.Layouts), 0, MaxRedrawShare);
        Assert.InRange(counts.Sum(count => count.NecksBuilt) / (float)Math.Max(1, counts.Sum(count => count.NecksDrawn)), 1 - MaxRedrawShare, 1);
    }

    [Fact]
    public void Fit_Campaign_ReadsTheLabCourtsAndNoMilitaryCourt()
    {
        var model = Models.Value[GenerationStyle.Campaign];

        Assert.Equal(10, model.GetTheme(LevelTheme.Lab).Courts.Count);
        Assert.Empty(model.GetTheme(LevelTheme.Military).Courts);
    }

    [Fact]
    public void Build_DrawnCampaignMissions_KeepsCriticalPathsWholeRoomsApartCourtsEmptyAndLabOutlinesWithoutSteps()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var model = new StyleFitter(objects, GameTable.Load(Path.Combine(game, "Maps", "common_table.lgc"))).Fit(References.Value, GenerationStyle.Campaign);
        var grammar = new MissionGrammar(model.Mission);
        int rings = 0;
        for (int seed = 1; seed <= PlanSeeds; seed++)
        {
            foreach (var brief in new CampaignPlanner(model.Campaign).Plan(new Random(seed)).Levels.Where(brief => brief.Level > 1))
            {
                var rng = new Random(seed * 100 + brief.Level);
                var builder = new AreaBuilder(model.Areas, rng);
                var objectives = new MissionObjectives(brief.GateStyle == LevelStyle.Blackout ? 5 : 0, brief.GateStyle == LevelStyle.Fenced ? 2 : 0);
                var mission = grammar.Draw(rng, brief, objectives);
                var plan = builder.Build(mission, new AreaRequest(brief.Theme, 0, 0, 0, brief.FloorPixels));
                if (plan is null)
                {
                    continue;
                }

                var pathLinks = mission.Links
                    .Where(link => link.Kind == MissionLinkKind.Tree)
                    .Where(link => mission.Progression.Phases.Any(phase => !phase.IsReturnPass && phase.Path.Contains(link.From) && phase.Path.Contains(link.To)));

                foreach (var link in pathLinks)
                {
                    Assert.True(IsJoined(plan, link.From, link.To), $"seed {seed} level {brief.Level} leaves the path link {link.From}-{link.To} unbuilt");
                }

                rings += builder.Counts.Rings;
                Assert.Equal(plan.Regions.Sum(region => region.Units.Count), plan.Occupancy.Count);
                Assert.DoesNotContain(plan.Occupancy.Keys, plan.Courts.Contains);
                if (brief.Theme == LevelTheme.Lab)
                {
                    Assert.All(plan.Regions.Where(region => region.Room is not null), region => Assert.False(HasStep(region.Units), $"seed {seed} level {brief.Level} room {region.Room} steps by one unit"));
                }
            }
        }

        Assert.True(rings > 0);
    }

    private static bool IsJoined(LayoutPlan plan, int first, int second)
    {
        var regionOf = plan.Regions.Where(region => region.Room is not null).ToDictionary(region => region.Room!.Value, region => region.Id);
        var corridors = plan.Regions.Where(region => region.Room is null).Select(region => region.Id).ToHashSet();
        var near = new[] { first, second }
            .Select(room => plan.Links
                .Where(link => link.From == regionOf[room] || link.To == regionOf[room])
                .Select(link => link.From == regionOf[room] ? link.To : link.From)
                .ToHashSet())
            .ToArray();

        return near[0].Contains(regionOf[second]) || near[0].Any(region => corridors.Contains(region) && near[1].Contains(region));
    }

    private static int GetDoorMin(LevelSurvey survey) => (survey.Setting == LevelTheme.Lab ? ThemeSettings.Lab : ThemeSettings.Military).MinShared;

    private static AreaModel GetModel(string level) => Models.Value[level.StartsWith("ADDON", StringComparison.Ordinal) ? GenerationStyle.AddOn : GenerationStyle.Campaign];

    // Read the original's door graph as the mission's links, keeping a walk tree from the start as tree links and the other links inside a phase
    // as loops when asked
    private static MissionGraph CreateMission(LevelSurvey survey, bool loops = false)
    {
        var progression = new ProgressionMeasurer(survey).Measure();
        var graph = DoorGraph.Create(survey);
        var locks = survey.Connections
            .Where(connection => connection.Kind is ConnectionKind.LockedDoor or ConnectionKind.Fence)
            .Select(connection => (Math.Min(connection.A, connection.B), Math.Max(connection.A, connection.B)))
            .ToHashSet();

        var phaseOf = new Dictionary<int, int>();
        foreach (var phase in progression.Phases)
        {
            foreach (int room in phase.Places)
            {
                phaseOf.TryAdd(room, phase.Index);
            }
        }

        var tree = new HashSet<(int, int)>();
        var seen = new HashSet<int> { progression.Phases[0].Path[0] };
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            foreach (int other in graph.Neighbors[room].Order().Where(seen.Add))
            {
                tree.Add((Math.Min(room, other), Math.Max(room, other)));
                queue.Enqueue(other);
            }
        }

        var links = new List<MissionLink>();
        foreach ((int room, var others) in graph.Neighbors)
        {
            foreach (int other in others.Where(other => other > room))
            {
                bool isLoop = loops && !tree.Contains((room, other)) && phaseOf.GetValueOrDefault(room, -1) == phaseOf.GetValueOrDefault(other, -2);
                if (!loops || isLoop || tree.Contains((room, other)))
                {
                    links.Add(new MissionLink(room, other, locks.Contains((room, other)), isLoop ? MissionLinkKind.Loop : MissionLinkKind.Tree));
                }
            }
        }

        var roles = new Dictionary<int, MissionRole>();
        foreach (var phase in progression.Phases)
        {
            foreach (int room in phase.Places)
            {
                roles.TryAdd(room, phase.Path.Contains(room) ? MissionRole.Path : MissionRole.Branch);
            }
        }

        foreach (var key in progression.Keys.Where(key => key.Kind != LandmarkKind.BlackoutTrigger))
        {
            roles[key.Place] = MissionRole.Key;
        }

        roles[progression.Phases[0].Path[0]] = MissionRole.Start;
        roles[progression.End] = MissionRole.End;
        return new MissionGraph(progression, links, roles);
    }

    private static bool HasStep(IReadOnlySet<Cell> cells)
    {
        var runs = new Dictionary<(bool Vertical, int Line, bool Inside), List<int>>();
        foreach (var cell in cells)
        {
            foreach ((int du, int dv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (cells.Contains(new Cell(cell.U + du, cell.V + dv)))
                {
                    continue;
                }

                var key = du != 0 ? (true, cell.U + Math.Max(0, du), du > 0) : (false, cell.V + Math.Max(0, dv), dv > 0);
                if (!runs.TryGetValue(key, out var list))
                {
                    list = [];
                    runs[key] = list;
                }

                list.Add(du != 0 ? cell.V : cell.U);
            }
        }

        return runs.Values.Any(list =>
        {
            list.Sort();
            return list.Where((value, index) => (index == 0 || list[index - 1] != value - 1) && (index == list.Count - 1 || list[index + 1] != value + 1)).Any();
        });
    }
}
