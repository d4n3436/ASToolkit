using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class UnitAnimationTests
{
    [Fact]
    public void SetState_StartsAtItsDirectionFrame()
    {
        var unit = CreateMonster(64);

        unit.ChangeAnimation(SpriteAnimation.Go);
        AssertFrames(unit, 48, 48, 55);

        unit.ChangeAnimation(SpriteAnimation.Fight);
        AssertFrames(unit, 164, 164, 168);

        unit.ChangeAnimation(SpriteAnimation.Death);
        AssertFrames(unit, 4, 4, 4);
    }

    [Fact]
    public void SetDirection_KeepsTheStepWithinTheState()
    {
        var unit = CreateMonster(64);
        unit.ChangeAnimation(SpriteAnimation.Go);
        unit.Frame += 3;

        unit.SetDirection(new Angle(128));

        AssertFrames(unit, 80, 83, 87);
    }

    [Fact]
    public void Start_OneFrameStand_DrawsOnlyTheTickPhase()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var definition = definitions.Find(KnownObject.Monster1)!;
        var random = new GameRandom(7);
        var expected = new GameRandom(7);
        var unit = new Unit(1, definition, Vector3.Zero, Army.Monsters, definition.MaxHp);

        unit.Start(SpriteAnimation.Stand, new Angle(64), 1000, random);

        Assert.Equal(1000 - expected.Next() % 61, unit.LastTickTime);
        AssertFrames(unit, 4, 4, 4);
        Assert.Equal(expected.Next(), random.Next());
    }

    [Fact]
    public void Damage_LethalHit_EndsTheDeathAtTheNextAnimationTickWithItsChild()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(definitions);
        var position = world.Player.Position + new Vector3(600, 0, 0);
        var enemy = world.AddMonster(definitions.Find(KnownObject.Monster1)!, position, direction: new Angle(64));
        while (world.Time < GameClock.LevelStartTime + 96)
        {
            world.Step(default);
        }

        world.Damage(enemy, enemy.Hp);
        world.Step(default);
        int steps = 1;
        while (world.Monsters.Contains(enemy) && steps < 20)
        {
            Assert.DoesNotContain(world.Effects.Active, effect => effect.Definition.Id == KnownObject.Monster1Death);
            world.Step(default);
            steps++;
        }

        Assert.InRange(steps, 2, 19);
        Assert.Contains(world.Effects.Active, effect => effect.Definition.Id == KnownObject.Monster1Death);
    }

    private static void AssertFrames(Unit unit, int first, int frame, int last)
    {
        Assert.Equal(first, unit.FirstFrame);
        Assert.Equal(frame, unit.Frame);
        Assert.Equal(last, unit.LastFrame);
    }

    private static Unit CreateMonster(Angle direction)
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var definition = definitions.Find(KnownObject.Monster1)!;
        var unit = new Unit(1, definition, Vector3.Zero, Army.Monsters, definition.MaxHp);
        unit.Start(SpriteAnimation.Stand, direction, 0, new GameRandom(1));

        return unit;
    }
}
