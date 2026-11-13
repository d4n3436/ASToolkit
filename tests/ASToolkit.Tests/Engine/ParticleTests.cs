using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Simulation;
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
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var recorder = new ReplayRecorder();
        var world = EngineTestWorld.CreateWorld(kinds, recorder);
        var effects = world.Effects;
        var scenery = recorder.CreateReplay().Scenery;
        effects.Spawn(flying, new Vector3(100, 200, 22), new Facing(64), world.Time);
        while (world.Time <= 3500)
        {
            world.Step(default);
        }

        Assert.DoesNotContain(effects.GetStates(), particle => particle.Id == flying);
        var settled = Assert.Single(scenery, particle => particle.Id == resting);
        Assert.True(settled.StartedAt > 0);
        Assert.True(settled.Position.Z >= 0);
        if (flying == KnownObject.FlyingGibs)
        {
            Assert.Contains(scenery, particle => particle.Id == KnownObject.SmallBloodPool);
        }
    }
}
