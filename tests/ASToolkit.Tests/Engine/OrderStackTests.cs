using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class OrderStackTests
{
    [Fact]
    public void Step_EachTrigger_KeepsTheFollowingWaveWaiting()
    {
        var (world, spawner) = CreateSpawner(
            new((int)ActionCode.StopStack, 0, 0, 0),
            new((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 3000, 1000),
            new((int)ActionCode.FlagmanTrigger, 2000, 1000, 50),
            new((int)ActionCode.BuildUnit, (int)KnownObject.YellowMonster1, 1400, 1000),
            new((int)ActionCode.FlagmanTrigger, 1000, 1000, 50));
        Step(world, 60);

        Assert.Equal([KnownObject.YellowMonster1], world.Monsters.Select(unit => unit.Definition.Id));
        world.Player.Position = new Vector3(2000, 1000, 0);
        Step(world, 60);

        Assert.Equal([KnownObject.YellowMonster1, KnownObject.Monster1], world.Monsters.Select(unit => unit.Definition.Id));
        Assert.Equal(UnitOrder.StopMarker, spawner.Orders.Top.Act);
    }

    [Fact]
    public void Step_PauseAndStackJump_RepeatTheWave()
    {
        var (world, _) = CreateSpawner(
            new((int)ActionCode.StopStack, 0, 0, 0),
            new((int)ActionCode.GotoStack, 3, 0, 0),
            new((int)ActionCode.Pause, 400, 0, 0),
            new((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 0, 0));
        var built = new List<int>();
        for (int tick = 0; tick < 120; tick++)
        {
            world.Step(default);

            if (world.Monsters.Count <= built.Count)
                continue;

            built.Add(world.Time);
            var unit = world.Monsters[^1];
            unit.Position += new Vector3(500, 500, 0);
            world.Units.ChangeCoor(unit);
        }

        Assert.Equal([170, 810, 1450], built);
    }

    [Fact]
    public void Step_ZeroRadiusTrigger_NeverReleasesItsWave()
    {
        var (world, spawner) = CreateSpawner(
            new((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 0, 0),
            new((int)ActionCode.FlagmanTrigger, 0, 0, 0));
        Step(world, 120);

        Assert.Empty(world.Monsters);
        Assert.Equal(2, spawner.Orders.Count);
        Assert.Equal(ActionCode.FlagmanTrigger, spawner.Orders.Top.Code);
    }

    private static (GameWorld World, Unit Spawner) CreateSpawner(params StackAction[] stack)
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var placement = new MergedMapSprite(1, KnownObject.Spawner, new Vector3(1000, 1600, 0), 0, Army.Monsters, []);
        var world = EngineTestWorld.CreateWorld(definitions, sprites: [placement]);
        var spawner = world.FindPlacementUnit(placement)!;
        spawner.Orders.Load(stack.Select(UnitOrder.FromAction).ToArray());

        return (world, spawner);
    }

    private static void Step(GameWorld world, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Step(default);
        }
    }
}
