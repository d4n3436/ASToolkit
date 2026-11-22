using System.Runtime.CompilerServices;
using ASToolkit.Core.Engine;
using System.Numerics;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class EnemyMotionTests
{
    [Theory]
    [InlineData(0, 64, 500, 20, 10, 54)]
    [InlineData(250, 1, 500, 20, 1, 0)]
    [InlineData(64, 192, 999999, 20, 64, 128)]
    [InlineData(64, 192, 0, 20, 192, 1)]
    [InlineData(0, 40, 1000, 1, 0, 40)]
    [InlineData(0, 64, 5000, 10, 1, 63)]
    [InlineData(0, 64, 5000, 9, 0, 64)]
    public void TurnToward_HandlesWrappingAndImmediateOrDisabledRotation(byte direction, byte goal, float period, int elapsed, byte expected, byte left)
    {
        byte result = new Angle(direction).TurnToward(new Angle(goal), period, elapsed, out var remaining).Value;

        Assert.Equal(expected, result);
        Assert.Equal(left, remaining.Value);
    }

    [Theory]
    [InlineData(SpriteClass.Unit)]
    [InlineData(SpriteClass.Man)]
    public void Move_BlockedDiagonal_SlidesAlongYFirst(SpriteClass mover)
    {
        var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog());
        var sprite = MakeUnit(new Body(50, 50), 0, mover);
        var obstacle = MakeUnit(new Body(60, 60), 1, SpriteClass.Unit);
        map.Collector.InsertToHash(sprite);
        map.Collector.InsertToHash(obstacle);

        Assert.True(map.Collision.Move(sprite, new Vector3(51, 51, 0)));
        Assert.Equal(new Vector2(50, 51), sprite.Position.AsVector2());
    }

    [Fact]
    public void Move_IntoAnotherBody_PushesItBeforeSliding()
    {
        var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog());
        var sprite = MakeUnit(new Body(50, 50), 0, SpriteClass.Man);
        var pushed = MakeUnit(new Body(60, 50, Push: 1), 1, SpriteClass.Unit);
        map.Collector.InsertToHash(sprite);
        map.Collector.InsertToHash(pushed);

        Assert.True(map.Collision.Move(sprite, new Vector3(51, 50, 0)));
        Assert.Equal(new Vector2(61, 50), pushed.Position.AsVector2());
        Assert.Equal(new Vector2(50, 50), sprite.Position.AsVector2());
    }

    [Fact]
    public void Next_MatchesTheGameSequence()
    {
        var random = new GameRandom(1);
        Assert.Equal([41, 18467, 6334, 26500, 19169, 15724, 11478, 29358], Enumerable.Range(0, 8).Select(_ => random.Next()).ToArray());
    }

    private static Sprite MakeUnit(Body body, int index, SpriteClass spriteClass)
    {
        var obj = default(GameObject);
        Unsafe.AsRef(in obj.SpriteClass) = spriteClass;
        Unsafe.AsRef(in obj.Properties) = ObjectProperties.Hash;
        Unsafe.AsRef(in obj.MoveMask) = (CollisionLayers)body.Mask;
        Unsafe.AsRef(in obj.Size) = new Vector3(body.HalfX * 2, body.HalfY * 2, body.SizeZ);
        var weapon = default(WeaponRecord);
        Unsafe.AsRef(in weapon.Flags) = body.Push != 0 ? WeaponFlags.Pushable : WeaponFlags.None;
        var definition = new SpriteDefinition(KnownObject.Monster1 + index, obj, weapon, 0);

        return new Sprite(index, definition, new Vector3(body.X, body.Y, body.Z), Army.Monsters, 10) { Direction = body.Direction };
    }

    public sealed record Body(float X, float Y, float Z = 0, byte Direction = 64, int Push = 0, int Mask = 1, float HalfX = 5, float HalfY = 5, float SizeZ = 40);
}
