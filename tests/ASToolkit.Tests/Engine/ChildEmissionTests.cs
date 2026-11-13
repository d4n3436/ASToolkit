using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ChildEmissionTests
{
    [Fact]
    public void GetChildren_PistolPulses_AlternateBetweenTheTwoHands()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var owner = kinds.Find(KnownObject.PistolsTorso)!;
        var child = kinds.Find(KnownObject.PistolMuzzleFlash)!;
        var origin = new Vector3(100, 200, 10);
        var first = Assert.Single(ChildEmission.GetChildren(owner, child, SpriteAnimation.Fight, origin, default, new GameRandom(1)));
        var second = Assert.Single(ChildEmission.GetChildren(owner, child, SpriteAnimation.Fight, origin, default, new GameRandom(1), secondPulse: true));

        Assert.Equal(105, first.Position.X);
        Assert.Equal(95, second.Position.X);
        Assert.Equal(first.Position.Y, second.Position.Y);
        Assert.Equal(first.Position.Z, second.Position.Z);
        Assert.Equal(KnownObject.PistolMuzzleFlash, first.Id);
    }

    [Fact]
    public void GetChildren_HitBlood_StartsAtTheAlien()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var owner = kinds.Find(KnownObject.Monster1)!;
        var child = kinds.Find(KnownObject.RedBloodSpray)!;
        var origin = new Vector3(100, 200, 10);
        var children = ChildEmission.GetChildren(owner, child, SpriteAnimation.Close, origin, default, new GameRandom(1)).ToArray();

        var blood = Assert.Single(children);
        Assert.Equal(KnownObject.RedBloodSpray, blood.Id);
        Assert.Equal(origin, blood.Position);
    }
}
