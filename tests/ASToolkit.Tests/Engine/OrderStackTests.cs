using ASToolkit.Bot;
using ASToolkit.Engine;
using System.Numerics;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class OrderStackTests
{
    [Fact]
    public void Step_EachTrigger_KeepsTheFollowingWaveWaiting()
    {
        var (map, spawner) = CreateSpawner(
            new StackAction((int)ActionCode.StopStack, 0, 0, 0),
            new StackAction((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 3000, 1000),
            new StackAction((int)ActionCode.FlagmanTrigger, 2000, 1000, 50),
            new StackAction((int)ActionCode.BuildUnit, (int)KnownObject.YellowMonster1, 1400, 1000),
            new StackAction((int)ActionCode.FlagmanTrigger, 1000, 1000, 50));
        Step(map, 60);

        Assert.Equal([KnownObject.YellowMonster1], map.Monsters.Select(sprite => sprite.Definition.Id));
        map.Player.Position = new Vector3(2000, 1000, 0);
        Step(map, 60);

        Assert.Equal([KnownObject.YellowMonster1, KnownObject.Monster1], map.Monsters.Select(sprite => sprite.Definition.Id));
        Assert.Equal(Act.StopMarker, spawner.ActionStack.Top.Value);
    }

    [Fact]
    public void Step_PauseAndStackJump_RepeatTheWave()
    {
        var (map, _) = CreateSpawner(
            new StackAction((int)ActionCode.StopStack, 0, 0, 0),
            new StackAction((int)ActionCode.GotoStack, 3, 0, 0),
            new StackAction((int)ActionCode.Pause, 400, 0, 0),
            new StackAction((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 0, 0));
        var built = new List<int>();
        for (int tick = 0; tick < 120; tick++)
        {
            map.Play(default);

            if (map.Monsters.Count() <= built.Count)
                continue;

            built.Add(map.Time);
            var sprite = map.Monsters.Last();
            sprite.Position += new Vector3(500, 500, 0);
            map.Collector.ChangeCoor(sprite);
        }

        Assert.Equal([170, 810, 1450], built);
    }

    [Fact]
    public void Step_ZeroRadiusTrigger_NeverReleasesItsWave()
    {
        var (map, spawner) = CreateSpawner(
            new StackAction((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 0, 0),
            new StackAction((int)ActionCode.FlagmanTrigger, 0, 0, 0));
        Step(map, 120);

        Assert.Empty(map.Monsters);
        Assert.Equal(2, spawner.ActionStack.Count);
        Assert.Equal(ActionCode.FlagmanTrigger, spawner.ActionStack.Top.Code);
    }

    private static (Map Map, Sprite Spawner) CreateSpawner(params StackAction[] stack)
    {
        var definitions = TestGameFolder.CreateCatalog();
        var placement = new MergedMapSprite(1, KnownObject.Spawner, new Vector3(1000, 1600, 0), 0, Army.Monsters, []);
        var map = EngineTestMap.CreateMap(definitions, sprites: [placement]);
        var spawner = map.FindPlacementSprite(placement)!;
        spawner.ActionStack.Load(stack.Select(Act.FromAction).ToArray());

        return (map, spawner);
    }

    private static void Step(Map map, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            map.Play(default);
        }
    }
}
