using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class EffectTests
{
    [Theory]
    [InlineData(null, 2)]
    [InlineData("1", 1)]
    public void Emit_Hit_CreatesBloodAndLeavesItsStains(string? blood, int stains)
    {
        string game = TestGameFolder.RequireFolder();
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions, blood: blood);
        var effects = map.Effects;
        var scenery = map.Effects.Settled;
        map.Step(default);
        int start = map.Time;
        effects.Emit(definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Close, new Vector3(100, 100, 0), new Angle(64), start);

        Assert.Contains(effects.Active, effect => effect.Definition.Id == KnownObject.RedBloodSpray && effect.CreatedAt == start);
        Assert.Contains(effects.Active, effect => effect is { Definition.Id: KnownObject.SmallBloodPool, Animation: SpriteAnimation.Death });
        Assert.Empty(scenery);
        while (map.Time <= 2000)
        {
            map.Step(default);
        }
        Assert.Empty(effects.Active);
        Assert.Equal(stains, scenery.Count);
        Assert.All(scenery, stain => Assert.Equal(KnownObject.SmallBloodPool, stain.Id));
    }

    [Fact]
    public void Emit_Death_LeavesBloodBeforeTheCorpseArrives()
    {
        string game = TestGameFolder.RequireFolder();
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions);
        var effects = map.Effects;
        var scenery = map.Effects.Settled;
        map.Step(default);
        effects.Emit(definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default, map.Time);

        Assert.Contains(effects.Active, effect => effect.Definition.Id == KnownObject.Monster1Death);
        Assert.Contains(effects.Active, effect => effect.Definition.Id == KnownObject.LargeBloodPool);
        while (map.Time <= 2000)
        {
            map.Step(default);
        }

        var ids = scenery.Select(effect => effect.Id).ToList();
        Assert.Equal(2, ids.Count(id => id == KnownObject.LargeBloodPool));
        Assert.Equal(16, ids.Count(id => id == KnownObject.SmallBloodPool));
        Assert.Contains(scenery, effect => effect is { Id: KnownObject.Monster1Corpse, StartedAt: > 0 });
        Assert.True(ids.IndexOf(KnownObject.LargeBloodPool) < ids.IndexOf(KnownObject.Monster1Corpse));
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
