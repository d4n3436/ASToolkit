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
        var map = EngineTestMap.CreateMap(definitions);
        var effects = map.Effects;
        var scenery = map.Effects.Settled;
        effects.Emit(definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Close, new Vector3(100, 100, 0), new Angle(64), map.Time);

        Assert.Contains(effects.Active, effect => effect is { Definition.Id: KnownObject.RedBloodSpray, CreatedAt: GameClock.LevelStartTime });
        var stain = Assert.Single(scenery);
        Assert.Equal(KnownObject.SmallBloodPool, stain.Id);
        while (map.Time <= 2000)
        {
            map.Step(default);
        }
        Assert.Empty(effects.Active);
        Assert.Equal(stain, Assert.Single(scenery));
    }

    [Fact]
    public void Emit_Death_LeavesBloodBeforeTheCorpseArrives()
    {
        string game = TestGameFolder.RequireFolder();
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions);
        var effects = map.Effects;
        var scenery = map.Effects.Settled;
        effects.Emit(definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default, 0);

        Assert.Contains(effects.Active, effect => effect.Definition.Id == KnownObject.Monster1Death);
        Assert.Contains(scenery, effect => effect.Id == KnownObject.LargeBloodPool);
        Assert.Equal(8, scenery.Count(effect => effect.Id == KnownObject.SmallBloodPool));
        Assert.DoesNotContain(scenery, effect => effect.Id == KnownObject.Monster1Corpse);
        while (map.Time <= 2000)
        {
            map.Step(default);
        }
        
        Assert.Contains(scenery, effect => effect is { Id: KnownObject.Monster1Corpse, StartedAt: > 0 });
    }

    [Fact]
    public void Emit_ArmoredMonsterDeath_BringsTheBodyAfterTheEffect()
    {
        string game = TestGameFolder.RequireFolder();
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions);
        var effects = map.Effects;

        effects.Emit(definitions.Find(KnownObject.WeaponMonster)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default, 0);

        Assert.Empty(map.Monsters);
        int duration = definitions.Find(KnownObject.WeaponMonsterDeath)!.GetAnimationDuration(SpriteAnimation.Death);
        while (map.Time <= duration + GameClock.DefaultFrameTime)
        {
            map.Step(default);
        }
        Assert.Equal(KnownObject.Monster2, Assert.Single(map.Monsters).Definition.Id);
    }
}
