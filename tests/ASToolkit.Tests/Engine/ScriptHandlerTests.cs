using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ScriptHandlerTests
{
    private const int MonsterArmy = 1;

    [Fact]
    public void Step_FreezeBonusTaken_FreezesNearbyMonstersInPlace()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = new ReplayRecorder();
        var world = EngineTestWorld.CreateWorld(kinds, recorder);
        var monster = world.AddMonster(kinds.Find(KnownObject.RedMonster1)!, world.Player.Position + new Vector3(300, 0, 0));
        world.AddPickup(world.Player.Position, KnownObject.FreezeMonsters, null);
        Step(world, 10);

        Assert.Equal(KnownObject.FrozenMonster1, monster.Kind.Id);
        Assert.True(monster.IsAlive);
        Assert.Equal(WeaponBehavior.None, monster.Behavior);
        Assert.Equal(0, monster.Speed);
        Assert.Contains(recorder.CreateReplay().Flashes, flash => flash.Color == 0x1010ff);
    }

    [Fact]
    public void Step_BombTaken_Deals300ToMonstersAndShakesTheScreen()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = new ReplayRecorder();
        var world = EngineTestWorld.CreateWorld(kinds, recorder);
        var tank = kinds.Find(KnownObject.RedTankMonster)!;
        var monster = world.AddMonster(tank, world.Player.Position + new Vector3(600, 0, 0));
        world.AddPickup(world.Player.Position, KnownObject.Bomb, null);
        Step(world, 10);

        var replay = recorder.CreateReplay();
        Assert.Equal(tank.MaxHp - 300, monster.Hp);
        Assert.Contains(replay.Flashes, flash => flash.Color == 0xffffff);
        Assert.NotEmpty(replay.Shakes);
        Assert.All(replay.Shakes, shake => Assert.InRange(Math.Abs(shake.Offset.X) + Math.Abs(shake.Offset.Y), 0, 8));

        int started = replay.Shakes[0].Time;
        Step(world, 2600 / world.FrameTime);
        Assert.InRange(replay.Shakes[^1].Time - started, 2400, 2600);
    }

    [Fact]
    public void Step_AdrenalineTaken_RaisesSpeedUntilTheNextScriptTick()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        float speed = world.Inventory.Speed;
        world.AddPickup(world.Player.Position, KnownObject.Adrenaline, null);
        var seen = new List<float>();
        for (int tick = 0; tick < 10; tick++)
        {
            world.Step(default);
            seen.Add(world.Inventory.Speed);
        }

        Assert.Contains(KnownObject.Adrenaline, world.TakenPickups);
        Assert.Equal(speed, seen[^1]);
        Assert.True(seen.Count(value => value > speed) <= 1);
    }

    [Fact]
    public void Step_DroneTaken_FollowsThePlayerAndRefusesASecond()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        world.AddPickup(world.Player.Position, KnownObject.Drone, null);
        Step(world, 10);

        var drone = Assert.Single(world.Monsters, unit => unit.Kind.Id == KnownObject.FlyingDrone);
        Assert.Equal(1, world.Inventory.Items[KnownObject.Drone]);

        world.AddPickup(world.Player.Position, KnownObject.Drone, null);
        Step(world, 10);
        Assert.Single(world.Pickups.ToArray(), pickup => pickup.Id == KnownObject.Drone);

        world.Player.Position += new Vector3(300, 0, 0);
        float before = Vector2.Distance(drone.Position.AsVector2(), world.Player.Position.AsVector2());
        Step(world, 100);
        Assert.True(Vector2.Distance(drone.Position.AsVector2(), world.Player.Position.AsVector2()) < before);

        world.Damage(drone, drone.Hp);
        Step(world, 100);
        Assert.Equal(0, world.Inventory.Items[KnownObject.Drone]);
    }

    [Fact]
    public void Constructor_DroneCarried_CreatesTheDroneAtTheStart()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default, new Dictionary<KnownObject, int> { [KnownObject.Drone] = 1 });
        var world = EngineTestWorld.CreateWorld(kinds, loadout: loadout);

        Assert.Single(world.Monsters, unit => unit.Kind.Id == KnownObject.FlyingDrone);
    }

    [Fact]
    public void Step_FreezeBulletKillsAMonster_FreezesItInsteadOfKillingIt()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var position = world.Player.Position + new Vector3(300, 0, 0);
        var monster = world.AddMonster(kinds.Find(KnownObject.Slug)!, position);
        monster.Hp = 10;
        var bullet = world.Effects.Spawn(KnownObject.FreezeBullet, position + new Vector3(40, 0, 0), default, world.Time, lifetime: 1);
        Assert.NotNull(bullet);
        Step(world, 20);

        Assert.Equal(KnownObject.FrozenSlug, monster.Kind.Id);
        Assert.True(monster.IsAlive);
        Assert.Equal(MonsterArmy, monster.Army);
    }

    [Fact]
    public void Step_FreezeRifleFiredAtAMonster_FreezesIt()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.FreezeRifle] = 100 }, default);
        var world = EngineTestWorld.CreateWorld(kinds, loadout: loadout);
        var monster = world.AddMonster(kinds.Find(KnownObject.Slug)!, world.Player.Position + new Vector3(250, 0, 0), new Facing(192));
        int slot = KnownObject.FreezeRifle - KnownObject.Shotgun + 2;
        for (int tick = 0; tick < 1500 && monster.Kind.Id != KnownObject.FrozenSlug; tick++)
        {
            world.Step(new PlayerCommand(null, PlayerCommand.GetCursor(monster.Position), true, slot));
        }

        Assert.Equal(KnownObject.FrozenSlug, monster.Kind.Id);
        Assert.True(monster.IsAlive);
    }

    [Fact]
    public void Step_FlamethrowerFiredAtAMonster_DamagesIt()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.FlameThrower] = 1000 }, default);
        var recorder = new ReplayRecorder();
        var world = EngineTestWorld.CreateWorld(kinds, recorder, loadout: loadout);
        var monster = world.AddMonster(kinds.Find(KnownObject.TankMonster)!, world.Player.Position + new Vector3(120, 0, 0), new Facing(192));
        int health = monster.Hp;
        int slot = KnownObject.FlameThrower - KnownObject.Shotgun + 2;
        for (int tick = 0; tick < 100; tick++)
        {
            world.Step(new PlayerCommand(null, PlayerCommand.GetCursor(monster.Position), true, slot));
            recorder.Capture(world);
        }

        Assert.True(monster.Hp < health);
    }

    [Fact]
    public void Step_ElectricBallBorn_DrainsTenHealthFromThePlayer()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        int health = world.Player.Hp;
        world.Effects.Spawn(KnownObject.ElectricBall, world.Player.Position + new Vector3(500, 0, 0), default, world.Time);
        Step(world, 5);

        Assert.Equal(health - 10, world.Player.Hp);
    }

    [Fact]
    public void Step_RescueMedkitSavesThePlayer_ProtectsForThreeSeconds()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default, new Dictionary<KnownObject, int> { [KnownObject.Medkit] = 1 });
        var world = EngineTestWorld.CreateWorld(kinds, loadout: loadout);
        world.Damage(world.Player, world.Player.Hp);
        Step(world, 2500 / world.FrameTime);
        int health = world.Player.Hp;
        world.Damage(world.Player, 5);

        Assert.True(world.Player.IsAlive);
        Assert.Equal(health, world.Player.Hp);
    }

    [Fact]
    public void Step_PlayerKilled_FlashesTheScreenRedOnce()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = new ReplayRecorder();
        var world = EngineTestWorld.CreateWorld(kinds, recorder);
        world.Damage(world.Player, world.Player.Hp);
        Step(world, 10);

        Assert.Single(recorder.CreateReplay().Flashes, flash => flash.Color == 0xff0000);
    }

    private static void Step(GameWorld world, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Step(default);
        }
    }
}
