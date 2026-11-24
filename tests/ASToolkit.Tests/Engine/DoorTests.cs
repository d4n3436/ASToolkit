using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class DoorTests
{
    [Theory]
    [InlineData(KnownObject.SmallDoor, 4, 16)]
    [InlineData(KnownObject.DoubleDoor, 2, 36)]
    public void Step_PlayerNearADoor_SinksItByItsScriptStepEachTick(KnownObject id, int step, int ticks)
    {
        var vids = TestGameFolder.CreateCatalog();
        var door = new MergedMapSprite(1, id, new Vector3(1000, 1000, 0), 0, 0, []);
        var map = EngineTestMap.CreateMap(vids, sprites: [door]);
        var body = map.FindFirstSprite(id);

        Assert.Equal(0f, body.Position.Z, 0f);
        for (int tick = 1; tick <= ticks + 1; tick++)
        {
            map.Play(default);
            Assert.Equal(-Math.Min(tick, ticks) * step, body.Position.Z, 0f);
        }
    }

    [Fact]
    public void Move_DoorRisingAroundAUnit_LetsTheUnitMoveOut()
    {
        var vids = TestGameFolder.CreateCatalog();
        var vid = vids.Find(KnownObject.SmallDoor)!;
        var walker = vids.Find(KnownObject.PlayerStart)!;
        var position = new Vector2(1000);
        var map = EngineTestMap.CreateMap(vids);
        var sprite = new Sprite(walker, position.AsVector3(), default, null, map);
        _ = new Sprite(vid, position.AsVector3(), default, null, map);

        var next = (position + Vector2.UnitX).AsVector3();
        Assert.Null(sprite.CanPlaceWithCrush(ref next));
        sprite.ChangeCoor(next);
        Assert.Equal(position + Vector2.UnitX, sprite.Position.AsVector2());
    }

    [Fact]
    public void Step_OccupiedDoor_StaysOpenUntilTheDoorsLock()
    {
        var vids = TestGameFolder.CreateCatalog();
        var door = new MergedMapSprite(1, KnownObject.SmallDoor, new Vector3(1000, 1000, 0), 0, 0, []);
        var map = EngineTestMap.CreateMap(vids, sprites: [door]);
        var body = map.FindFirstSprite(KnownObject.SmallDoor);
        map.CreateSprite(vids.Find(KnownObject.Monster1), door.Position, default);
        for (int tick = 0; tick <= 16; tick++)
        {
            map.Play(default);
        }

        Assert.Equal(-64f, body.Position.Z, 0f);
        map.Player.Position = new Vector3(3000, 1000, 0);
        int left = map.Time;
        while (map.Time <= left + 3000)
        {
            map.Play(default);
        }

        Assert.Equal(-64f, body.Position.Z, 0f);
        map.WriteScriptNumber("DoorsIsOpen"u8, 0);
        map.Play(default);
        Assert.Equal(-60f, body.Position.Z, 0f);
    }
}
