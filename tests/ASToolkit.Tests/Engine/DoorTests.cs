using ASToolkit.Core.Engine;
using ASToolkit.Core.Simulation;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class DoorTests
{
    [Theory]
    [InlineData(KnownObject.SmallDoor, 4, 16)]
    [InlineData(KnownObject.DoubleDoor, 2, 36)]
    public void Step_PlayerNearADoor_SinksItByItsScriptStepEachTick(KnownObject id, int step, int ticks)
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var door = new MergedMapSprite(1, id, new Vector3(1000, 1000, 0), 0, 0, []);
        var recorder = new ReplayRecorder();
        var world = EngineTestWorld.CreateWorld(kinds, recorder, sprites: [door]);
        var doorFrames = recorder.CreateReplay().DoorFrames;
        world.Step(default);

        Assert.Single(doorFrames);
        for (int tick = 1; tick <= ticks + 1; tick++)
        {
            world.Step(default);
            Assert.Equal(-Math.Min(tick, ticks) * step, doorFrames[^1].Position.Z);
        }
    }

    [Fact]
    public void Move_DoorRisingAroundAUnit_LetsTheUnitMoveOut()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var kind = kinds.Find(KnownObject.SmallDoor)!;
        var walker = kinds.Find(KnownObject.PlayerStart)!;
        var position = new Vector2(1000);
        var map = new MovementMap();
        map.AddObstacle(kind.Object, new Vector3(position, 0));
        var grid = new UnitGrid(Vector2.Zero, new Vector2(2000));
        var unit = new Unit(0, walker, position.AsVector3(), 0, walker.MaxHp);
        grid.Add(unit);
        var movement = new UnitCollision(map, grid);

        Assert.False(map.CanStand(walker.Object, position.AsVector3()));
        Assert.True(movement.Move(unit, (position + Vector2.UnitX).AsVector3()));
        Assert.Equal(position + Vector2.UnitX, unit.Position.AsVector2());
    }

    [Fact]
    public void Step_OccupiedDoor_StaysOpenUntilTheDoorsLock()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var door = new MergedMapSprite(1, KnownObject.SmallDoor, new Vector3(1000, 1000, 0), 0, 0, []);
        var recorder = new ReplayRecorder();
        var world = EngineTestWorld.CreateWorld(kinds, recorder, sprites: [door]);
        var doorFrames = recorder.CreateReplay().DoorFrames;
        world.AddMonster(kinds.Find(KnownObject.Monster1)!, door.Position);
        for (int tick = 0; tick <= 16; tick++)
        {
            world.Step(default);
        }

        Assert.Equal(-64, doorFrames[^1].Position.Z);
        world.Player.Position = new Vector3(3000, 1000, 0);
        int left = world.Time;
        while (world.Time <= left + 3000)
        {
            world.Step(default);
        }

        Assert.Equal(-64, doorFrames[^1].Position.Z);
        world.StartBlackout();
        world.Step(default);
        Assert.Equal(-60, doorFrames[^1].Position.Z);
    }
}
