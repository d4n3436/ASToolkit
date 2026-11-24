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
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects, placePlayer: false, script: """
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

        Assert.False(map.HasPlayer);
        Assert.Null(map.GetFlagman(0));

        map.Step();

        Assert.True(map.HasPlayer);
        Assert.Equal(map.Player, map.GetFlagman(0));
        Assert.Equal(map.Player, map.Weapons.Torso?.Owner);
    }

    [Fact]
    public void Step_PlayerKilledWithALifeLeft_IsRebornWithTheSpaceKey()
    {
        var objects = TestGameFolder.CreateCatalog();
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.Shotgun] = 23 }, default)
        { Items = new Dictionary<KnownObject, int> { [KnownObject.ExtraLife] = 1 } };
        var map = EngineTestMap.CreateMap(objects, loadout: loadout);
        map.Play(default);
        var first = map.Player;
        var start = first.Position;
        map.Action(first, ActionCode.Damage, first.Hp);
        for (int tick = 0; tick < 200 && map.GetFlagman(0) is not null; tick++)
        {
            map.Play(default);
        }

        Assert.Null(map.GetFlagman(0));
        for (int tick = 0; tick < 5 && (!map.HasPlayer || map.Player == first); tick++)
        {
            map.Play(default);
        }

        Assert.NotSame(first, map.Player);
        Assert.Same(map.Player, map.GetFlagman(0));
        Assert.True(map.Player.IsAlive);
        Assert.Equal(start, map.Player.Position);
        var player = (Man)map.Player;
        Assert.Equal(23, player.SlotAmmo[KnownObject.Shotgun - KnownObject.Shotgun + 2] + (player.WeaponSlot == 2 ? player.GetAmmo() : 0));

        map.Action(map.Player, ActionCode.Damage, map.Player.Hp);
        Assert.True(map.Player.IsAlive);
    }

    [Fact]
    public void Damage_KillingAMonsterCarryingAnItem_DropsItThenItExpires()
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects);
        map.Play(default);
        var position = map.Player.Position + new Vector3(500, 0, 0);
        var enemy = map.AddSprite(objects.Find(KnownObject.Monster1), position, default)!;
        enemy.Items.Clear();
        map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Money);
        map.Action(enemy, ActionCode.Damage, enemy.Hp);
        Sprite? dropped = null;
        for (int tick = 0; tick < 100 && dropped is null; tick++)
        {
            map.Play(default);
            dropped = map.UpdatedSprites.FirstOrDefault(sprite => sprite.Definition.Id == KnownObject.Money);
        }

        Assert.NotNull(dropped);
        int droppedAt = map.Time;
        while (map.Time <= droppedAt + DroppedItemMaxLifetime)
        {
            map.Play(default);
        }

        Assert.True(dropped.Animation >= SpriteAnimation.Death || dropped.ReferenceCount <= 0);
    }

    [Fact]
    public void Action_RepairOnAWoundedMonster_RestoresItsHp()
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects);
        map.Play(default);
        var enemy = map.AddSprite(objects.Find(KnownObject.Monster1), map.Player.Position + new Vector3(500, 0, 0), default)!;
        int maxHp = enemy.Hp;
        map.Action(enemy, ActionCode.Damage, maxHp / 2);

        map.Action(enemy, ActionCode.Repair);

        Assert.Equal(maxHp, enemy.Hp);
    }

    [Fact]
    public void Action_GetItemType_PicksACarriedItemOrNoneWithoutItems()
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects);
        map.Play(default);
        var enemy = map.AddSprite(objects.Find(KnownObject.Monster1), map.Player.Position + new Vector3(500, 0, 0), default)!;
        enemy.Items.Clear();

        Assert.Equal(-1, map.Action(enemy, ActionCode.GetItemType));

        map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Money);
        map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Medkit);

        Assert.Contains(map.Action(enemy, ActionCode.GetItemType), new[] { (int)KnownObject.Money, (int)KnownObject.Medkit });
    }

    [Fact]
    public void Step_PlayerCommand_MovesThePlayerWithoutRouteOrBot()
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects, camera: new Vector2(1000 - GameSession.DefaultScreenWidth / 2f, 1000 - GameSession.DefaultScreenHeight / 2f));
        map.Play(default);
        var destination = map.Player.Position.AsVector2() + new Vector2(300, 0);
        var command = new PlayerCommand(destination, PlayerCommand.GetCursor(map.Player.Position), false, Sprite.Pistols);
        for (int tick = 0; tick < 250; tick++)
        {
            map.Play(command);
        }

        Assert.NotEqual(Command.Move, map.Player.Command);
        Assert.True(map.Player.IsTouching(destination, map.Definitions.Empty.Half));
        for (int tick = 0; tick < 50; tick++)
        {
            map.Play(default);
        }

        Assert.Equal(0, map.Player.Speed);
        Assert.Equal(GameClock.LevelStartTime + 301 * FramePacer.DefaultFrameTime, map.Time);
    }

    [Fact]
    public void Step_MovesEnemiesWithoutAPlayerBot()
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects);
        var start = map.Player.Position.AsVector2() + new Vector2(200, 0);
        var enemy = map.AddSprite(objects.Find(KnownObject.Monster1), new Vector3(start, map.Movement.GetHeight(start)), new Angle(192))!;
        int health = map.Player.Hp;
        for (int tick = 0; tick < 300; tick++)
        {
            map.Play(default);
        }

        Assert.NotEqual(start, enemy.Position.AsVector2());
        Assert.True(map.Player.Hp < health);
    }

    [Theory]
    [InlineData(16)]
    public void Step_HighFrameRates_LetTheTankAlienReachThePlayer(int frameTime)
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects, frameTime: frameTime);
        var player = map.Player.Position.AsVector2();
        var start = player + new Vector2(350, 0);
        var tank = map.AddSprite(objects.Find(KnownObject.TankMonster), new Vector3(start, 0), Angle.FromOffset(player - start) + 64)!;

        while (map.Time < 6000)
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
