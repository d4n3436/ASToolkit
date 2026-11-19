using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MapTests
{
    private const int DroppedItemMaxLifetime = 22000;

    [Fact]
    public void Step_PlayerKilledWithALifeLeft_IsRebornWithTheSpaceKey()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.Shotgun] = 23 }, default)
        { Items = new Dictionary<KnownObject, int> { [KnownObject.ExtraLife] = 1 } };
        var map = EngineTestMap.CreateMap(objects, loadout: loadout);
        map.Step(default);
        var first = map.Player;
        var start = first.Position;
        map.Action(first, ActionCode.Damage, first.Hp);
        for (int tick = 0; tick < 200 && map.GetFlagman(0) is not null; tick++)
        {
            map.Step(default);
        }

        Assert.Null(map.GetFlagman(0));
        for (int tick = 0; tick < 5 && map.Player == first; tick++)
        {
            map.Step(default);
        }

        Assert.NotSame(first, map.Player);
        Assert.Same(map.Player, map.GetFlagman(0));
        Assert.True(map.Player.IsAlive);
        Assert.Equal(start, map.Player.Position);
        Assert.Equal(23, map.Player.SlotAmmo[KnownObject.Shotgun - KnownObject.Shotgun + 2] + (map.Player.WeaponSlot == 2 ? map.Player.GetAmmo() : 0));

        map.Action(map.Player, ActionCode.Damage, map.Player.Hp);
        Assert.True(map.Player.IsAlive);
    }

    [Fact]
    public void Damage_KillingAMonsterCarryingAnItem_DropsItThenItExpires()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(objects);
        map.Step(default);
        var position = map.Player.Position + new Vector3(500, 0, 0);
        var enemy = map.AddMonster(objects.Find(KnownObject.Monster1)!, position);
        enemy.Items.Clear();
        map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Money);
        map.Action(enemy, ActionCode.Damage, enemy.Hp);
        MergedMapSprite? dropped = null;
        for (int tick = 0; tick < 100 && dropped is null; tick++)
        {
            map.Step(default);
            dropped = map.Sprites.FirstOrDefault(sprite => sprite.Id == KnownObject.Money);
        }

        Assert.NotNull(dropped);
        int droppedAt = map.Time;
        while (map.Time <= droppedAt + DroppedItemMaxLifetime)
        {
            map.Step(default);
        }

        Assert.True(map.IsRemoved(dropped));
    }

    [Fact]
    public void Action_RepairOnAWoundedMonster_RestoresItsHp()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(objects);
        map.Step(default);
        var enemy = map.AddMonster(objects.Find(KnownObject.Monster1)!, map.Player.Position + new Vector3(500, 0, 0));
        int maxHp = enemy.Hp;
        map.Action(enemy, ActionCode.Damage, maxHp / 2);

        map.Action(enemy, ActionCode.Repair);

        Assert.Equal(maxHp, enemy.Hp);
    }

    [Fact]
    public void Action_GetItemType_PicksACarriedItemOrNoneWithoutItems()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(objects);
        map.Step(default);
        var enemy = map.AddMonster(objects.Find(KnownObject.Monster1)!, map.Player.Position + new Vector3(500, 0, 0));
        enemy.Items.Clear();

        Assert.Equal(-1, map.Action(enemy, ActionCode.GetItemType));

        map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Money);
        map.Action(enemy, ActionCode.AddItem, (int)KnownObject.Medkit);

        Assert.Contains(map.Action(enemy, ActionCode.GetItemType), new[] { (int)KnownObject.Money, (int)KnownObject.Medkit });
    }

    [Fact]
    public void Step_PlayerCommand_MovesThePlayerWithoutRouteOrBot()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(objects);
        map.Step(default);
        var destination = map.Player.Position.AsVector2() + new Vector2(300, 0);
        var command = new PlayerCommand(destination, PlayerCommand.GetCursor(map.Player.Position), false, Sprite.Pistols);
        for (int tick = 0; tick < 250; tick++)
        {
            map.Step(command);
        }

        Assert.NotEqual(Command.Move, map.Player.Command);
        Assert.True(PlayerMovement.IsTouching(map.Player, destination, map.Definitions.Empty.Half));
        for (int tick = 0; tick < 50; tick++)
        {
            map.Step(default);
        }

        Assert.Equal(0, map.Player.Speed);
        Assert.Equal(GameClock.LevelStartTime + 301 * GameClock.DefaultFrameTime, map.Time);
    }

    [Fact]
    public void Step_MovesEnemiesWithoutAPlayerBot()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(objects);
        var start = map.Player.Position.AsVector2() + new Vector2(200, 0);
        var enemy = map.AddMonster(objects.Find(KnownObject.Monster1)!, new Vector3(start, map.Movement.GetHeight(start)), direction: new Angle(192));
        int health = map.Player.Hp;
        for (int tick = 0; tick < 300; tick++)
        {
            map.Step(default);
        }

        Assert.NotEqual(start, enemy.Position.AsVector2());
        Assert.True(map.Player.Hp < health);
    }

    [Fact]
    public void Step_Recording_KeepsTheMapsBehavior()
    {
        var objects = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = new ReplayRecorder();
        var recorded = EngineTestMap.CreateMap(objects);
        recorder.Start(recorded);
        var unrecorded = EngineTestMap.CreateMap(objects);
        var definition = objects.Find(KnownObject.Monster1)!;
        var position = recorded.Player.Position + new Vector3(200, 0, 0);
        var firstEnemy = recorded.AddMonster(definition, position, direction: new Angle(192));
        var secondEnemy = unrecorded.AddMonster(definition, position, direction: new Angle(192));
        for (int tick = 0; tick < 300; tick++)
        {
            recorded.Step(new PlayerCommand(null, PlayerCommand.GetCursor(firstEnemy.Position), firstEnemy.IsAlive, Sprite.Pistols));
            unrecorded.Step(new PlayerCommand(null, PlayerCommand.GetCursor(secondEnemy.Position), secondEnemy.IsAlive, Sprite.Pistols));
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
        var map = EngineTestMap.CreateMap(objects, frameTime: frameTime);
        var player = map.Player.Position.AsVector2();
        var start = player + new Vector2(350, 0);
        var tank = map.AddMonster(objects.Find(KnownObject.TankMonster)!, new Vector3(start, 0), Angle.FromOffset(player - start) + 64);

        while (map.Time < 6000)
        {
            map.Step(default);
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
