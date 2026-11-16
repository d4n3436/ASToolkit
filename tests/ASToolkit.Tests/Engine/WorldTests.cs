using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class WorldTests
{
    private const int DroppedItemMaxLifetime = 22000;

    [Fact]
    public void Step_PlayerKilledWithALifeLeft_IsRebornWithTheSpaceKey()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.Shotgun] = 23 }, default)
        { Items = new Dictionary<KnownObject, int> { [KnownObject.ExtraLife] = 1 } };
        var world = EngineTestWorld.CreateWorld(objects, loadout: loadout);
        world.Step(default);
        var first = world.Player;
        var start = first.Position;
        world.Damage(first, first.Hp);
        for (int tick = 0; tick < 200 && world.GetFlagman(0) is not null; tick++)
        {
            world.Step(default);
        }

        Assert.Null(world.GetFlagman(0));
        for (int tick = 0; tick < 5 && world.Player == first; tick++)
        {
            world.Step(default);
        }

        Assert.NotSame(first, world.Player);
        Assert.Same(world.Player, world.GetFlagman(0));
        Assert.True(world.Player.IsAlive);
        Assert.Equal(start, world.Player.Position);
        Assert.Equal(23, world.Player.SlotAmmo[KnownObject.Shotgun - KnownObject.Shotgun + 2] + (world.Player.WeaponSlot == 2 ? world.Player.GetAmmo() : 0));

        world.Damage(world.Player, world.Player.Hp);
        Assert.True(world.Player.IsAlive);
    }

    [Fact]
    public void Damage_KillingAMonsterCarryingAnItem_DropsItThenItExpires()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        world.Step(default);
        var position = world.Player.Position + new Vector3(500, 0, 0);
        var enemy = world.AddMonster(objects.Find(KnownObject.Monster1)!, position);
        enemy.Items.Clear();
        world.Action(enemy, ActionCode.AddItem, (int)KnownObject.Money);
        world.Damage(enemy, enemy.Hp);
        MergedMapSprite? dropped = null;
        for (int tick = 0; tick < 100 && dropped is null; tick++)
        {
            world.Step(default);
            dropped = world.Sprites.FirstOrDefault(sprite => sprite.Id == KnownObject.Money);
        }

        Assert.NotNull(dropped);
        int droppedAt = world.Time;
        while (world.Time <= droppedAt + DroppedItemMaxLifetime)
        {
            world.Step(default);
        }

        Assert.True(world.IsRemoved(dropped));
    }

    [Fact]
    public void Step_PlayerCommand_MovesThePlayerWithoutRouteOrBot()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        world.Step(default);
        var destination = world.Player.Position.AsVector2() + new Vector2(300, 0);
        var command = new PlayerCommand(destination, PlayerCommand.GetCursor(world.Player.Position), false, Unit.Pistols);
        for (int tick = 0; tick < 250; tick++)
        {
            world.Step(command);
        }

        Assert.NotEqual(UnitCommand.Move, world.Player.Command);
        Assert.True(PlayerMovement.IsTouching(world.Player, destination));
        for (int tick = 0; tick < 50; tick++)
        {
            world.Step(default);
        }

        Assert.Equal(0, world.Player.Speed);
        Assert.Equal(GameClock.LevelStartTime + 301 * GameClock.DefaultFrameTime, world.Time);
    }

    [Fact]
    public void Step_MovesEnemiesWithoutAPlayerBot()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        var start = world.Player.Position.AsVector2() + new Vector2(200, 0);
        var enemy = world.AddMonster(objects.Find(KnownObject.Monster1)!, new Vector3(start, world.Movement.GetHeight(start)), direction: new Angle(192));
        int health = world.Player.Hp;
        for (int tick = 0; tick < 300; tick++)
        {
            world.Step(default);
        }

        Assert.NotEqual(start, enemy.Position.AsVector2());
        Assert.True(world.Player.Hp < health);
    }

    [Fact]
    public void Step_Recording_KeepsTheWorldsBehavior()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = new ReplayRecorder();
        var recorded = EngineTestWorld.CreateWorld(objects);
        recorder.Start(recorded);
        var unrecorded = EngineTestWorld.CreateWorld(objects);
        var definition = objects.Find(KnownObject.Monster1)!;
        var position = recorded.Player.Position + new Vector3(200, 0, 0);
        var firstEnemy = recorded.AddMonster(definition, position, direction: new Angle(192));
        var secondEnemy = unrecorded.AddMonster(definition, position, direction: new Angle(192));
        for (int tick = 0; tick < 300; tick++)
        {
            recorded.Step(new PlayerCommand(null, PlayerCommand.GetCursor(firstEnemy.Position), firstEnemy.IsAlive, Unit.Pistols));
            unrecorded.Step(new PlayerCommand(null, PlayerCommand.GetCursor(secondEnemy.Position), secondEnemy.IsAlive, Unit.Pistols));
            recorder.Capture(recorded);
            Assert.Equal((recorded.Player.Position, recorded.Player.Frame, recorded.Weapons.Torso.Frame), (unrecorded.Player.Position, unrecorded.Player.Frame, unrecorded.Weapons.Torso.Frame));
            Assert.Equal((firstEnemy.Position, firstEnemy.Frame, firstEnemy.Hp), (secondEnemy.Position, secondEnemy.Frame, secondEnemy.Hp));
            Assert.Equal(recorded.Player.Hp, unrecorded.Player.Hp);
        }

        var replay = recorder.CreateReplay([]);
        Assert.NotEmpty(replay.Tracks);
        Assert.NotEmpty(replay.Inputs);
        Assert.Equal(new PlayerState(recorded).GetRemainingAmmo(), new PlayerState(unrecorded).GetRemainingAmmo());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(8)]
    public void Step_HighFrameRates_LetTheTankAlienReachThePlayer(int frameTime)
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects, frameTime: frameTime);
        var player = world.Player.Position.AsVector2();
        var start = player + new Vector2(350, 0);
        var tank = world.AddMonster(objects.Find(KnownObject.TankMonster)!, new Vector3(start, 0), Angle.FromOffset(player - start) + 64);

        while (world.Time < 6000)
        {
            world.Step(default);
        }

        Assert.InRange(Vector2.Distance(tank.Position.AsVector2(), player), 0, 100);
    }

    [Fact]
    public void GetDisplayFrameTimes_FillOneSecond()
    {
        Assert.Equal([16, 17, 17], GameClock.GetDisplayFrameTimes(60).Take(3));
        Assert.Equal(1000, GameClock.GetDisplayFrameTimes(60).Sum());
        Assert.Equal(1000, GameClock.GetDisplayFrameTimes(144).Sum());
        Assert.All(GameClock.GetDisplayFrameTimes(144), time => Assert.InRange(time, 6, 7));
    }
}
