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
        var map = EngineTestMap.CreateMap(definitions, sprites: [door]);
        var body = map.FindPlacementSprite(door)!;
        map.Step(default);

        Assert.Equal(0, body.Position.Z);
        for (int tick = 1; tick <= ticks + 1; tick++)
        {
            map.Step(default);
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
        var collector = new SpriteCollector(new System.Drawing.SizeF(2000, 2000), new Vector2(130));
        var sprite = new Sprite(0, walker, position.AsVector3(), 0, walker.MaxHp);
        collector.InsertToHash(new Sprite(1, definition, position.AsVector3(), 0, definition.MaxHp));
        collector.InsertToHash(sprite);
        var movement = new SpriteCollision(new MovementMap(), collector);

        Assert.True(movement.Move(sprite, (position + Vector2.UnitX).AsVector3()));
        Assert.Equal(position + Vector2.UnitX, sprite.Position.AsVector2());
    }

    [Fact]
    public void Step_OccupiedDoor_StaysOpenUntilTheDoorsLock()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var door = new MergedMapSprite(1, KnownObject.SmallDoor, new Vector3(1000, 1000, 0), 0, 0, []);
        var map = EngineTestMap.CreateMap(definitions, sprites: [door]);
        var body = map.FindPlacementSprite(door)!;
        map.AddMonster(definitions.Find(KnownObject.Monster1)!, door.Position);
        for (int tick = 0; tick <= 16; tick++)
        {
            map.Step(default);
        }

        Assert.Equal(-64, body.Position.Z);
        map.Player.Position = new Vector3(3000, 1000, 0);
        int left = map.Time;
        while (map.Time <= left + 3000)
        {
            map.Step(default);
        }

        Assert.Equal(-64, body.Position.Z);
        map.WriteScriptNumber("DoorsIsOpen"u8, 0);
        map.Step(default);
        Assert.Equal(-60, body.Position.Z);
    }
}
