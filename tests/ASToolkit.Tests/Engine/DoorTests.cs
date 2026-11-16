using ASToolkit.Core.Engine;
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
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var door = new MergedMapSprite(1, id, new Vector3(1000, 1000, 0), 0, 0, []);
        var world = EngineTestWorld.CreateWorld(definitions, sprites: [door]);
        var body = world.FindPlacementUnit(door)!;
        world.Step(default);

        Assert.Equal(0, body.Position.Z);
        for (int tick = 1; tick <= ticks + 1; tick++)
        {
            world.Step(default);
            Assert.Equal(-Math.Min(tick, ticks) * step, body.Position.Z);
        }
    }

    [Fact]
    public void Move_DoorRisingAroundAUnit_LetsTheUnitMoveOut()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var definition = definitions.Find(KnownObject.SmallDoor)!;
        var walker = definitions.Find(KnownObject.PlayerStart)!;
        var position = new Vector2(1000);
        var grid = new UnitGrid(new System.Drawing.SizeF(2000, 2000), new Vector2(130));
        var unit = new Unit(0, walker, position.AsVector3(), 0, walker.MaxHp);
        grid.Add(new Unit(1, definition, position.AsVector3(), 0, definition.MaxHp));
        grid.Add(unit);
        var movement = new UnitCollision(new MovementMap(), grid);

        Assert.True(movement.Move(unit, (position + Vector2.UnitX).AsVector3()));
        Assert.Equal(position + Vector2.UnitX, unit.Position.AsVector2());
    }

    [Fact]
    public void Step_OccupiedDoor_StaysOpenUntilTheDoorsLock()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var door = new MergedMapSprite(1, KnownObject.SmallDoor, new Vector3(1000, 1000, 0), 0, 0, []);
        var world = EngineTestWorld.CreateWorld(definitions, sprites: [door]);
        var body = world.FindPlacementUnit(door)!;
        world.AddMonster(definitions.Find(KnownObject.Monster1)!, door.Position);
        for (int tick = 0; tick <= 16; tick++)
        {
            world.Step(default);
        }

        Assert.Equal(-64, body.Position.Z);
        world.Player.Position = new Vector3(3000, 1000, 0);
        int left = world.Time;
        while (world.Time <= left + 3000)
        {
            world.Step(default);
        }

        Assert.Equal(-64, body.Position.Z);
        world.WriteScriptNumber("DoorsIsOpen"u8, 0);
        world.Step(default);
        Assert.Equal(-60, body.Position.Z);
    }
}
