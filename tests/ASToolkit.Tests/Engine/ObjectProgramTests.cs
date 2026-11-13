using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ObjectProgramTests
{
    [Fact]
    public void Tick_EachTrigger_KeepsTheFollowingWaveWaiting()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var actions = new StackAction[]
        {
            new((int)ActionCode.StopStack, 0, 0, 0),
            new((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 300, 100),
            new((int)ActionCode.FlagmanTrigger, 2000, 1000, 50),
            new((int)ActionCode.BuildUnit, (int)KnownObject.YellowMonster1, 100, 100),
            new((int)ActionCode.FlagmanTrigger, 1000, 1000, 50)
        };
        var program = new ObjectProgram(kinds.Find(KnownObject.Spawner)!, Vector3.Zero, default, actions, new GameRandom(1), world);
        for (int time = 20; time <= 1000; time += 20)
        {
            program.Tick(new Vector2(1000), time);
        }

        Assert.Equal([KnownObject.YellowMonster1], world.Monsters.Select(unit => unit.Kind.Id));
        for (int time = 1020; time <= 2000; time += 20)
        {
            program.Tick(new Vector2(2000, 1000), time);
        }

        Assert.Equal([KnownObject.YellowMonster1, KnownObject.Monster1], world.Monsters.Select(unit => unit.Kind.Id));
        Assert.True(program.Finished);
    }

    [Fact]
    public void Tick_PausesAndStackJumps_RepeatTheWaveAtAnimationTicks()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var built = new List<int>();
        int time;
        var actions = new StackAction[]
        {
            new((int)ActionCode.StopStack, 0, 0, 0),
            new((int)ActionCode.GotoStack, 3, 0, 0),
            new((int)ActionCode.Pause, 200, 0, 0),
            new((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 0, 0)
        };
        var program = new ObjectProgram(kinds.Find(KnownObject.Spawner)!, Vector3.Zero, default, actions, new GameRandom(1), world);
        for (time = 20; time <= 600; time += 20)
        {
            int count = world.Monsters.Count;
            program.Tick(Vector2.Zero, time);

            if (world.Monsters.Count <= count)
                continue;
            
            built.Add(time);
            var unit = world.Monsters[^1];
            unit.Position += new Vector3(500, 500, 0);
            world.Units.Move(unit);
        }

        Assert.Equal([160, 580], built);
    }

    [Fact]
    public void Tick_ZeroRadiusTrigger_NeverReleasesItsWave()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var actions = new StackAction[]
        {
            new((int)ActionCode.BuildUnit, (int)KnownObject.Monster1, 0, 0),
            new((int)ActionCode.FlagmanTrigger, 0, 0, 0)
        };
        var program = new ObjectProgram(kinds.Find(KnownObject.Spawner)!, Vector3.Zero, default, actions, new GameRandom(1), world);
        for (int time = 20; time <= 2000; time += 20)
        {
            program.Tick(Vector2.Zero, time);
        }

        Assert.Empty(world.Monsters);
        Assert.Equal(2, program.Cursor);
        Assert.False(program.Finished);
    }
}
