using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class MonsterBrain
{
    private const int RepeatTargetOdds = 3;
    private const int ForwardCone = 0x1f;

    private readonly SpriteKinematics _motion;
    private readonly SpriteCollision _movement;
    private readonly Random _random;
    private readonly SpriteCollector _collector;
    private readonly GameSession _session;

    public MonsterBrain(SpriteKinematics motion, SpriteCollision movement, Random random, SpriteCollector collector, GameSession session)
    {
        _motion = motion;
        _movement = movement;
        _random = random;
        _collector = collector;
        _session = session;
    }

    [EngineRoutine(0x00447260)]
    public void Move(Sprite sprite, int elapsed, int framesPerSecond)
    {
        var next = _motion.CalculateNewCoordinates(sprite, elapsed);

        if (sprite.TurnTicks == 0)
        {
            if (sprite.HasGoal && !sprite.Speed.IsExactlyZero())
            {
                var goal = sprite.MeasureDirectionTo(sprite.Goal!);

                if (sprite.Speed < 0)
                {
                    goal = goal.Opposite;
                }
                goal = _movement.RotateForGlide(goal, sprite);
                sprite.RotateTact(goal, elapsed);

                if (sprite is { HasReachedX: true, HasReachedY: true })
                {
                    SpriteKinematics.Stop(sprite);
                }
            }
        }
        else
        {
            if (Math.Abs(sprite.TurnTicks) > framesPerSecond)
            {
                sprite.TurnTicks = Math.Sign(sprite.TurnTicks) * framesPerSecond / 2;
            }
            var goal = sprite.Direction + (sprite.TurnTicks > 0 ? Angle.QuarterTurn : -Angle.QuarterTurn);
            sprite.RotateTact(goal, elapsed);
            sprite.TurnTicks -= Math.Sign(sprite.TurnTicks);
        }

        if (next == sprite.Position)
            return;

        if (_movement.Move(sprite, ref next))
        {
            RepelFromMapEdge(sprite, next, elapsed);
            sprite.ChangeCoor(next);
        }
        else if (sprite.TurnTicks == 0)
        {
            sprite.TurnTicks = (_random.Next(2) == 0 ? -1 : 1) * framesPerSecond / 2;
        }
    }

    public void MoveTo(Sprite sprite, Sprite goal)
    {
        if (!SpriteKinematics.SetCommand(sprite, Command.Move, goal) && !_motion.StartMove(sprite))
        {
            SpriteKinematics.SetCommand(sprite, Command.None);
        }
    }

    [EngineRoutine(0x00444a80)]
    public Sprite? SeekEnemy(Sprite sprite, Sprite? flagman)
    {
        var definition = sprite.Definition;

        if (definition.DetectRange.IsExactlyZero() || definition.Targets == 0)
            return null;

        Sprite? best = null;
        float bestDistance = definition.DetectRange + 1;

        if (sprite.Army == Army.Monsters)
            return flagman is not null && TryTake(sprite, flagman, ref best, ref bestDistance) ? flagman : best;

        if (flagman is not null && TryTake(sprite, flagman, ref best, ref bestDistance))
            return flagman;

        foreach (var candidate in _collector.Listed)
        {
            if (candidate != sprite && TryTake(sprite, candidate, ref best, ref bestDistance))
                return candidate;
        }

        return best;
    }

    // TODO: Port the trains
    [EngineRoutine(0x004505c0)]
    private static bool HasCarOfArmy(Sprite engine, Army army) => engine.Army == army;

    [EngineRoutine(0x00445c80)]
    private static bool IsBetter(Sprite sprite, float distance, float bestDistance, Sprite candidate, Sprite? best)
    {
        if (best is null)
            return true;

        if (sprite.Definition.WeaponFlags.HasFlag(WeaponFlags.PrefersNearest))
            return distance < bestDistance;

        if (sprite.Goal == best)
            return false;

        if (sprite.Goal == candidate)
            return true;

        if (candidate.Army == Army.Neutral && best.Army != Army.Neutral)
            return false;

        bool bestFlies = best.Definition.Object.SpriteType.HasFlag(SpriteType.Avia);
        bool candidateFlies = candidate.Definition.Object.SpriteType.HasFlag(SpriteType.Avia);

        if (bestFlies != candidateFlies)
            return candidateFlies;

        float range = (sprite.ArmedLink?.Definition ?? sprite.Definition).BattleRange;

        if (range < bestDistance && distance <= range)
            return true;

        bool candidateArmed = candidate.Definition.Object.SpriteType.HasFlag(SpriteType.Unit) && candidate.Rounds != 0;
        bool bestArmed = best.Definition.Object.SpriteType.HasFlag(SpriteType.Unit) && best.Rounds != 0;

        if (candidateArmed != bestArmed)
            return candidateArmed;

        int candidateRank = (candidate.ArmedLink?.Definition ?? candidate.Definition).Rank;
        int bestRank = (best.ArmedLink?.Definition ?? best.Definition).Rank;

        return bestRank < candidateRank || bestRank <= candidateRank && distance < bestDistance;
    }

    private bool TryTake(Sprite sprite, Sprite candidate, ref Sprite? best, ref float bestDistance)
    {
        var definition = sprite.Definition;

        if ((definition.Targets & candidate.Definition.Object.SpriteType) == 0 || candidate.Definition.MaxHp == 0 || !IsEnemy(sprite, candidate) ||
            (candidate.Definition.Object.SpriteType.HasFlag(SpriteType.Avia) && candidate.Owner is not null))
            return false;

        if (definition.NeedsForwardTarget &&
            sprite.Direction.MeasureTurnTo(sprite.MeasureDirectionTo(candidate)) > ForwardCone)
            return false;

        float distance = sprite.Position.AsVector2().MeasureOctagonalDistance(candidate.Position.AsVector2());

        if (distance > definition.DetectRange || distance < definition.MinRange)
            return false;

        bool accepts;

        if (definition.Object.SpriteType.HasFlag(SpriteType.Avia) && sprite.Goal is null)
        {
            accepts = distance < bestDistance;
        }
        else if (candidate.Definition.Id == KnownObject.PistolMuzzleFlash)
        {
            accepts = false;
        }
        else if (bestDistance <= definition.BattleRange)
        {
            accepts = distance <= definition.BattleRange && IsBetter(sprite, distance, bestDistance, candidate, best);
        }
        else
        {
            accepts = distance <= definition.BattleRange || IsBetter(sprite, distance, bestDistance, candidate, best);
        }

        if (!accepts || (sprite.Army == Army.Player && candidate.Definition.Object.SpriteClass == SpriteClass.Engine && HasCarOfArmy(candidate, Army.Player)))
            return false;

        best = candidate;
        bestDistance = distance;

        return definition.WeaponFlags.HasFlag(WeaponFlags.RandomTarget) && _random.Next() % RepeatTargetOdds == 0;
    }

    private bool IsEnemy(Sprite sprite, Sprite target)
    {
        bool ownArmy = sprite.Definition.WeaponFlags.HasFlag(WeaponFlags.TargetsOwnArmy);

        return (target.Army != sprite.Army || ownArmy) && !target.Definition.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) &&
            (target.Army != Army.Neutral || ownArmy ||
                (_session.CanMonstersAttackNeutralTrains && sprite.Army == Army.Monsters && target.Definition.Object.SpriteClass == SpriteClass.Engine));
    }

    [EngineRoutine(0x0043fa00)]
    private void RepelFromMapEdge(Sprite sprite, Vector3 position, int elapsed)
    {
        var size = _movement.MapSize;
        int goal = position.X < 0 ? Angle.QuarterTurn : position.Y < 0 ? Angle.HalfTurn : position.X >= size.Width ? Angle.HalfTurn + Angle.QuarterTurn : position.Y >= size.Height ? 0 : -1;

        if (goal >= 0)
        {
            sprite.RotateTact(new Angle((byte)goal), elapsed);
        }
    }
}
