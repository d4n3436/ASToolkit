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
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var owner = definitions.Find(KnownObject.PistolsTorso)!;
        var child = definitions.Find(KnownObject.PistolMuzzleFlash)!;
        var origin = new Vector3(100, 200, 10);
        var first = Assert.Single(Collect(ChildEmission.GetChildren(owner, child, SpriteAnimation.Fight, origin, default, new GameRandom(1))));
        var second = Assert.Single(Collect(ChildEmission.GetChildren(owner, child, SpriteAnimation.Fight, origin, default, new GameRandom(1), secondPulse: true)));

        Assert.Equal(105, first.Position.X);
        Assert.Equal(95, second.Position.X);
        Assert.Equal(first.Position.Y, second.Position.Y);
        Assert.Equal(first.Position.Z, second.Position.Z);
        Assert.Equal(KnownObject.PistolMuzzleFlash, first.Id);
    }

    [Fact]
    public void GetChildren_HitBlood_StartsAtTheAlien()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var owner = definitions.Find(KnownObject.Monster1)!;
        var child = definitions.Find(KnownObject.RedBloodSpray)!;
        var origin = new Vector3(100, 200, 10);
        var children = Collect(ChildEmission.GetChildren(owner, child, SpriteAnimation.Close, origin, default, new GameRandom(1)));

        var blood = Assert.Single(children);
        Assert.Equal(KnownObject.RedBloodSpray, blood.Id);
        Assert.Equal(origin, blood.Position);
    }

    private static List<ChildEmission.Spawn> Collect(ChildEmission.ChildBatch batch)
    {
        var spawns = new List<ChildEmission.Spawn>();
        foreach (var spawn in batch)
        {
            spawns.Add(spawn);
        }

        return spawns;
    }
}
