using System.Numerics;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ParticleTests
{
    [Theory]
    [InlineData(KnownObject.FlyingCasing, KnownObject.LyingCasing)]
    [InlineData(KnownObject.FlyingShotgunCasing, KnownObject.LyingShotgunCasing)]
    [InlineData(KnownObject.FlyingGibs, KnownObject.LyingGibs)]
    [InlineData(KnownObject.FlyingGreenGibs, KnownObject.LyingGreenGibs)]
    public void Spawn_FlyingParticles_SettleIntoTheirRestingObjects(KnownObject flying, KnownObject resting)
    {
        var map = EngineTestMap.CreateMap();
        var observer = new MapObserver(map);
        var scenery = observer.BakedEffects;
        map.CreateSprite(map.FindVid(flying), new Vector3(100, 200, 22), new Angle(64));
        while (map.CurrentTime <= 4000)
        {
            map.Play(default);
            observer.Observe();
        }

        Assert.DoesNotContain(map.GetEffects(), particle => particle.Vid.Id == flying);
        var settled = Assert.Single(scenery, particle => particle.Id == resting);
        Assert.True(settled.StartedAt > 0);
        Assert.True(settled.Position.Z >= 0);
        if (flying == KnownObject.FlyingGibs)
        {
            Assert.Contains(scenery, particle => particle.Id == KnownObject.SmallBloodPool);
        }
    }
}
