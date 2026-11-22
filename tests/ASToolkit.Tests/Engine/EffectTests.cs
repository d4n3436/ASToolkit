using ASToolkit.Bot;
using ASToolkit.Engine;
using System.Numerics;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class EffectTests
{
    [Theory]
    [InlineData("0", 2)]
    [InlineData("1", 1)]
    public void Emit_Hit_CreatesBloodAndLeavesItsStains(string blood, int stains)
    {
        string game = TestGameFolder.RequireFolder();
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions, blood: blood);
        var observer = new MapObserver(map);
        var scenery = observer.BakedEffects;
        map.Play(default);
        observer.Observe();
        int start = map.Time;
        Emit(map, definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Close, new Vector3(100, 100, 0), new Angle(64));

        Assert.Contains(map.GetEffects(), effect => effect.Definition.Id == KnownObject.RedBloodSpray && effect.CreatedAt == start);
        Assert.Contains(map.GetEffects(), effect => effect is { Definition.Id: KnownObject.SmallBloodPool, Animation: SpriteAnimation.Death });
        Assert.Empty(scenery);
        while (map.Time <= 2000)
        {
            map.Play(default);
            observer.Observe();
        }
        Assert.Empty(map.GetEffects());
        Assert.Equal(stains, scenery.Count);
        Assert.All(scenery, stain => Assert.Equal(KnownObject.SmallBloodPool, stain.Id));
    }

    [Fact]
    public void Emit_Death_LeavesBloodBeforeTheCorpseArrives()
    {
        string game = TestGameFolder.RequireFolder();
        var definitions = new SpriteCatalog(game, TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions);
        var observer = new MapObserver(map);
        var scenery = observer.BakedEffects;
        map.Play(default);
        observer.Observe();
        Emit(map, definitions.Find(KnownObject.Monster1)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default);

        Assert.Contains(map.GetEffects(), effect => effect.Definition.Id == KnownObject.Monster1Death);
        Assert.Contains(map.GetEffects(), effect => effect.Definition.Id == KnownObject.LargeBloodPool);
        while (map.Time <= 2000)
        {
            map.Play(default);
            observer.Observe();
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

        int start = map.Time;
        Emit(map, definitions.Find(KnownObject.WeaponMonster)!, SpriteAnimation.Death, new Vector3(100, 100, 0), default);

        Assert.Empty(map.Monsters);
        int duration = definitions.Find(KnownObject.WeaponMonsterDeath)!.GetAnimationDuration(SpriteAnimation.Death);
        while (map.Time <= start + duration + 2 * GameClock.DefaultFrameTime)
        {
            map.Play(default);
        }
        Assert.Equal(KnownObject.Monster2, Assert.Single(map.Monsters).Definition.Id);
    }

    private static void Emit(Map map, SpriteDefinition owner, SpriteAnimation state, Vector3 position, Angle direction)
    {
        var sprite = new Sprite(-1, owner, position, 0, owner.MaxHp, direction);
        map.CreateChildFor(sprite, state);
    }
}
