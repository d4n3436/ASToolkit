using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class EffectTests
{
    [Fact]
    public void Emit_Hit_CreatesBloodAndLeavesItsStain()
    {
        string game = TestGameFolder.RequireFolder();
        var kinds = new ObjectCatalog(game, TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var effects = world.Effects;
        var scenery = world.Effects.Settled;
        effects.Emit(kinds.Find(KnownObject.Monster1)!, SpriteAnimation.Close, new Vector3(100, 100, 0), new Facing(64), world.Time);

        Assert.Contains(effects.GetStates(), effect => effect is { Id: KnownObject.RedBloodSpray, StartedAt: 0 });
        var stain = Assert.Single(scenery);
        Assert.Equal(KnownObject.SmallBloodPool, stain.Id);
        while (world.Time <= 2000)
        {
            world.Step(default);
        }
        Assert.Empty(effects.GetStates());
        Assert.Equal(stain, Assert.Single(scenery));
    }

    [Fact]
    public void Emit_Death_LeavesBloodBeforeTheCorpseArrives()
    {
        string game = TestGameFolder.RequireFolder();
        var kinds = new ObjectCatalog(game, TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var effects = world.Effects;
        var scenery = world.Effects.Settled;
        effects.Emit(kinds.Find(KnownObject.Monster1)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default, 0);

        Assert.Contains(effects.GetStates(), effect => effect.Id == KnownObject.Monster1Death);
        Assert.Contains(scenery, effect => effect.Id == KnownObject.LargeBloodPool);
        Assert.Equal(8, scenery.Count(effect => effect.Id == KnownObject.SmallBloodPool));
        Assert.DoesNotContain(scenery, effect => effect.Id == KnownObject.Monster1Corpse);
        while (world.Time <= 2000)
        {
            world.Step(default);
        }
        
        Assert.Contains(scenery, effect => effect is { Id: KnownObject.Monster1Corpse, StartedAt: > 0 });
    }

    [Fact]
    public void Emit_ArmoredMonsterDeath_BringsTheBodyAfterTheEffect()
    {
        string game = TestGameFolder.RequireFolder();
        var kinds = new ObjectCatalog(game, TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var effects = world.Effects;

        effects.Emit(kinds.Find(KnownObject.WeaponMonster)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default, 0);

        Assert.Empty(world.Monsters);
        int duration = kinds.Find(KnownObject.WeaponMonsterDeath)!.GetAnimationDuration(SpriteAnimation.Death);
        while (world.Time <= duration + GameClock.DefaultFrameTime)
        {
            world.Step(default);
        }
        Assert.Equal(KnownObject.Monster2, Assert.Single(world.Monsters).Kind.Id);
    }
}
