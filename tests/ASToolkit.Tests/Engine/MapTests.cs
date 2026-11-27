using System.Numerics;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MapTests
{
    private const int DroppedItemMaxLifetime = 22000;

    [Fact]
    public void Map_WithoutAPlacedMan_HasNoPlayerUntilTheScriptCreatesOne()
    {
        var map = EngineTestMap.CreateMap(placePlayer: false, script: """
            #include "maps\export.lgc"
            int man;
            main()
            {
              if( !man )
              {
                man = CreateSprite(9, 1000, 1000, 0);
                SetFlagman(0, man);
              }
            }
            """);

        Assert.False(map.Flagman(0) is not null);
        Assert.Null(map.Flagman(0));

        map.Step();

        Assert.True(map.Flagman(0) is not null);
        Assert.Equal(map.Flagman(0), map.Flagman(0));
        Assert.Equal(map.Flagman(0), map.Flagman(0)?.Link?.Owner);
    }

    [Fact]
    public void Step_PlayerKilledWithALifeLeft_IsRebornWithTheSpaceKey()
    {
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.Shotgun] = 23 }, default)
        { Items = new Dictionary<KnownObject, int> { [KnownObject.ExtraLife] = 1 } };
        var map = EngineTestMap.CreateMap(loadout: loadout);
        map.Play(default);
        var first = map.Flagman(0)!;
        var start = first.Position;
        Map.Action(first, ActionCode.Damage, first.Hp);
        for (int tick = 0; tick < 200 && map.Flagman(0) is not null; tick++)
        {
            map.Play(default);
        }

        Assert.Null(map.Flagman(0));
        for (int tick = 0; tick < 5 && (map.Flagman(0) is null || map.Flagman(0) == first); tick++)
        {
            map.Play(default);
        }

        Assert.NotSame(first, map.Flagman(0));
        Assert.Same(map.Flagman(0), map.Flagman(0));
        Assert.True(map.Flagman(0)!.IsAlive);
        Assert.Equal(start, map.Flagman(0)!.Position);
        var player = (Man)map.Flagman(0)!;
        Assert.Equal(23, player.SlotAmmo[KnownObject.Shotgun - KnownObject.Shotgun + 2] + (player.WeaponSlot == 2 ? player.GetAmmo() : 0));

        Map.Action(map.Flagman(0)!, ActionCode.Damage, map.Flagman(0)!.Hp);
        Assert.True(map.Flagman(0)!.IsAlive);
    }

    [Fact]
    public void Damage_KillingAMonsterCarryingAnItem_DropsItThenItExpires()
    {
        var map = EngineTestMap.CreateMap();
        map.Play(default);
        var position = map.Flagman(0)!.Position + new Vector3(500, 0, 0);
        var enemy = map.CreateSprite(map.FindVid(KnownObject.Monster1), position, default)!;
        Map.Action(enemy, ActionCode.DeleteAllItems);
        Map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Money);
        Map.Action(enemy, ActionCode.Damage, enemy.Hp);
        Sprite? dropped = null;
        for (int tick = 0; tick < 100 && dropped is null; tick++)
        {
            map.Play(default);
            dropped = map.UpdatedSprites.FirstOrDefault(sprite => sprite.Vid.Id == KnownObject.Money);
        }

        Assert.NotNull(dropped);
        int droppedAt = map.CurrentTime;
        while (map.CurrentTime <= droppedAt + DroppedItemMaxLifetime)
        {
            map.Play(default);
        }

        Assert.True(dropped.Animation >= SpriteAnimation.Death || dropped.ReferenceCount <= 0);
    }

    [Fact]
    public void Action_RepairOnAWoundedMonster_RestoresItsHp()
    {
        var map = EngineTestMap.CreateMap();
        map.Play(default);
        var enemy = map.CreateSprite(map.FindVid(KnownObject.Monster1), map.Flagman(0)!.Position + new Vector3(500, 0, 0), default)!;
        int maxHp = enemy.Hp;
        Map.Action(enemy, ActionCode.Damage, maxHp / 2);

        Map.Action(enemy, ActionCode.Repair);

        Assert.Equal(maxHp, enemy.Hp);
    }

    [Fact]
    public void Action_GetItemType_PicksACarriedItemOrNoneWithoutItems()
    {
        var map = EngineTestMap.CreateMap();
        map.Play(default);
        var enemy = map.CreateSprite(map.FindVid(KnownObject.Monster1), map.Flagman(0)!.Position + new Vector3(500, 0, 0), default)!;
        Map.Action(enemy, ActionCode.DeleteAllItems);

        Assert.Equal(-1, Map.Action(enemy, ActionCode.GetItemType));

        Map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Money);
        Map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Medkit);

        Assert.Contains(Map.Action(enemy, ActionCode.GetItemType), new[] { (int)KnownObject.Money, (int)KnownObject.Medkit });
    }

    [Fact]
    public void Step_PlayerCommand_MovesThePlayerWithoutRouteOrBot()
    {
        var map = EngineTestMap.CreateMap(camera: new Vector2(1000 - Playthrough.ProfileScreenSize.Width / 2f, 1000 - Playthrough.ProfileScreenSize.Height / 2f));
        map.Play(default);
        var destination = map.Flagman(0)!.Position.AsVector2() + new Vector2(300, 0);
        var command = new PlayerCommand(destination, PlayerCommand.GetCursor(map.Flagman(0)!.Position), false, Sprite.Pistols);
        for (int tick = 0; tick < 250; tick++)
        {
            map.Play(command);
        }

        Assert.NotEqual(Command.Move, map.Flagman(0)!.Command);
        Assert.True(map.Flagman(0)!.IsTouching(destination, map.EmptyVid.Half));
        for (int tick = 0; tick < 50; tick++)
        {
            map.Play(default);
        }

        Assert.Equal(0, map.Flagman(0)!.Speed);
        Assert.Equal(Map.LevelStartTime + 301 * FramePacer.DefaultFrameTime, map.CurrentTime);
    }

    [Fact]
    public void Step_MovesEnemiesWithoutAPlayerBot()
    {
        var map = EngineTestMap.CreateMap();
        var start = map.Flagman(0)!.Position.AsVector2() + new Vector2(200, 0);
        var enemy = map.CreateSprite(map.FindVid(KnownObject.Monster1), new Vector3(start, map.GetGroundZ(start)), new Angle(192))!;
        int health = map.Flagman(0)!.Hp;
        for (int tick = 0; tick < 300; tick++)
        {
            map.Play(default);
        }

        Assert.NotEqual(start, enemy.Position.AsVector2());
        Assert.True(map.Flagman(0)!.Hp < health);
    }

    [Theory]
    [InlineData(16)]
    public void Step_HighFrameRates_LetTheTankAlienReachThePlayer(int frameTime)
    {
        var map = EngineTestMap.CreateMap(frameTime: frameTime);
        var player = map.Flagman(0)!.Position.AsVector2();
        var start = player + new Vector2(350, 0);
        var tank = map.CreateSprite(map.FindVid(KnownObject.TankMonster), new Vector3(start, 0), Angle.FromOffset(player - start) + 64)!;

        while (map.CurrentTime < 6000)
        {
            map.Play(default);
        }

        Assert.InRange(Vector2.Distance(tank.Position.AsVector2(), player), 0, 100);
    }

    [Fact]
    public void GetDisplayFrameTimes_FillOneSecond()
    {
        Assert.Equal([16, 17, 17], FramePacer.GetDisplayFrameTimes(60).Take(3));
        Assert.Equal(1000, FramePacer.GetDisplayFrameTimes(60).Sum());
        Assert.Equal(1000, FramePacer.GetDisplayFrameTimes(144).Sum());
        Assert.All(FramePacer.GetDisplayFrameTimes(144), time => Assert.InRange(time, 6, 7));
    }
}
