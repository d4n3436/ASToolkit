using System.Drawing;
using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class PlayerMovementTests
{
    [Fact]
    public void Tick_NoTarget_BrakesWithoutDiscardingMomentum()
    {
        var kind = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects).Find(KnownObject.PlayerStart)!;
        var grid = new UnitGrid(new SizeF(2000, 2000), new Vector2(130));
        var player = new Unit(0, kind, new Vector3(1000, 1000, 0), 0, kind.MaxHp) { Direction = new Angle(64) };
        grid.Add(player);
        var movement = new PlayerMovement(new UnitCollision(new MovementMap(), grid));
        movement.Tick(player, new Vector2(1400, 1000), 0.18f, 20);
        Assert.Equal(0.018f, player.Speed, 5);
        Assert.InRange(Vector2.Distance(player.Position.AsVector2(), new Vector2(1000.36f, 1000)), 0, 0.0001f);
        movement.Tick(player, null, 0.18f, 20);
        Assert.Equal(0.009f, player.Speed, 5);
        Assert.InRange(Vector2.Distance(player.Position.AsVector2(), new Vector2(1000.54f, 1000)), 0, 0.0001f);
        var stopped = player.Position.AsVector2();
        movement.Tick(player, null, 0.18f, 20);
        Assert.Equal(0, player.Speed);
        Assert.Equal(stopped, player.Position.AsVector2());
    }

    [Fact]
    public void Tick_DisplacesAlongTheDirectionFromBeforeTheTurn()
    {
        var kind = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects).Find(KnownObject.PlayerStart)!;
        var grid = new UnitGrid(new SizeF(2000, 2000), new Vector2(130));
        var player = new Unit(0, kind, new Vector3(1000, 1000, 0), 0, kind.MaxHp);
        grid.Add(player);
        var movement = new PlayerMovement(new UnitCollision(new MovementMap(), grid));
        movement.Tick(player, new Vector2(1400, 1000), 0.18f, 20);

        Assert.Equal(1000, player.Position.X);
        Assert.Equal(999.64f, player.Position.Y, 3);
        Assert.Equal(10, player.Direction.Value);
    }
}
