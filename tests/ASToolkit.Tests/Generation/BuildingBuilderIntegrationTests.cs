using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Generation;

[Trait("Category", "Integration")]
public class BuildingBuilderIntegrationTests
{
    private const int Seeds = 20;

    private static readonly Lazy<ReferenceLevelSet> References = new(() => TestGameFolder.References);

    private static readonly Lazy<IReadOnlyList<RoomFootprint>> Footprints = new(() =>
        RoomFootprintFitter.Fit(StyleFitter.GetLayoutLevels(GenerationStyle.AddOn).Select(References.Value.GetLevel).ToArray()));

    private static BuildingPlan[] BuildSeeds(BuildingRequest request)
    {
        var buildings = new BuildingPlan[Seeds];
        Parallel.For(1, Seeds + 1, seed => buildings[seed - 1] = new BuildingBuilder(new Random(seed), Footprints.Value).Build(request));
        return buildings;
    }

    // Counts the closets more than twice the level's median closet, the walled rooms entered through a jammed door beside a breakable wall
    private static int CountWalledRooms(ReferenceLevel level)
    {
        int[] closets = level.Atlas.Rooms
            .Where(room => room.Type == Core.Rooms.RoomType.Closet)
            .Select(room => room.Place.Cells.Count)
            .Order()
            .ToArray();

        return closets.Length == 0 ? 0 : closets.Count(cells => cells > 2 * closets[closets.Length / 2]);
    }

    public static TheoryData<string> Originals { get; } = new(StyleFitter.GetLayoutLevels(GenerationStyle.AddOn).Where(name => ReferenceLevelSet.GetBuilding(name) != Building.Grounds));

    [Theory]
    [MemberData(nameof(Originals))]
    public void Build_OriginalsProgram_KeepsTheBuildingsGuarantees(string level)
    {
        var original = References.Value.GetLevel(level);
        var kind = ReferenceLevelSet.GetBuilding(level);
        var keys = new ProgressionMeasurer(original.Survey).Measure().Keys.Select(key => key.Kind).ToHashSet();
        var request = new BuildingRequest
        {
            Building = kind,
            Level = int.Parse(Path.GetFileNameWithoutExtension(level)[6..]),
            Gate = keys.Contains(LandmarkKind.Generator) ? LevelStyle.Fenced
                : keys.Contains(LandmarkKind.BlackoutTrigger) ? LevelStyle.Blackout
                : keys.Contains(LandmarkKind.Breaker) ? LevelStyle.LockedDoors
                : LevelStyle.Ungated,
            Rooms = original.Atlas.Rooms.Count(room => room.Type is not (Core.Rooms.RoomType.Hall or Core.Rooms.RoomType.Passage or Core.Rooms.RoomType.Closet)) + CountWalledRooms(original),
            Style = level.StartsWith("ADDON", StringComparison.Ordinal) ? GenerationStyle.AddOn : GenerationStyle.Campaign,
            IsIntro = level is "Level_02.map" or "ADDON/Level_02.map" or "ADDON2/Level_04.map",
            HasSites = false,
            SecretClosets = original.Survey.Places.Count(place => place.Kind == PlaceKind.SecretCloset),
            SpawnClosets = original.Survey.Places.Count(place => place.Kind == PlaceKind.SpawnCloset)
        };

        var buildings = BuildSeeds(request);
        for (int seed = 1; seed <= Seeds; seed++)
        {
            AssertGuarantees(buildings[seed - 1], request, $"{level} seed {seed}");
            var doors = new DoorStage().Choose(buildings[seed - 1]);
            var broken = DoorGuarantees.Check(buildings[seed - 1], doors);
            Assert.True(broken.Count == 0, $"{level} seed {seed}: {string.Join(", ", broken)}");
            var inconsistent = BuildingChecks.Find(buildings[seed - 1]);
            Assert.True(inconsistent.Count == 0, $"{level} seed {seed}: {string.Join(", ", inconsistent)}");
            Assert.DoesNotContain(BuildingFault.LockSourceOffPath, doors.Faults.Keys);
            Assert.DoesNotContain(BuildingFault.DarkRoomPastBreaker, doors.Faults.Keys);
        }
    }

    [Fact]
    public void Build_OfficeBlock_RunsTheDarkToTheBreakerAndOpensTheRest()
    {
        var request = new BuildingRequest
        {
            Building = Building.OfficeBlock, Gate = LevelStyle.Blackout, Rooms = 26, Style = GenerationStyle.AddOn, IsIntro = false, HasSites = true,
            SecretClosets = 2, SpawnClosets = 4
        };

        var buildings = BuildSeeds(request);
        for (int seed = 1; seed <= Seeds; seed++)
        {
            var building = buildings[seed - 1];
            var phaseOf = building.Rooms.ToDictionary(room => room.Id, room => room.Phase);
            int[] run = building.Rooms.Where(room => room is { Kind: BuildingRoomKind.Room, Phase: 1 }).Select(room => room.Id).ToArray();
            Assert.InRange(run.Length, 6, 8);
            Assert.Equal(1, phaseOf[building.Keys.Single()]);
            Assert.Equal(1, phaseOf[building.Trigger!.Value]);
            Assert.Equal(2, phaseOf[building.End!.Value]);
            var doors = new DoorStage().Choose(building);
            Assert.Equal(LevelStyle.Blackout, doors.Gate);
            Assert.Empty(DoorGuarantees.Check(building, doors));
            Assert.Empty(BuildingChecks.Find(building));
            Assert.DoesNotContain(BuildingFault.DarkRoomPastBreaker, doors.Faults.Keys);
        }
    }

    [Theory]
    [InlineData(Building.ConferenceBuilding, LevelStyle.Blackout)]
    [InlineData(Building.School, LevelStyle.LockedDoors)]
    [InlineData(Building.OfficeBlock, LevelStyle.Ungated)]
    public void Build_AddOnBuildingWithAnotherGate_CarriesTheGate(Building kind, LevelStyle gate)
    {
        var request = new BuildingRequest
        {
            Building = kind, Gate = gate, Rooms = 16, Style = GenerationStyle.AddOn, IsIntro = false, HasSites = false, SecretClosets = 2, SpawnClosets = 1
        };

        var buildings = BuildSeeds(request);
        for (int seed = 1; seed <= Seeds; seed++)
        {
            AssertGuarantees(buildings[seed - 1], request, $"{kind} {gate} seed {seed}");
            Assert.Equal(gate == LevelStyle.Ungated, buildings[seed - 1].Keys.Count == 0);
        }
    }

    [Fact]
    public void Build_SameSeed_BuildsTheSameBuilding()
    {
        var request = new BuildingRequest
        {
            Building = Building.UndergroundLab, Gate = LevelStyle.LockedDoors, Rooms = 20, Style = GenerationStyle.AddOn, IsIntro = false, HasSites = false, SecretClosets = 3,
            SpawnClosets = 2
        };

        var first = new BuildingBuilder(new Random(7), Footprints.Value).Build(request);
        var second = new BuildingBuilder(new Random(7), Footprints.Value).Build(request);

        Assert.Equal(first.Rooms.SelectMany(room => room.Rects), second.Rooms.SelectMany(room => room.Rects));
        Assert.Equal(first.Contacts, second.Contacts);
    }

    private static void AssertGuarantees(BuildingPlan building, BuildingRequest request, string name)
    {
        var owner = new Dictionary<Cell, int>();
        foreach (var room in building.Rooms)
        {
            foreach (var cell in room.Rects.SelectMany(rect => rect.Units))
            {
                Assert.True(owner.TryAdd(cell, room.Id) || owner[cell] == room.Id, $"{name}: rooms {owner.GetValueOrDefault(cell)} and {room.Id} overlap at {cell}");
            }
        }

        var walkable = building.Rooms.Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room).Select(room => room.Id).ToHashSet();
        var neighbors = building.Contacts
            .SelectMany(pair => new[] { (pair.A, pair.B), (pair.B, pair.A) })
            .ToLookup(pair => pair.Item1, pair => pair.Item2);

        var reached = new HashSet<int> { building.Start };
        var stack = new Stack<int>([building.Start]);
        while (stack.Count > 0)
        {
            foreach (int next in neighbors[stack.Pop()])
            {
                if (reached.Add(next))
                {
                    stack.Push(next);
                }
            }
        }

        Assert.True(walkable.SetEquals(reached), $"{name}: rooms {string.Join(", ", walkable.Except(reached))} out of reach of the start");
        int regular = building.Rooms.Count(room => room.Kind == BuildingRoomKind.Room && room.Type != Core.Rooms.RoomType.Passage);
        Assert.True(regular >= request.Rooms * 0.7, $"{name}: {regular} rooms built of {request.Rooms}");
        foreach (var room in building.Rooms.Where(room => room.Kind is BuildingRoomKind.SecretCloset or BuildingRoomKind.SpawnCloset or BuildingRoomKind.SecretRoom))
        {
            Assert.NotNull(room.Host);
            var host = building.Rooms[room.Host.Value];
            Assert.True(room.Rects.Any(rect => host.Rects.Any(other => rect.GetSharedEdges(other).Length > 0)), $"{name}: side room {room.Id} off its host's wall");
        }

        foreach ((int from, int to) in building.Locks)
        {
            Assert.True(building.Rooms[to].Phase > building.Rooms[from].Phase, $"{name}: lock {from}-{to} not into a later phase");
        }

        Assert.All(building.Keys, key => Assert.True(walkable.Contains(key), $"{name}: key {key} is no walkable room"));
        switch (request.Gate)
        {
            case LevelStyle.LockedDoors or LevelStyle.Fenced:
                Assert.Single(building.Keys);
                Assert.Equal(0, building.Rooms[building.Keys[0]].Phase);
                Assert.NotNull(building.GateRoom);
                Assert.NotEmpty(building.Locks);
                break;
            case LevelStyle.Blackout:
                Assert.Single(building.Keys);
                Assert.NotNull(building.Trigger);
                Assert.Equal(1, building.Rooms[building.Trigger.Value].Phase);
                Assert.Equal(1, building.Rooms[building.Keys[0]].Phase);
                break;
        }
    }
}
