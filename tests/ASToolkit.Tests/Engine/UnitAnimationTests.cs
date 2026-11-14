using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class UnitAnimationTests
{
    [Fact]
    public void SetState_StartsAtTheFacingsDirection()
    {
        var unit = CreateMonster(64);

        unit.SetState(SpriteAnimation.Go);
        AssertFrames(unit, 48, 48, 55);

        unit.SetState(SpriteAnimation.Fight);
        AssertFrames(unit, 164, 164, 168);

        unit.SetState(SpriteAnimation.Death);
        AssertFrames(unit, 4, 4, 4);
    }

    [Fact]
    public void SetFacing_KeepsTheStepWithinTheState()
    {
        var unit = CreateMonster(64);
        unit.SetState(SpriteAnimation.Go);
        unit.Frame += 3;

        unit.SetFacing(new Facing(128));

        AssertFrames(unit, 80, 83, 87);
    }

    [Fact]
    public void Start_OneFrameStand_DrawsOnlyTheTickPhase()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var kind = kinds.Find(KnownObject.Monster1)!;
        var random = new GameRandom(7);
        var expected = new GameRandom(7);
        var unit = new Unit(1, kind, Vector3.Zero, 1, kind.MaxHp);

        unit.Start(SpriteAnimation.Stand, new Facing(64), 1000, random);

        Assert.Equal(1000 - expected.Next() % 61, unit.LastTickTime);
        AssertFrames(unit, 4, 4, 4);
        Assert.Equal(expected.Next(), random.Next());
    }

    [Fact]
    public void Damage_LethalHit_EndsTheDeathAtTheNextAnimationTickWithItsChild()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var position = world.Player.Position + new Vector3(600, 0, 0);
        var enemy = world.AddMonster(kinds.Find(KnownObject.Monster1)!, position, facing: new Facing(64));
        while (world.Time < 96)
        {
            world.Step(default);
        }

        world.Damage(enemy, enemy.Hp);
        world.Step(default);
        world.Step(default);
        Assert.Contains(enemy, world.Monsters);
        Assert.DoesNotContain(world.Effects.Active, effect => effect.Kind.Id == KnownObject.Monster1Death);

        world.Step(default);

        Assert.DoesNotContain(enemy, world.Monsters);
        Assert.Contains(world.Effects.Active, effect => effect.Kind.Id == KnownObject.Monster1Death);
    }

    private static void AssertFrames(Unit unit, int first, int frame, int last)
    {
        Assert.Equal(first, unit.FirstFrame);
        Assert.Equal(frame, unit.Frame);
        Assert.Equal(last, unit.LastFrame);
    }

    private static Unit CreateMonster(byte facing)
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var kind = kinds.Find(KnownObject.Monster1)!;
        var unit = new Unit(1, kind, Vector3.Zero, 1, kind.MaxHp);
        unit.Start(SpriteAnimation.Stand, new Facing(facing), 0, new GameRandom(1));

        return unit;
    }
}
