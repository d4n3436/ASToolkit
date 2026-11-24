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
        var definitions = TestGameFolder.CreateCatalog();
        var definition = definitions.Find(KnownObject.PlayerStart)!;
        var map = EngineTestMap.CreateMap(definitions, frameTime: 20);
        var player = new Man(0, definition, new Vector3(1000, 1000, 0), 0, definition.MaxHp, map) { Direction = new Angle(64) };
        map.Collector.InsertToHash(player);
        player.Move(new Sprite(-1, definitions.Empty, new Vector3(1400, 1000, 0), 0, 0, map));
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
        var definitions = TestGameFolder.CreateCatalog();
        var definition = definitions.Find(KnownObject.PlayerStart)!;
        var map = EngineTestMap.CreateMap(definitions, frameTime: 20);
        var player = new Man(0, definition, new Vector3(1000, 1000, 0), 0, definition.MaxHp, map);
        map.Collector.InsertToHash(player);
        player.Move(new Sprite(-1, definitions.Empty, new Vector3(1400, 1000, 0), 0, 0, map));
        map.AdvanceClock();
        player.MoveTact();

        Assert.Equal(1000f, player.Position.X, 0f);
        Assert.Equal(999.64f, player.Position.Y, 3);
        Assert.Equal(10, player.Direction.Value);
    }
}
