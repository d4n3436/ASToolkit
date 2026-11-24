using System.Numerics;
using System.Runtime.CompilerServices;
using ASToolkit.Engine;
using ASToolkit.Formats;
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
    public void RotateTact_HandlesWrappingAndImmediateOrDisabledRotation(byte direction, byte goal, float period, int elapsed, byte expected, byte left)
    {
        var obj = default(GameObject);
        Unsafe.AsRef(in obj.SpriteClass) = SpriteClass.Unit;
        Unsafe.AsRef(in obj.Directions) = 1;
        Unsafe.AsRef(in obj.RotationPeriod) = period;
        Unsafe.AsRef(in obj.Size) = new Vector3(10, 10, 40);
        var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog());
        var sprite = new Sprite(0, new SpriteDefinition(KnownObject.Monster1, obj, default, 0), Vector3.Zero, Army.Monsters, 10, map) { Direction = new Angle(direction) };

        var remaining = sprite.RotateTact(new Angle(goal), elapsed);

        Assert.Equal(expected, sprite.Direction.Value);
        Assert.Equal(left, remaining.Value);
    }

    [Theory]
    [InlineData(SpriteClass.Unit)]
    [InlineData(SpriteClass.Man)]
    public void Move_BlockedDiagonal_SlidesAlongYFirst(SpriteClass mover)
    {
        var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog());
        var sprite = MakeUnit(map, new UnitBody(50, 50), 0, mover);
        var obstacle = MakeUnit(map, new UnitBody(60, 60), 1, SpriteClass.Unit);
        map.Collector.InsertToHash(sprite);
        map.Collector.InsertToHash(obstacle);

        Assert.True(map.Collision.Move(sprite, new Vector3(51, 51, 0)));
        Assert.Equal(new Vector2(50, 51), sprite.Position.AsVector2());
    }

    [Fact]
    public void Move_IntoAnotherBody_PushesItBeforeSliding()
    {
        var map = EngineTestMap.CreateMap(TestGameFolder.CreateCatalog());
        var sprite = MakeUnit(map, new UnitBody(50, 50), 0, SpriteClass.Man);
        var pushed = MakeUnit(map, new UnitBody(60, 50, Push: 1), 1, SpriteClass.Unit);
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

    private static Sprite MakeUnit(Map map, UnitBody body, int index, SpriteClass spriteClass)
    {
        var obj = default(GameObject);
        Unsafe.AsRef(in obj.SpriteClass) = spriteClass;
        Unsafe.AsRef(in obj.Directions) = 1;
        Unsafe.AsRef(in obj.Properties) = ObjectProperties.Hash;
        Unsafe.AsRef(in obj.MoveMask) = (CollisionLayers)body.Mask;
        Unsafe.AsRef(in obj.Size) = new Vector3(body.HalfX * 2, body.HalfY * 2, body.SizeZ);
        var weapon = default(WeaponRecord);
        Unsafe.AsRef(in weapon.Flags) = body.Push != 0 ? WeaponFlags.Pushable : WeaponFlags.None;
        var definition = new SpriteDefinition(KnownObject.Monster1 + index, obj, weapon, 0);

        return new Sprite(index, definition, new Vector3(body.X, body.Y, body.Z), Army.Monsters, 10, map) { Direction = body.Direction };
    }
}
