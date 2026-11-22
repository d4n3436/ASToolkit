using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ChildEmissionTests
{
    [Fact]
    public void CreateChild_PistolPulses_AlternateBetweenTheTwoHands()
    {
        var definitions = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(definitions);
        var owner = definitions.Find(KnownObject.PistolsTorso)!;
        var origin = new Vector3(100, 200, 10);
        var torso = new Sprite(-1, owner, origin, 0, owner.MaxHp);
        torso.SetAnimationValue(SpriteAnimation.Fight);
        torso.Goal = map.ConstructGoal(new Vector3(300, 200, 10));
        var before = map.GetEffects().ToHashSet();

        map.CreateChild(torso);
        var first = Assert.Single(map.GetEffects().Where(effect => !before.Contains(effect)), effect => effect.Definition.Id == KnownObject.PistolMuzzleFlash);
        before = map.GetEffects().ToHashSet();
        map.CreateChild(torso);
        var second = Assert.Single(map.GetEffects().Where(effect => !before.Contains(effect)), effect => effect.Definition.Id == KnownObject.PistolMuzzleFlash);

        Assert.Equal(KnownObject.PistolMuzzleFlash, first.Definition.Id);
        Assert.Equal(105f, first.Position.X, 0f);
        Assert.Equal(95f, second.Position.X, 0f);
        Assert.Equal(first.Position.Y, second.Position.Y, 0f);
    }

    [Fact]
    public void CreateChild_HitBlood_StartsAtTheAlien()
    {
        var definitions = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(definitions);
        var owner = definitions.Find(KnownObject.Monster1)!;
        var origin = new Vector3(100, 200, 10);
        var alien = new Sprite(-1, owner, origin, 0, owner.MaxHp);
        var before = map.GetEffects().ToHashSet();

        map.CreateChildFor(alien, SpriteAnimation.Close);

        var blood = Assert.Single(map.GetEffects().Where(effect => !before.Contains(effect)), effect => effect.Definition.Id == KnownObject.RedBloodSpray);
        Assert.Equal(origin, blood.Position);
    }
}
