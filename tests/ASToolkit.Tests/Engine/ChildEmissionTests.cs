using ASToolkit.Engine;
using System.Numerics;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ChildEmissionTests
{
    [Fact]
    public void CreateChild_PistolPulses_AlternateBetweenTheTwoHands()
    {
        var map = EngineTestMap.CreateMap();
        var owner = map.FindVid(KnownObject.PistolsTorso)!;
        var origin = new Vector3(100, 200, 10);
        var torso = new Sprite(owner, origin, default, null, map);
        torso.SetAnimationValue(SpriteAnimation.Fight);
        torso.Goal = map.ConstructGoal(new Vector3(300, 200, 10));
        var before = map.GetEffects().ToHashSet();

        torso.CreateChild();
        var first = Assert.Single(map.GetEffects().Where(effect => !before.Contains(effect)), effect => effect.Vid.Id == KnownObject.PistolMuzzleFlash);
        before = map.GetEffects().ToHashSet();
        torso.CreateChild();
        var second = Assert.Single(map.GetEffects().Where(effect => !before.Contains(effect)), effect => effect.Vid.Id == KnownObject.PistolMuzzleFlash);

        Assert.Equal(KnownObject.PistolMuzzleFlash, first.Vid.Id);
        Assert.Equal(105f, first.Position.X, 0f);
        Assert.Equal(95f, second.Position.X, 0f);
        Assert.Equal(first.Position.Y, second.Position.Y, 0f);
    }

    [Fact]
    public void CreateChild_HitBlood_StartsAtTheAlien()
    {
        var map = EngineTestMap.CreateMap();
        var owner = map.FindVid(KnownObject.Monster1)!;
        var origin = new Vector3(100, 200, 10);
        var alien = new Sprite(owner, origin, default, null, map);
        var before = map.GetEffects().ToHashSet();

        alien.CreateChildFor(SpriteAnimation.Close);

        var blood = Assert.Single(map.GetEffects().Where(effect => !before.Contains(effect)), effect => effect.Vid.Id == KnownObject.RedBloodSpray);
        Assert.Equal(alien.Position, blood.Position);
    }
}
