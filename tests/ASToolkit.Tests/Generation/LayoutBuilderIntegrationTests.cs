using ASToolkit.Core.Design;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Reference;
using Xunit;
using ASToolkit.Core.Survey;

namespace ASToolkit.Tests.Generation;

[Trait("Category", "Integration")]
public class LayoutBuilderIntegrationTests
{
    private static readonly int[] LabLevels = [2, 3, 4, 5];

    [Fact]
    public void Measure_CampaignLabs_FindsEveryRole()
    {
        var references = LoadReferences();
        foreach (int level in LabLevels)
        {
            var profile = LevelProfile.Measure(references.GetSurvey(ReferenceLevelSet.GetCampaignName(level)), ThemeSettings.Lab);
            Assert.NotEmpty(profile.Halls);
            Assert.True(profile.NeckUnits > 0, $"level {level} has no neck");
            Assert.NotEmpty(profile.Branches);
            Assert.Equal(profile.Branches.Count, profile.BranchEntrances.Count);
            Assert.All(profile.BranchEntrances.Zip(profile.Branches), pair => Assert.InRange(pair.First, 1, pair.Second.Count));
        }
    }

    [Fact]
    public void Build_CampaignLabs_KeepsTheOriginalsRoomGraph()
    {
        var references = LoadReferences();
        var profiles = LabLevels.Select(level => LevelProfile.Measure(references.GetSurvey(ReferenceLevelSet.GetCampaignName(level)), ThemeSettings.Lab)).ToArray();
        var misses = new List<string>();
        for (int index = 0; index < LabLevels.Length; index++)
        {
            int level = LabLevels[index];
            var target = references.GetTarget(level);
            var original = RoomGraph.Build(target.OriginalSurvey);
            var gate = CreateGate(target);
            var rooms = new List<int>();
            var hubs = new List<int>();
            for (int seed = 1; seed <= 10; seed++)
            {
                var rng = new Random(seed);
                LayoutPlan? plan = null;
                for (int attempt = 0; plan is null && attempt < 20; attempt++)
                {
                    plan = LayoutBuilder.Build(rng, profiles[index].Sample(rng, profiles, profiles[index].Units, gate.IsWing ? gate.Share : 0), new LayoutRequest(gate, 2, 2, 1));
                }

                Assert.NotNull(plan);
                Assert.Equal(gate.IsWing ? GateKind.LockedDoors : gate.Kind, plan.Gate);
                Assert.NotNull(plan.KeyRoom);
                var walkable = plan.Regions.Where(region => region.Kind is not (RegionKind.SecretCloset or RegionKind.SpawnCloset)).Select(region => region.Id).ToHashSet();
                var links = plan.Links.Where(link => walkable.Contains(link.From) && walkable.Contains(link.To)).ToArray();
                rooms.Add(walkable.Count);
                hubs.Add(walkable.Max(id => links.Count(link => link.From == id || link.To == id)));
                float locked = plan.Regions.Where(region => region.IsGated && walkable.Contains(region.Id)).Sum(region => region.Units.Count) / (float)plan.Regions.Where(region => walkable.Contains(region.Id)).Sum(region => region.Units.Count);
                if (gate is { Kind: GateKind.LockedDoors, IsWing: false } && Math.Abs(locked - gate.Share) > 0.15f)
                {
                    misses.Add($"level {level} seed {seed} locks {locked:P0}, original {gate.Share:P0}");
                }
            }

            int medianRooms = rooms.Order().ElementAt(rooms.Count / 2);
            int medianHub = hubs.Order().ElementAt(hubs.Count / 2);
            if (Math.Abs(medianRooms - original.Rooms) > original.Rooms / 4)
            {
                misses.Add($"level {level} has {medianRooms} rooms, original {original.Rooms}");
            }

            if (Math.Abs(medianHub - original.HubDoors) > 3)
            {
                misses.Add($"level {level} has a hub of {medianHub} doors, original {original.HubDoors}");
            }
        }

        Assert.Empty(misses);
    }

    private static GateRequest CreateGate(LevelTarget target) => target.Style switch
    {
        LevelStyle.LockedDoors => new GateRequest(GateKind.LockedDoors, target.LockedShare ?? 0.5f, 0),
        LevelStyle.Blackout => new GateRequest(GateKind.Blackout, target.OriginalSurvey.Gates.First(gate => gate.Kind == LevelStyle.Blackout).GatedShare, 5200),
        _ => new GateRequest(GateKind.LockedDoors, 0.25f, 0, IsWing: true)
    };

    private static ReferenceLevelSet LoadReferences()
    {
        string game = TestGameFolder.RequireFolder();
        return ReferenceLevelSet.Load(game, ObjectTable.Load(Path.Combine(game, "objects.res")));
    }
}
