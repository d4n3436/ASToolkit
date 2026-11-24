using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class PlayerMovementTests
{
    [Fact]
    public void MoveTact_NoTarget_BrakesWithoutDiscardingMomentum()
    {
        var map = EngineTestMap.CreateMap(frameTime: 20);
        var vid = map.FindVid(KnownObject.PlayerStart)!;
        var player = new Man(vid, new Vector3(1000, 1000, 0), default, null, map) { Direction = new Angle(64) };
        player.Move(new Sprite(map.EmptyVid, new Vector3(1400, 1000, 0), default, null, map));
        map.AdvanceClock();
        player.MoveTact();
        Assert.Equal(0.018f, player.Speed, 5);
        Assert.InRange(Vector2.Distance(player.Position.AsVector2(), new Vector2(1000.36f, 1000)), 0, 0.0001f);
        player.Stop();
        map.AdvanceClock();
        player.MoveTact();
        Assert.Equal(0.009f, player.Speed, 5);
        Assert.InRange(Vector2.Distance(player.Position.AsVector2(), new Vector2(1000.54f, 1000)), 0, 0.0001f);
        var stopped = player.Position.AsVector2();
        map.AdvanceClock();
        player.MoveTact();
        Assert.Equal(0, player.Speed);
        Assert.Equal(stopped, player.Position.AsVector2());
    }

    [Fact]
    public void MoveTact_DisplacesAlongTheDirectionFromBeforeTheTurn()
    {
        var map = EngineTestMap.CreateMap(frameTime: 20);
        var vid = map.FindVid(KnownObject.PlayerStart)!;
        var player = new Man(vid, new Vector3(1000, 1000, 0), default, null, map);
        player.Move(new Sprite(map.EmptyVid, new Vector3(1400, 1000, 0), default, null, map));
        map.AdvanceClock();
        player.MoveTact();

        Assert.Equal(1000f, player.Position.X, 0f);
        Assert.Equal(999.64f, player.Position.Y, 3);
        Assert.Equal(10, player.Direction.Value);
    }
}
