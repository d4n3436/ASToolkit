using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Generation;

public class DoorStageTests
{
    private const int Seeds = 150;

    private static readonly LevelStyle[] Gates = [LevelStyle.LockedDoors, LevelStyle.Blackout, LevelStyle.Fenced, LevelStyle.Ungated];

    private static readonly BuildingFault[] StageFaults = [BuildingFault.LockSourceOffPath, BuildingFault.DarkRoomPastBreaker, BuildingFault.CrossedBackWithoutSites];

    [Fact]
    public void Choose_ValidBuildings_KeepsEveryGuarantee()
    {
        foreach (var (building, plan) in ChooseAll())
        {
            Assert.Empty(plan.Faults.Keys.Except(StageFaults));
            Assert.Equal(building.Gate, plan.Gate);
            Assert.Empty(DoorGuarantees.Check(building, plan));
        }
    }

    [Fact]
    public void Choose_ValidBuildings_KeepsGivenPhasesLocksAndKeys()
    {
        foreach (var (building, plan) in ChooseAll())
        {
            foreach (var room in building.Rooms.Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room))
            {
                Assert.Contains(room.Id, plan.Phases[room.Phase]);
            }

            foreach ((int from, int to) in building.Locks)
            {
                Assert.Equal(DoorKind.Lock, plan.Doors[new RoomPair(from, to)]);
            }

            var keyTargets = building.Gate switch
            {
                LevelStyle.Blackout => plan.Targets.Skip(1).Take(1),
                LevelStyle.Ungated => [],
                _ => plan.Targets.Take(plan.KeyCount)
            };

            Assert.Equal(building.Keys.Order(), keyTargets.Order());
        }
    }

    [Fact]
    public void Choose_SameBuilding_ChoosesTheSameDoors()
    {
        foreach (var gate in Gates)
        {
            var building = RandomBuildings.Create(new Random(5), gate);
            var first = new DoorStage().Choose(building);
            var second = new DoorStage().Choose(building);

            Assert.Equal(first.Doors.OrderBy(pair => pair.Key), second.Doors.OrderBy(pair => pair.Key));
            Assert.Equal(first.Targets, second.Targets);
        }
    }

    [Fact]
    public void Choose_ValidBuildings_LaysEveryOpenWallADoorInsideAPhaseAndALockAcrossPhases()
    {
        foreach (var (building, plan) in ChooseAll())
        {
            var closed = building.ClosedWalls
                .Select(wall => Order(wall.A, wall.B))
                .ToHashSet();

            var phaseOf = building.Rooms
                .Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room && room.Phase >= 0)
                .ToDictionary(room => room.Id, room => room.Phase);

            var given = building.Locks
                .Select(pair => Order(pair.From, pair.To))
                .Concat(building.Shortcuts.Select(pair => Order(pair.From, pair.To)))
                .ToHashSet();

            foreach (var contact in building.Contacts.Select(contact => Order(contact.A, contact.B)).Distinct())
            {
                if (contact.A == contact.B || closed.Contains(contact) || given.Contains(contact) || !phaseOf.ContainsKey(contact.A) || !phaseOf.TryGetValue(contact.B, out int value))
                {
                    continue;
                }

                Assert.Equal(phaseOf[contact.A] == value ? DoorKind.Door : DoorKind.Lock, plan.Doors[new RoomPair(contact.A, contact.B)]);
            }
        }
    }

    [Fact]
    public void Choose_FaultyBuilding_StillLaysDoors()
    {
        var building = RandomBuildings.Create(new Random(3), LevelStyle.Blackout);
        var regular = building.Rooms
            .Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room)
            .Select(room => room.Id)
            .ToHashSet();

        var locked = building.Contacts.First(pair => regular.Contains(pair.A) && regular.Contains(pair.B));
        var faulty = building with
        {
            Rooms = building.Rooms.Select(room => room with { Phase = 0 }).ToArray(),
            Locks = [(locked.A, locked.B)]
        };

        var plan = new DoorStage().Choose(faulty);

        var failures = BuildingChecks.Find(faulty);
        Assert.Contains(failures, failure => failure.EndsWith("phases for Blackout", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.EndsWith("not forward", StringComparison.Ordinal));
        Assert.Equal(LevelStyle.Ungated, plan.Gate);
        Assert.Empty(DoorGuarantees.Check(faulty, plan));
    }

    [Fact]
    public void Choose_BlackoutWithoutTrigger_ReachesEveryRoom()
    {
        for (int seed = 0; seed < Seeds; seed++)
        {
            var building = RandomBuildings.Create(new Random(seed), LevelStyle.Blackout) with { Trigger = null };
            var plan = new DoorStage().Choose(building);

            Assert.Contains("blackout trigger or breaker outside the blackout phase", BuildingChecks.Find(building));
            Assert.Equal(LevelStyle.Ungated, plan.Gate);
            Assert.Empty(DoorGuarantees.Check(building, plan));
        }
    }

    [Fact]
    public void Write_ValidBuildings_GivesEveryRoomARoleAndEveryDoorALink()
    {
        foreach (var (building, plan) in ChooseAll())
        {
            var mission = MissionWriter.Write(plan);
            var graph = mission.Mission;

            Assert.Equal(plan.Phases.SelectMany(phase => phase).Order(), graph.Roles.Keys.Order());
            Assert.Equal(plan.Doors.Count, graph.Links.Count);
            Assert.Equal(plan.Doors.Count, mission.Realized.Count);
            Assert.Equal(MissionRole.Start, graph.Roles[plan.Start]);
            Assert.Equal(plan.Phases.SelectMany(phase => phase).Count() - 1, mission.Parent.Count);
            Assert.Equal(building.Gate == LevelStyle.Blackout ? 1 : 0, graph.Progression.Seals.Count(seal => seal.Kind == SealKind.DoorsClose));
            int locked = plan.Doors.Values.Count(kind => kind != DoorKind.Door) - (building.Gate == LevelStyle.Blackout ? 1 : 0);
            Assert.Equal(locked, graph.Links.Count(link => link.IsLocked));
        }
    }

    private static (int A, int B) Order(int a, int b) => a < b ? (a, b) : (b, a);

    private static IEnumerable<(BuildingPlan Building, DoorPlan Plan)> ChooseAll()
    {
        foreach (var gate in Gates)
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rng = new Random(seed);
                var building = RandomBuildings.Create(rng, gate);
                yield return (building, new DoorStage().Choose(building));
            }
        }
    }
}
