using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Engine;

/// <summary>
/// Represents checks of the world independently of automated player decisions.
/// </summary>
public class WorldTests
{
    private const int RespawnProtection = 3000;
    private const int DroppedItemMaxLifetime = 22000;

    /// <summary>
    /// Verifies a fatal hit consumes a rescue medkit and grants temporary protection.
    /// </summary>
    [Fact]
    public void Damage_FatalHitWithMedkit_RescuesThePlayer()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        Assert.True(world.Inventory.TryTakePickup(KnownObject.Medkit));
        world.Damage(world.Player, world.Player.Hp);

        Assert.Equal(world.Player.Kind.MaxHp, world.Player.Hp);
        Assert.False(world.Inventory.Items.ContainsKey(KnownObject.Medkit));
        world.Damage(world.Player, world.Player.Hp);
        Assert.True(world.Player.IsAlive);

        for (int tick = 0; tick < 500; tick++)
        {
            world.Step(default);
        }

        world.Damage(world.Player, world.Player.Hp);
        Assert.False(world.Player.IsAlive);
    }

    /// <summary>
    /// Verifies respawning spends a life while retaining ammunition, money, and the checkpoint.
    /// </summary>
    [Fact]
    public void Damage_FatalHitWithLivesLeft_RespawnsWithRemainingEquipment()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var loadout = new PlayerLoadout(KnownObject.GreenArmor,
            new Dictionary<KnownObject, int> { [KnownObject.Shotgun] = 23 }, default)
        { Items = new Dictionary<KnownObject, int> { [KnownObject.ExtraLife] = 1 } };
        var world = EngineTestWorld.CreateWorld(objects, loadout: loadout);
        world.Player.Position += new Vector3(100, 0, 0);
        world.SetRespawnPoint();
        var checkpoint = world.Player.Position.AsVector2();
        world.Player.Position += new Vector3(100, 0, 0);
        world.Inventory.TryTakePickup(KnownObject.Money);
        int money = world.Inventory.Money;
        world.Damage(world.Player, 10000);
        Assert.False(world.Player.IsAlive);
        world.Step(default);

        Assert.Equal(1, world.Respawns);
        Assert.Equal(checkpoint, world.Player.Position.AsVector2());
        Assert.Equal(world.Player.Kind.MaxHp, world.Player.Hp);
        Assert.Equal(0, world.Inventory.Armor);
        Assert.Equal(23, world.Inventory.GetRemainingAmmo()[KnownObject.Shotgun]);
        Assert.Equal(money, world.Inventory.Money);
        Assert.False(world.Inventory.Items.ContainsKey(KnownObject.ExtraLife));
        world.Damage(world.Player, 10000);
        Assert.True(world.Player.IsAlive);

        while (world.Time <= RespawnProtection)
        {
            world.Step(default);
        }

        world.Damage(world.Player, 10000);
        world.Step(default);
        Assert.False(world.Player.IsAlive);
        Assert.Equal(1, world.Respawns);
    }

    /// <summary>
    /// Verifies a monster's stored item stays where it drops and expires.
    /// </summary>
    [Fact]
    public void Damage_KillingAMonster_DropsLootThatExpires()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        var enemy = world.AddMonster(objects.Find(KnownObject.Monster1)!,
            world.Player.Position + new Vector3(500, 0, 0), dropItem: KnownObject.Money);
        world.Damage(enemy, enemy.Hp);
        Assert.False(enemy.IsAlive);
        Assert.Empty(world.Pickups.ToArray());
        while (world.Pickups.Length == 0 && world.Time <= enemy.DiedAt + enemy.Kind.Layout.GetFrameDuration(SpriteAnimation.Death) + GameClock.DefaultFrameTime)
        {
            world.Step(default);
        }

        var pickup = Assert.Single(world.Pickups.ToArray());
        Assert.Equal(KnownObject.Money, pickup.Id);
        Assert.True(pickup.IsDropped);
        var initialPosition = pickup.Position;

        world.Step(default);
        Assert.Equal(initialPosition, pickup.Position);
        int droppedAt = world.Time;
        while (world.Time <= droppedAt + DroppedItemMaxLifetime)
        {
            world.Step(default);
        }

        Assert.Empty(world.Pickups.ToArray());
        Assert.Equal(0, world.Inventory.Money);
    }

    /// <summary>
    /// Verifies movement and braking from explicit player commands.
    /// </summary>
    [Fact]
    public void Step_PlayerCommand_MovesThePlayerWithoutRouteOrBot()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        var destination = world.Player.Position.AsVector2() + new Vector2(300, 0);
        var command = new PlayerCommand(destination, PlayerCommand.GetCursor(world.Player.Position), false, PlayerInventory.Pistols);
        for (int tick = 0; tick < 250; tick++)
        {
            world.Step(command);
        }

        Assert.True(world.PlayerMovement.HasReached(world.Player, destination));
        for (int tick = 0; tick < 50; tick++)
        {
            world.Step(default);
        }

        Assert.Equal(0, world.Player.Speed);
        Assert.Equal(300 * GameClock.DefaultFrameTime, world.Time);
    }

    /// <summary>
    /// Verifies enemy acquisition, movement, and attacks without player automation.
    /// </summary>
    [Fact]
    public void Step_MovesEnemiesWithoutAPlayerBot()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        var start = world.Player.Position.AsVector2() + new Vector2(200, 0);
        var enemy = world.AddMonster(objects.Find(KnownObject.Monster1)!, new Vector3(start, world.Movement.GetHeight(start)), facing: new Facing(192));
        int health = world.Player.Hp;
        for (int tick = 0; tick < 300; tick++)
        {
            world.Step(default);
        }

        Assert.NotEqual(start, enemy.Position.AsVector2());
        Assert.True(world.Player.Hp < health);
    }

    /// <summary>
    /// Verifies replay capture preserves the world state and random sequence.
    /// </summary>
    [Fact]
    public void Step_Recording_KeepsTheWorldsBehavior()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = new ReplayRecorder();
        var recorded = EngineTestWorld.CreateWorld(objects, recorder);
        var unrecorded = EngineTestWorld.CreateWorld(objects);
        var kind = objects.Find(KnownObject.Monster1)!;
        var position = recorded.Player.Position + new Vector3(200, 0, 0);
        var firstEnemy = recorded.AddMonster(kind, position, facing: new Facing(192));
        var secondEnemy = unrecorded.AddMonster(kind, position, facing: new Facing(192));
        for (int tick = 0; tick < 300; tick++)
        {
            recorded.Step(new PlayerCommand(null, PlayerCommand.GetCursor(firstEnemy.Position), firstEnemy.IsAlive, PlayerInventory.Pistols));
            unrecorded.Step(new PlayerCommand(null, PlayerCommand.GetCursor(secondEnemy.Position), secondEnemy.IsAlive, PlayerInventory.Pistols));
            recorder.Capture(recorded);
            Assert.Equal(recorded.Weapons.GetPlayerState(), unrecorded.Weapons.GetPlayerState());
            Assert.Equal(firstEnemy.GetState(), secondEnemy.GetState());
            Assert.Equal(recorded.Player.Hp, unrecorded.Player.Hp);
        }

        var replay = recorder.CreateReplay();
        Assert.NotEmpty(replay.Shots);
        Assert.NotEmpty(replay.Frames);
        Assert.Equal(recorded.Inventory.GetRemainingAmmo(), unrecorded.Inventory.GetRemainingAmmo());
    }

    /// <summary>
    /// Verifies a tank alien turned away from the player walks to it at 60 frames a second and above.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(8)]
    public void Step_HighFrameRates_LetTheTankAlienReachThePlayer(int frameTime)
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects, frameTime: frameTime);
        var player = world.Player.Position.AsVector2();
        var start = player + new Vector2(350, 0);
        var tank = world.AddMonster(objects.Find(KnownObject.TankMonster)!, new Vector3(start, 0), Facing.FromOffset(player - start) + 64);

        while (world.Time < 6000)
        {
            world.Step(default);
        }

        Assert.InRange(Vector2.Distance(tank.Position.AsVector2(), player), 0, 100);
    }

    /// <summary>
    /// Verifies the frame times of a display add up to one second in the 1 ms steps of the game's timer.
    /// </summary>
    [Fact]
    public void GetDisplayFrameTimes_FillOneSecond()
    {
        Assert.Equal([16, 17, 17], GameClock.GetDisplayFrameTimes(60).Take(3));
        Assert.Equal(1000, GameClock.GetDisplayFrameTimes(60).Sum());
        Assert.Equal(1000, GameClock.GetDisplayFrameTimes(144).Sum());
        Assert.All(GameClock.GetDisplayFrameTimes(144), time => Assert.InRange(time, 6, 7));
    }
}
