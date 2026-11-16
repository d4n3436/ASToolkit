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
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(definitions);
        var effects = world.Effects;
        var scenery = world.Effects.Settled;
        effects.Emit(definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Close, new Vector3(100, 100, 0), new Angle(64), world.Time);

        Assert.Contains(effects.Active, effect => effect is { Definition.Id: KnownObject.RedBloodSpray, CreatedAt: GameClock.LevelStartTime });
        var stain = Assert.Single(scenery);
        Assert.Equal(KnownObject.SmallBloodPool, stain.Id);
        while (world.Time <= 2000)
        {
            world.Step(default);
        }
        Assert.Empty(effects.Active);
        Assert.Equal(stain, Assert.Single(scenery));
    }

    [Fact]
    public void Emit_Death_LeavesBloodBeforeTheCorpseArrives()
    {
        string game = TestGameFolder.RequireFolder();
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(definitions);
        var effects = world.Effects;
        var scenery = world.Effects.Settled;
        effects.Emit(definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default, 0);

        Assert.Contains(effects.Active, effect => effect.Definition.Id == KnownObject.Monster1Death);
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
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(definitions);
        var effects = world.Effects;

        effects.Emit(definitions.Find(KnownObject.WeaponMonster)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default, 0);

        Assert.Empty(world.Monsters);
        int duration = definitions.Find(KnownObject.WeaponMonsterDeath)!.GetAnimationDuration(SpriteAnimation.Death);
        while (world.Time <= duration + GameClock.DefaultFrameTime)
        {
            world.Step(default);
        }
        Assert.Equal(KnownObject.Monster2, Assert.Single(world.Monsters).Definition.Id);
    }
}
