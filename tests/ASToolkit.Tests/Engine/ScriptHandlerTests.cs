using System.Numerics;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ScriptHandlerTests
{
    [Fact]
    public void Step_FreezeBonusTaken_FreezesNearbyMonstersInPlace()
    {
        var vids = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(vids);
        var monster = (Unit)map.CreateSprite(vids.Find(KnownObject.RedMonster1), map.Player.Position + new Vector3(300, 0, 0), default)!;
        map.CreateSprite(vids.Find(KnownObject.FreezeMonsters), map.Player.Position, default);
        Step(map, 10);

        Assert.Equal(KnownObject.FrozenMonster1, monster.Vid.Id);
        Assert.True(monster.IsAlive);
        Assert.Equal(WeaponBehavior.None, monster.Behavior);
        Assert.Equal(0, monster.Speed);
        Assert.Equal(0x1010ff, map.Session.Graphics.EffectFirst[(int)ScreenEffect.Nuke]);
    }

    [Fact]
    public void Step_MapStartingWithAnEarthquake_ShakesTheScreenFromTheFirstTick()
    {
        var objects = TestGameFolder.CreateCatalog();
        var calm = EngineTestMap.CreateMap(objects);
        var shaking = EngineTestMap.CreateMap(objects, environment: MapEnvironment.Earthquake);

        Step(calm, 1);
        Step(shaking, 1);

        Assert.False(calm.Session.Graphics.Environment.HasFlag(MapEnvironment.Earthquake));
        Assert.True(shaking.Session.Graphics.Environment.HasFlag(MapEnvironment.Earthquake));
    }

    [Fact]
    public void Step_BombTaken_Deals300ToMonstersAndShakesTheScreen()
    {
        var vids = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(vids);
        var screen = new ScreenLog();
        var tank = vids.Find(KnownObject.RedTankMonster)!;
        var monster = map.CreateSprite(tank, map.Player.Position + new Vector3(600, 0, 0), default)!;
        map.CreateSprite(vids.Find(KnownObject.Bomb), map.Player.Position, default);
        Step(map, 10, screen);

        Assert.False(monster.IsAlive);
        Assert.Equal(tank.MaxHp, monster.Hp);
        Assert.Contains(screen.Flashes, flash => flash.Color == 0xffffff);
        Assert.NotEmpty(screen.Shakes);

        Step(map, 2600 / FramePacer.DefaultFrameTime, screen);
        Assert.InRange(screen.Shakes[^1] - screen.Shakes[0], 2400, 2600);
    }

    [Fact]
    public void Step_AdrenalineTaken_RaisesSpeedUntilTheNextScriptTick()
    {
        var vids = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(vids);
        float speed = map.Player.MaxSpeed;
        map.CreateSprite(vids.Find(KnownObject.Adrenaline), map.Player.Position, default);
        var log = new PlayLog(map);
        var seen = new List<float>();
        for (int tick = 0; tick < 10; tick++)
        {
            map.Play(default);
            log.Update(map);
            seen.Add(map.Player.MaxSpeed);
        }

        Assert.Contains(KnownObject.Adrenaline, log.TakenPickups);
        Assert.Equal(speed, seen[^1]);
        Assert.True(seen.Count(value => value > speed) <= 1);
    }

    [Fact]
    public void Step_DroneTaken_FollowsThePlayerAndRefusesASecond()
    {
        var vids = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(vids);
        Step(map, 1);
        map.CreateSprite(vids.Find(KnownObject.Drone), map.Player.Position, default);
        Step(map, 10);

        var drone = Assert.Single(map.Monsters, sprite => sprite.Vid.Id == KnownObject.FlyingDrone);

        var second = map.CreateSprite(vids.Find(KnownObject.Drone), map.Player.Position, default)!;
        Step(map, 10);
        Assert.True(second.Animation < SpriteAnimation.Death);

        map.Player.Position += new Vector3(300, 0, 0);
        float before = Vector2.Distance(drone.Position.AsVector2(), map.Player.Position.AsVector2());
        Step(map, 100);
        Assert.True(Vector2.Distance(drone.Position.AsVector2(), map.Player.Position.AsVector2()) < before);

        Map.Action(drone, ActionCode.Damage, drone.Hp);
        Step(map, 100);
        Assert.DoesNotContain(map.Monsters, sprite => sprite.Vid.Id == KnownObject.FlyingDrone && sprite.IsAlive);
    }

    [Fact]
    public void Constructor_DroneCarried_CreatesTheDroneAtTheStart()
    {
        var vids = TestGameFolder.CreateCatalog();
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default, new Dictionary<KnownObject, int> { [KnownObject.Drone] = 1 });
        var map = EngineTestMap.CreateMap(vids, loadout: loadout);
        Step(map, 1);

        Assert.Single(map.Monsters, sprite => sprite.Vid.Id == KnownObject.FlyingDrone);
    }

    [Fact]
    public void Step_FreezeBulletKillsAMonster_FreezesItInsteadOfKillingIt()
    {
        var vids = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(vids);
        Step(map, 1);
        var position = map.Player.Position + new Vector3(300, 0, 0);
        var monster = map.CreateSprite(vids.Find(KnownObject.Slug), position, default)!;
        monster.Hp = 10;
        var bullet = map.CreateSprite(vids.Find(KnownObject.FreezeBullet), position + new Vector3(40, 0, 0), default);
        Assert.NotNull(bullet);
        bullet.Lifetime = 1;
        Step(map, 20);

        Assert.Equal(KnownObject.FrozenSlug, monster.Vid.Id);
        Assert.True(monster.IsAlive);
        Assert.Equal(Army.Monsters, monster.Army);
    }

    [Fact]
    public void Step_FreezeRifleFiredAtAMonster_FreezesIt()
    {
        const int slot = KnownObject.FreezeRifle - KnownObject.Shotgun + 2;
        
        var vids = TestGameFolder.CreateCatalog();
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.FreezeRifle] = 100 }, default);
        var map = EngineTestMap.CreateMap(vids, loadout: loadout);
        Step(map, 1);
        var monster = map.CreateSprite(vids.Find(KnownObject.Slug), map.Player.Position + new Vector3(250, 0, 0), new Angle(192))!;

        for (int tick = 0; tick < 1500 && monster.Vid.Id != KnownObject.FrozenSlug; tick++)
        {
            map.Play(new PlayerCommand(null, PlayerCommand.GetCursor(monster.Position), true, slot));
        }

        Assert.Equal(KnownObject.FrozenSlug, monster.Vid.Id);
        Assert.True(monster.IsAlive);
    }

    [Fact]
    public void Step_FlamethrowerFiredAtAMonster_DamagesIt()
    {
        const int slot = KnownObject.FlameThrower - KnownObject.Shotgun + 2;

        var vids = TestGameFolder.CreateCatalog();
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.FlameThrower] = 1000 }, default);
        var map = EngineTestMap.CreateMap(vids, loadout: loadout);
        Step(map, 1);
        var monster = map.CreateSprite(vids.Find(KnownObject.TankMonster), map.Player.Position + new Vector3(120, 0, 0), new Angle(192))!;
        int health = monster.Hp;

        for (int tick = 0; tick < 100; tick++)
        {
            map.Play(new PlayerCommand(null, PlayerCommand.GetCursor(monster.Position), true, slot));
        }

        Assert.True(monster.Hp < health);
    }

    [Fact]
    public void Step_ElectricBallBorn_DrainsTenHealthFromThePlayer()
    {
        var vids = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(vids);
        Step(map, 1);
        int health = map.Player.Hp;
        map.CreateSprite(vids.Find(KnownObject.ElectricBall), map.Player.Position + new Vector3(500, 0, 0), default);
        Step(map, 5);

        Assert.Equal(health - 10, map.Player.Hp);
    }

    [Fact]
    public void Step_RescueMedkitSavesThePlayer_ProtectsForThreeSeconds()
    {
        var vids = TestGameFolder.CreateCatalog();
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default, new Dictionary<KnownObject, int> { [KnownObject.Medkit] = 1 });
        var map = EngineTestMap.CreateMap(vids, loadout: loadout);
        Step(map, 1);
        Map.Action(map.Player, ActionCode.Damage, map.Player.Hp);
        Step(map, 2500 / FramePacer.DefaultFrameTime);
        int health = map.Player.Hp;
        Map.Action(map.Player, ActionCode.Damage, 5);

        Assert.True(map.Player.IsAlive);
        Assert.Equal(health, map.Player.Hp);
    }

    [Fact]
    public void Step_PlayerKilled_FlashesTheScreenRedOnce()
    {
        var vids = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(vids);
        var screen = new ScreenLog();
        Step(map, 1);
        Map.Action(map.Player, ActionCode.Damage, map.Player.Hp);
        Step(map, 40, screen);

        Assert.Single(screen.Flashes, flash => flash.Color == 0xff0000);
    }

    private static void Step(Map map, int ticks, ScreenLog? screen = null)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            map.Play(default);
            screen?.Read(map);
        }
    }
}
