using System.Numerics;
using ASToolkit.Core;
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
    private readonly Map _map;

    public MonsterBrain(SpriteKinematics motion, SpriteCollision movement, Random random, SpriteCollector collector, Map map)
    {
        _motion = motion;
        _movement = movement;
        _random = random;
        _collector = collector;
        _map = map;
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
        var seeker = sprite;

        for (var next = seeker.Link; next is not null && next.Definition == seeker.Definition.LinkDefinition && next.Definition.FightChild != 0 && next.Definition.Object.WeaponIndex != 0; next = seeker.Link)
        {
            seeker = next;
        }

        var definition = seeker.Definition;

        if (definition.DetectRange.IsExactlyZero() || definition.Targets == 0)
            return null;

        Sprite? best = null;
        float bestDistance = definition.DetectRange + 1;

        if (seeker.Army == Army.Monsters)
            return flagman is not null && TryTake(seeker, flagman, ref best, ref bestDistance) ? flagman : best;

        var listed = _collector.Listed;

        for (int i = listed.Length - 1; i >= 0; i--)
        {
            if (TryTake(seeker, listed[i], ref best, ref bestDistance))
                return listed[i];
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

        if (sprite.Shooter == best)
            return false;

        if (sprite.Shooter == candidate)
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

        bool candidateArmed = candidate.Definition.Object.SpriteType.HasFlag(SpriteType.Unit) && Map.Action(candidate, ActionCode.GetAmmo) != 0;
        bool bestArmed = best.Definition.Object.SpriteType.HasFlag(SpriteType.Unit) && Map.Action(best, ActionCode.GetAmmo) != 0;

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

        if (definition.HasForwardTargets &&
            sprite.Direction.MeasureTurnTo(sprite.DirectionTo(candidate)) > ForwardCone)
            return false;

        float distance = sprite.Position.AsVector2().MeasureOctagonalDistance(candidate.Position.AsVector2());

        if (distance > definition.DetectRange || distance < definition.MinRange)
            return false;

        bool isAccepted;

        if (definition.Object.SpriteType.HasFlag(SpriteType.Avia) && sprite.Shooter is null)
        {
            isAccepted = distance < bestDistance;
        }
        else if (candidate.Definition.Id == KnownObject.PistolMuzzleFlash)
        {
            isAccepted = false;
        }
        else if (bestDistance <= definition.BattleRange)
        {
            isAccepted = distance <= definition.BattleRange && IsBetter(sprite, distance, bestDistance, candidate, best);
        }
        else
        {
            isAccepted = distance <= definition.BattleRange || IsBetter(sprite, distance, bestDistance, candidate, best);
        }

        if (!isAccepted || (sprite.Army == Army.Player && candidate.Definition.Class == SpriteClass.Engine && HasCarOfArmy(candidate, Army.Player)))
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
                (_map.CanMonstersAttackNeutralTrains && sprite.Army == Army.Monsters && target.Definition.Class == SpriteClass.Engine));
    }
}
