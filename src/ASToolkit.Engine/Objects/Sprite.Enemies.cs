using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Sprite
{
    private const int RepeatTargetOdds = 3;
    private const int ForwardCone = 0x1f;

    [EngineRoutine(0x00444a80)]
    internal Sprite? SeekEnemy(Sprite? flagman)
    {
        var seeker = this;

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
            return flagman is not null && seeker.TryTake(flagman, ref best, ref bestDistance) ? flagman : best;

        var listed = Map.Collector.Listed;

        for (int i = listed.Length - 1; i >= 0; i--)
        {
            if (seeker.TryTake(listed[i], ref best, ref bestDistance))
                return listed[i];
        }

        return best;
    }

    // TODO: Port the trains
    [EngineRoutine(0x004505c0)]
    private static bool HasCarOfArmy(Sprite engine, Army army) => engine.Army == army;

    [EngineRoutine(0x00445c80)]
    private bool IsBetter(float distance, float bestDistance, Sprite candidate, Sprite? best)
    {
        if (best is null)
            return true;

        if (Definition.WeaponFlags.HasFlag(WeaponFlags.PrefersNearest))
            return distance < bestDistance;

        if (Shooter == best)
            return false;

        if (Shooter == candidate)
            return true;

        if (candidate.Army == Army.Neutral && best.Army != Army.Neutral)
            return false;

        bool bestFlies = best.Definition.Object.SpriteType.HasFlag(SpriteType.Avia);
        bool candidateFlies = candidate.Definition.Object.SpriteType.HasFlag(SpriteType.Avia);

        if (bestFlies != candidateFlies)
            return candidateFlies;

        float range = (ArmedLink?.Definition ?? Definition).BattleRange;

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

    private bool TryTake(Sprite candidate, ref Sprite? best, ref float bestDistance)
    {
        var definition = Definition;

        if ((definition.Targets & candidate.Definition.Object.SpriteType) == 0 || candidate.Definition.MaxHp == 0 || !IsEnemy(candidate) ||
            (candidate.Definition.Object.SpriteType.HasFlag(SpriteType.Avia) && candidate.Owner is not null))
            return false;

        if (definition.HasForwardTargets &&
            Direction.MeasureTurnTo(DirectionTo(candidate)) > ForwardCone)
            return false;

        float distance = Position.AsVector2().MeasureOctagonalDistance(candidate.Position.AsVector2());

        if (distance > definition.DetectRange || distance < definition.MinRange)
            return false;

        bool isAccepted;

        if (definition.Object.SpriteType.HasFlag(SpriteType.Avia) && Shooter is null)
        {
            isAccepted = distance < bestDistance;
        }
        else if (candidate.Definition.Id == KnownObject.PistolMuzzleFlash)
        {
            isAccepted = false;
        }
        else if (bestDistance <= definition.BattleRange)
        {
            isAccepted = distance <= definition.BattleRange && IsBetter(distance, bestDistance, candidate, best);
        }
        else
        {
            isAccepted = distance <= definition.BattleRange || IsBetter(distance, bestDistance, candidate, best);
        }

        if (!isAccepted || (Army == Army.Player && candidate.Definition.Class == SpriteClass.Engine && HasCarOfArmy(candidate, Army.Player)))
            return false;

        best = candidate;
        bestDistance = distance;

        return definition.WeaponFlags.HasFlag(WeaponFlags.RandomTarget) && Map.Random.Next() % RepeatTargetOdds == 0;
    }

    private bool IsEnemy(Sprite target)
    {
        bool ownArmy = Definition.WeaponFlags.HasFlag(WeaponFlags.TargetsOwnArmy);

        return (target.Army != Army || ownArmy) && !target.Definition.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) &&
            (target.Army != Army.Neutral || ownArmy ||
                (Map.CanMonstersAttackNeutralTrains && Army == Army.Monsters && target.Definition.Class == SpriteClass.Engine));
    }
}
