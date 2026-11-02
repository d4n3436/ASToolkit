using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Generation;

public class AreaBuilderTests
{
    private const int Breaker = 3;
    private const int Seeds = 10;

    [Fact]
    public void Build_SmallMission_PlacesEveryRoomOnceAndReachesItFromTheStart()
    {
        var plan = new AreaBuilder(CreateModel(), new Random(3)).Build(CreateMission(locked: false), new AreaRequest(LevelTheme.Lab, 1, 1, 0, 0));

        Assert.NotNull(plan);
        Assert.Equal(Enumerable.Range(1, 8), plan.Regions.Where(region => region.Room is not null).Select(region => region.Room!.Value).Order());
        Assert.Equal(plan.Regions.Sum(region => region.Units.Count), plan.Occupancy.Count);
        Assert.Equal(1, plan.Stations[0].Room);
        Assert.Equal(4, plan.Stations[^1].Room);
        Assert.Null(plan.Gate);
        Assert.Equal(plan.Regions.Count, CountReached(plan));
    }

    [Fact]
    public void Build_LockedSecondPhase_GatesItsRoomsBehindTheBreaker()
    {
        var plan = new AreaBuilder(CreateModel(), new Random(5)).Build(CreateMission(locked: true), new AreaRequest(LevelTheme.Lab, 0, 0, 0, 0));

        Assert.NotNull(plan);
        Assert.Equal(GateKind.LockedDoors, plan.Gate);
        Assert.Equal(Breaker, plan.KeyRoom?.Room);
        Assert.All(plan.Regions.Where(region => region.Room is not null), region => Assert.Equal(region.Room is 4 or 7 or 8, region.IsGated));
        Assert.Contains(plan.Links, link => link.Kind == LinkKind.Door && plan.Regions[link.To].IsGated && !plan.Regions[link.From].IsGated);
    }

    [Fact]
    public void Build_DrawnLoops_JoinTwoRoomsOfOnePhaseOutsideTheTree()
    {
        var mission = CreateMission(locked: false);
        int built = 0;
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var builder = new AreaBuilder(CreateModel(), new Random(seed));
            var plan = builder.Build(mission, new AreaRequest(LevelTheme.Lab, 0, 0, 0, 0));

            Assert.NotNull(plan);
            Assert.Equal(1, builder.Counts.LoopsDrawn);
            var ends = plan.Links
                .Where(link => link.Role == MissionLinkKind.Loop)
                .SelectMany(link => new[] { plan.Regions[link.From], plan.Regions[link.To] })
                .Where(region => region.Room is not null)
                .Distinct()
                .ToArray();

            Assert.Equal(builder.Counts.LoopsBuilt * 2, ends.Length);
            if (ends.Length == 2)
            {
                Assert.Equal(ends[0].Phase, ends[1].Phase);
                Assert.DoesNotContain(mission.Links, link => link.Kind == MissionLinkKind.Tree && ends.Any(end => end.Room == link.From) && ends.Any(end => end.Room == link.To));
            }

            built += builder.Counts.LoopsBuilt;
        }

        Assert.True(built > 0);
    }

    [Fact]
    public void Build_ExtraEntrance_EntersTheBranchFromThePathAndDropsTheLinkItDisplaces()
    {
        var plan = new AreaBuilder(CreateModel(), new Random(3)).Build(CreateMission(locked: false, entrance: (3, 6)), new AreaRequest(LevelTheme.Lab, 0, 0, 0, 0));

        Assert.NotNull(plan);
        Assert.Contains(plan.Links, link => IsBetween(plan, link, 3, 6));
        Assert.DoesNotContain(plan.Links, link => IsBetween(plan, link, 5, 6));
    }

    [Fact]
    public void Build_ExtraEntranceIntoThePath_KeepsThePathWhole()
    {
        var plan = new AreaBuilder(CreateModel(), new Random(3)).Build(CreateMission(locked: false, entrance: (1, 3)), new AreaRequest(LevelTheme.Lab, 0, 0, 0, 0));

        Assert.NotNull(plan);
        Assert.Contains(plan.Links, link => IsBetween(plan, link, 2, 3));
    }

    [Fact]
    public void Build_SameSeed_LaysTheSamePlan()
    {
        var first = new AreaBuilder(CreateModel(), new Random(9)).Build(CreateMission(locked: true), new AreaRequest(LevelTheme.Lab, 1, 1, 0, 0));
        var second = new AreaBuilder(CreateModel(), new Random(9)).Build(CreateMission(locked: true), new AreaRequest(LevelTheme.Lab, 1, 1, 0, 0));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Occupancy.OrderBy(pair => pair.Key.U).ThenBy(pair => pair.Key.V), second.Occupancy.OrderBy(pair => pair.Key.U).ThenBy(pair => pair.Key.V));
    }

    private static bool IsBetween(LayoutPlan plan, PlanLink link, int first, int second)
    {
        int?[] rooms = [plan.Regions[link.From].Room, plan.Regions[link.To].Room];
        return rooms.Contains(first) && rooms.Contains(second);
    }

    private static int CountReached(LayoutPlan plan)
    {
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

        return reached.Count;
    }

    private static AreaModel CreateModel()
    {
        var size = new LogNormalFit(Math.Log(15), 0.3, Math.Log(15) - 0.6, Math.Log(15) + 0.6);
        var classes = new Dictionary<OutlineClass, float> { [OutlineClass.L] = 1, [OutlineClass.NotchedCorner] = 1, [OutlineClass.T] = 1, [OutlineClass.Cross] = 1 };
        var theme = new AreaThemeModel
        {
            RoleSizes = new[] { MissionRole.Start, MissionRole.End, MissionRole.Key, MissionRole.Path, MissionRole.Branch }
                .ToDictionary(role => role, role => (IReadOnlyList<float>)(role == MissionRole.Branch ? [0.6f, 0.8f, 1f] : [1f, 1.5f, 2f])),
            Scales = [40],
            PixelsPerUnit = 1,
            Halls = [new HallSize(26, 6)],
            Courts = [],
            HallShare = 0.3f,
            Turns = new TurnShares(0.4f, 0.5f, 0.1f),
            BranchTurn = 0.5f,
            RowGaps = [0, 0, 2],
            CutShares = Enumerable.Range(0, AreaThemeModel.SizeClassCount).Select(_ => new CutShare(0.3f, classes)).ToArray(),
            CutSizes = classes.Keys.ToDictionary(name => name, _ => size),
            Neck = new NeckFit(0.4f, 0.6f, new LogNormalFit(Math.Log(3.6), 0.2, Math.Log(3), Math.Log(4.5)), new LogNormalFit(Math.Log(2), 0.2, Math.Log(1.5), Math.Log(3))),
            Connector = new ConnectorFit(0.3f, [4], [4])
        };

        return new AreaModel(new Dictionary<LevelTheme, AreaThemeModel> { [LevelTheme.Lab] = theme, [LevelTheme.Military] = theme });
    }

    // Lay out a start, a hall and the breaker's room with a two-room branch, then a second phase through a lock with two side rooms, and an
    // extra entrance when asked
    private static MissionGraph CreateMission(bool locked, (int From, int To)? entrance = null)
    {
        var first = new LevelPhase(0, [1, 2, 3, 5, 6], 1, locked ? [Breaker] : [], [1, 2, 3], [], [new PhaseBranch([5, 6], [2], new Dictionary<int, int> { [5] = 1, [6] = 2 }, 0, true, 0)],
            0, false, [], []);
        var second = new LevelPhase(1, [4, 7, 8], 4, [], [4], [], [new PhaseBranch([7, 8], [4], new Dictionary<int, int> { [7] = 1, [8] = 1 }, 0, true, 0)], 0, false, [], []);
        var keys = locked ? new[] { new PhaseKey(LandmarkKind.Breaker, Breaker) } : [];
        var progression = new LevelProgression([first, second], keys, locked ? [4] : [], 4, [1, 2, 3, 5, 6, 4, 7, 8], [], []);
        var links = new MissionLink[]
        {
            new(1, 2, false, MissionLinkKind.Tree),
            new(2, 3, false, MissionLinkKind.Tree),
            new(2, 5, false, MissionLinkKind.Tree),
            new(5, 6, false, MissionLinkKind.Tree),
            new(3, 4, locked, MissionLinkKind.Tree),
            new(4, 7, false, MissionLinkKind.Tree),
            new(4, 8, false, MissionLinkKind.Tree),
            new(7, 8, false, MissionLinkKind.Loop)
        };

        if (entrance is not null)
        {
            links = links.Append(new MissionLink(entrance.Value.From, entrance.Value.To, false, MissionLinkKind.Entrance)).ToArray();
        }

        var roles = new Dictionary<int, MissionRole>
        {
            [1] = MissionRole.Start,
            [2] = MissionRole.Path,
            [3] = locked ? MissionRole.Key : MissionRole.Path,
            [4] = MissionRole.End,
            [5] = MissionRole.Branch,
            [6] = MissionRole.Reward,
            [7] = MissionRole.Branch,
            [8] = MissionRole.Branch
        };
        return new MissionGraph(progression, links, roles);
    }
}
