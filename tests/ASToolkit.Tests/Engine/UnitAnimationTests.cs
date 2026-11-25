using System.Numerics;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class UnitAnimationTests
{
    [Fact]
    public void SetState_StartsAtItsDirectionFrame()
    {
        var sprite = CreateMonster(64);

        sprite.ChangeAnimation(SpriteAnimation.Go);
        AssertFrames(sprite, 48, 48, 55);

        sprite.ChangeAnimation(SpriteAnimation.Fight);
        AssertFrames(sprite, 164, 164, 168);

        sprite.ChangeAnimation(SpriteAnimation.Death);
        AssertFrames(sprite, 4, 4, 4);
    }

    [Fact]
    public void SetDirection_KeepsTheStepWithinTheState()
    {
        var sprite = CreateMonster(64);
        sprite.ChangeAnimation(SpriteAnimation.Go);
        sprite.Frame += 3;

        sprite.ChangeDirection(new Angle(128));

        AssertFrames(sprite, 80, 83, 87);
    }

    [Fact]
    public void Start_OneFrameStand_DrawsOnlyTheTickPhase()
    {
        var map = EngineTestMap.CreateMap();
        var vid = map.FindVid(KnownObject.Monster1)!;
        var random = new GameRandom(7);
        var expected = new GameRandom(7);
        var sprite = new Sprite(vid, Vector3.Zero, default, null, map) { Army = Army.Monsters };

        sprite.Start(SpriteAnimation.Stand, new Angle(64), 1000, random);

        Assert.Equal(1000 - expected.Next() % 61, sprite.LastTickTime);
        AssertFrames(sprite, 4, 4, 4);
        Assert.Equal(expected.Next(), random.Next());
    }

    [Fact]
    public void Damage_LethalHit_EndsTheDeathAtTheNextAnimationTickWithItsChild()
    {
        var map = EngineTestMap.CreateMap();
        var position = map.Flagman(0)!.Position + new Vector3(600, 0, 0);
        var enemy = map.CreateSprite(map.FindVid(KnownObject.Monster1), position, new Angle(64))!;
        while (map.CurrentTime + map.FrameTime < Map.LevelStartTime + 96)
        {
            map.Play(default);
        }

        Map.Action(enemy, ActionCode.Damage, enemy.Hp);
        map.Play(default);
        int steps = 1;
        while (map.Monsters.Contains(enemy) && steps < 20)
        {
            Assert.DoesNotContain(map.GetEffects(), effect => effect.Vid.Id == KnownObject.Monster1Death);
            map.Play(default);
            steps++;
        }

        Assert.InRange(steps, 2, 19);
        Assert.Contains(map.GetEffects(), effect => effect.Vid.Id == KnownObject.Monster1Death);
    }

    private static void AssertFrames(Sprite sprite, int first, int frame, int last)
    {
        Assert.Equal(first, sprite.FirstFrame);
        Assert.Equal(frame, sprite.Frame);
        Assert.Equal(last, sprite.LastFrame);
    }

    private static Sprite CreateMonster(Angle direction)
    {
        var map = EngineTestMap.CreateMap();
        var vid = map.FindVid(KnownObject.Monster1)!;
        var sprite = new Sprite(vid, Vector3.Zero, default, null, map) { Army = Army.Monsters };
        sprite.Start(SpriteAnimation.Stand, direction, 0, new GameRandom(1));

        return sprite;
    }
}
