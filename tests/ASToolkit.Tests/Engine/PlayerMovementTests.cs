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
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var definition = definitions.Find(KnownObject.PlayerStart)!;
        var collector = new SpriteCollector(new SizeF(2000, 2000), new Vector2(130));
        var player = new Sprite(0, definition, new Vector3(1000, 1000, 0), 0, definition.MaxHp) { Direction = new Angle(64) };
        collector.InsertToHash(player);
        var movement = new PlayerMovement(new SpriteCollision(new MovementMap(), collector));
        new SpriteKinematics(definitions, null!).SetMoveTarget(player, new Sprite(-1, definitions.Empty, new Vector3(1400, 1000, 0), 0, 0));
        movement.Tick(player, 0.18f, 20);
        Assert.Equal(0.018f, player.Speed, 5);
        Assert.InRange(Vector2.Distance(player.Position.AsVector2(), new Vector2(1000.36f, 1000)), 0, 0.0001f);
        SpriteKinematics.Stop(player);
        movement.Tick(player, 0.18f, 20);
        Assert.Equal(0.009f, player.Speed, 5);
        Assert.InRange(Vector2.Distance(player.Position.AsVector2(), new Vector2(1000.54f, 1000)), 0, 0.0001f);
        var stopped = player.Position.AsVector2();
        movement.Tick(player, 0.18f, 20);
        Assert.Equal(0, player.Speed);
        Assert.Equal(stopped, player.Position.AsVector2());
    }

    [Fact]
    public void Tick_DisplacesAlongTheDirectionFromBeforeTheTurn()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var definition = definitions.Find(KnownObject.PlayerStart)!;
        var collector = new SpriteCollector(new SizeF(2000, 2000), new Vector2(130));
        var player = new Sprite(0, definition, new Vector3(1000, 1000, 0), 0, definition.MaxHp);
        collector.InsertToHash(player);
        var movement = new PlayerMovement(new SpriteCollision(new MovementMap(), collector));
        new SpriteKinematics(definitions, null!).SetMoveTarget(player, new Sprite(-1, definitions.Empty, new Vector3(1400, 1000, 0), 0, 0));
        movement.Tick(player, 0.18f, 20);

        Assert.Equal(1000, player.Position.X);
        Assert.Equal(999.64f, player.Position.Y, 3);
        Assert.Equal(10, player.Direction.Value);
    }
}
