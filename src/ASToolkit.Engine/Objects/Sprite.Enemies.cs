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

        for (var next = seeker.Link; next is not null && next.Vid == seeker.Vid.LinkVid && next.Vid.FightChild != 0 && next.Vid.Object.WeaponIndex != 0; next = seeker.Link)
        {
            seeker = next;
        }

        var vid = seeker.Vid;

        if (vid.DetectRange.IsExactlyZero() || vid.Targets == 0)
            return null;

        Sprite? best = null;
        float bestDistance = vid.DetectRange + 1;

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

        if (Vid.WeaponFlags.HasFlag(WeaponFlags.PrefersNearest))
            return distance < bestDistance;

        if (Shooter == best)
            return false;

        if (Shooter == candidate)
            return true;

        if (candidate.Army == Army.Neutral && best.Army != Army.Neutral)
            return false;

        bool bestFlies = best.Vid.Object.SpriteType.HasFlag(SpriteType.Avia);
        bool candidateFlies = candidate.Vid.Object.SpriteType.HasFlag(SpriteType.Avia);

        if (bestFlies != candidateFlies)
            return candidateFlies;

        float range = (ArmedLink?.Vid ?? Vid).BattleRange;

        if (range < bestDistance && distance <= range)
            return true;

        bool candidateArmed = candidate.Vid.Object.SpriteType.HasFlag(SpriteType.Unit) && Map.Action(candidate, ActionCode.GetAmmo) != 0;
        bool bestArmed = best.Vid.Object.SpriteType.HasFlag(SpriteType.Unit) && Map.Action(best, ActionCode.GetAmmo) != 0;

        if (candidateArmed != bestArmed)
            return candidateArmed;

        int candidateRank = (candidate.ArmedLink?.Vid ?? candidate.Vid).Rank;
        int bestRank = (best.ArmedLink?.Vid ?? best.Vid).Rank;

        return bestRank < candidateRank || bestRank <= candidateRank && distance < bestDistance;
    }

    private bool TryTake(Sprite candidate, ref Sprite? best, ref float bestDistance)
    {
        var vid = Vid;

        if ((vid.Targets & candidate.Vid.Object.SpriteType) == 0 || candidate.Vid.MaxHp == 0 || !IsEnemy(candidate) ||
            (candidate.Vid.Object.SpriteType.HasFlag(SpriteType.Avia) && candidate.Owner is not null))
            return false;

        if (vid.HasForwardTargets &&
            Direction.MeasureTurnTo(DirectionTo(candidate)) > ForwardCone)
            return false;

        float distance = Position.AsVector2().MeasureOctagonalDistance(candidate.Position.AsVector2());

        if (distance > vid.DetectRange || distance < vid.MinRange)
            return false;

        bool isAccepted;

        if (vid.Object.SpriteType.HasFlag(SpriteType.Avia) && Shooter is null)
        {
            isAccepted = distance < bestDistance;
        }
        else if (candidate.Vid.Id == KnownObject.PistolMuzzleFlash)
        {
            isAccepted = false;
        }
        else if (bestDistance <= vid.BattleRange)
        {
            isAccepted = distance <= vid.BattleRange && IsBetter(distance, bestDistance, candidate, best);
        }
        else
        {
            isAccepted = distance <= vid.BattleRange || IsBetter(distance, bestDistance, candidate, best);
        }

        if (!isAccepted || (Army == Army.Player && candidate.Vid.Class == SpriteClass.Engine && HasCarOfArmy(candidate, Army.Player)))
            return false;

        best = candidate;
        bestDistance = distance;

        return vid.WeaponFlags.HasFlag(WeaponFlags.RandomTarget) && Map.Random.Next() % RepeatTargetOdds == 0;
    }

    private bool IsEnemy(Sprite target)
    {
        bool ownArmy = Vid.WeaponFlags.HasFlag(WeaponFlags.TargetsOwnArmy);

        return (target.Army != Army || ownArmy) && !target.Vid.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) &&
            (target.Army != Army.Neutral || ownArmy ||
                (Map.CanMonstersAttackNeutralTrains && Army == Army.Monsters && target.Vid.Class == SpriteClass.Engine));
    }
}
