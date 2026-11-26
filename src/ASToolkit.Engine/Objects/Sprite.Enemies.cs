using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Sprite
{
    private const int RepeatTargetOdds = 3;
    private const int ForwardCone = 0x1f;

    [EngineRoutine(0x00444a80)]
    internal Sprite? SeekEnemy()
    {
        var seeker = this;

        for (var next = seeker.Link; next is not null && next.Vid == seeker.Vid.LinkVid && next.Vid.FightChild != 0 && next.Vid.WeaponIndex != 0; next = seeker.Link)
        {
            seeker = next;
        }

        var vid = seeker.Vid;

        if (vid.Weapon.DetectRange.IsExactlyZero() || vid.Weapon.Targets == 0)
            return null;

        Sprite? best = null;
        float bestDistance = vid.Weapon.DetectRange + 1;

        bool isMonster = seeker.Army == Army.Monsters;
        var listed = Map.Sprites.ListedSprites;
        int index = 0;

        for (var candidate = isMonster ? Map.Flagman(0) : listed.BeginIterate(ref index); candidate is not null; candidate = isMonster ? null : listed.NextIterate(ref index))
        {

            if ((vid.Weapon.Targets & candidate.Vid.SpriteType) == 0 || candidate.Vid.MaxHp == 0 || !((candidate.Army != seeker.Army || seeker.Vid.Weapon.Flags.HasFlag(WeaponFlags.TargetsOwnArmy)) && !candidate.Vid.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) &&
                (candidate.Army != Army.Neutral || seeker.Vid.Weapon.Flags.HasFlag(WeaponFlags.TargetsOwnArmy) ||
                    (Map.CanMonstersAttackNeutralTrains && seeker.Army == Army.Monsters && candidate.Vid.Class == SpriteClass.Engine))) ||
                (candidate.Vid.SpriteType.HasFlag(OldSpriteType.Avia) && candidate.Owner is not null))
                continue;

            if (vid.HasForwardTargets &&
                seeker.Direction.MeasureTurnTo(seeker.DirectionTo(candidate)) > ForwardCone)
                continue;

            float distance = seeker.Position.AsVector2().NearLength(candidate.Position.AsVector2());

            if (distance > vid.Weapon.DetectRange || distance < vid.Weapon.MinRange)
                continue;

            bool isAccepted;

            if (vid.SpriteType.HasFlag(OldSpriteType.Avia) && seeker.Shooter is null)
            {
                isAccepted = distance < bestDistance;
            }
            else if (candidate.Vid.Id == KnownObject.PistolMuzzleFlash)
            {
                isAccepted = false;
            }
            else if (bestDistance <= vid.Weapon.BattleRange)
            {
                isAccepted = distance <= vid.Weapon.BattleRange && seeker.IsBetter(distance, bestDistance, candidate, best);
            }
            else
            {
                isAccepted = distance <= vid.Weapon.BattleRange || seeker.IsBetter(distance, bestDistance, candidate, best);
            }

            if (!isAccepted || (seeker.Army == Army.Player && candidate.Vid.Class == SpriteClass.Engine && HasCarOfArmy(candidate, Army.Player)))
                continue;

            best = candidate;
            bestDistance = distance;

            if (vid.Weapon.Flags.HasFlag(WeaponFlags.RandomTarget) && Map.Random.Next() % RepeatTargetOdds == 0)
                return candidate;
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

        if (Vid.Weapon.Flags.HasFlag(WeaponFlags.PrefersNearest))
            return distance < bestDistance;

        if (Shooter == best)
            return false;

        if (Shooter == candidate)
            return true;

        if (candidate.Army == Army.Neutral && best.Army != Army.Neutral)
            return false;

        bool bestFlies = best.Vid.SpriteType.HasFlag(OldSpriteType.Avia);
        bool candidateFlies = candidate.Vid.SpriteType.HasFlag(OldSpriteType.Avia);

        if (bestFlies != candidateFlies)
            return candidateFlies;

        float range = Vid.Weapon.BattleRange;
        var armedLink = ArmedLink;
        if (armedLink is not null && range.IsExactlyZero())
        {
            range = armedLink.Vid.Weapon.BattleRange;
        }

        if (range < bestDistance && distance <= range)
            return true;

        bool candidateArmed = candidate.Vid.SpriteType.HasFlag(OldSpriteType.Unit) && Map.Action(candidate, ActionCode.GetAmmo) != 0;
        bool bestArmed = best.Vid.SpriteType.HasFlag(OldSpriteType.Unit) && Map.Action(best, ActionCode.GetAmmo) != 0;

        if (candidateArmed != bestArmed)
            return candidateArmed;

        int candidateRank = (candidate.ArmedLink?.Vid ?? candidate.Vid).Weapon.Rank;
        int bestRank = (best.ArmedLink?.Vid ?? best.Vid).Weapon.Rank;

        return bestRank < candidateRank || bestRank <= candidateRank && distance < bestDistance;
    }
}
