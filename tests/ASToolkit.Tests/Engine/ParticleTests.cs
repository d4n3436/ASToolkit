using ASToolkit.Core.Simulation;
using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
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
        var definitions = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(definitions);
        var observer = new MapObserver(map);
        var scenery = observer.BakedEffects;
        map.Effects.Spawn(flying, new Vector3(100, 200, 22), new Angle(64), map.Time);
        while (map.Time <= 3500)
        {
            map.Play(default);
            observer.Observe();
        }

        Assert.DoesNotContain(map.GetEffects(), particle => particle.Definition.Id == flying);
        var settled = Assert.Single(scenery, particle => particle.Id == resting);
        Assert.True(settled.StartedAt > 0);
        Assert.True(settled.Position.Z >= 0);
        if (flying == KnownObject.FlyingGibs)
        {
            Assert.Contains(scenery, particle => particle.Id == KnownObject.SmallBloodPool);
        }
    }
}
