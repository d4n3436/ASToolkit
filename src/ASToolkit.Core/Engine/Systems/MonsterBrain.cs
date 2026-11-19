using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class MonsterBrain
{
    private const int IdleScanOdds = 11;
    private const int RepeatTargetOdds = 3;
    private const int ForwardCone = 0x1f;
    private const float WanderReach = 64;
    private const int LinkScanOdds = 4;
    private const int CreaturePeriodMask = 0x7ff;
    private const int CreatureTurn = 0x20;

    private readonly SpriteKinematics _motion;
    private readonly SpriteCollision _movement;
    private readonly Random _random;
    private readonly IReadOnlyList<Sprite> _candidates;
    private readonly GameSession _session;

    public MonsterBrain(SpriteKinematics motion, SpriteCollision movement, Random random, IReadOnlyList<Sprite> candidates, GameSession session)
    {
        _motion = motion;
        _movement = movement;
        _random = random;
        _candidates = candidates;
        _session = session;
    }

    [EngineRoutine(0x00447260)]
    public void Move(Sprite sprite, int elapsed, int framesPerSecond)
    {
        ref readonly var obj = ref sprite.Definition.Object;

        if (sprite is { IsMoving: false, Speed: 0, TurnTicks: 0, VerticalSpeed: 0 } && (obj.Properties & (ObjectProperties.Gravity | ObjectProperties.Gravity2)) == 0)
            return;

        var next = _motion.Move(sprite, elapsed);

        if (sprite.TurnTicks == 0)
        {
            if (sprite.HasGoal && !sprite.Speed.IsZero())
            {
                var goal = Angle.FromOffset((sprite.GoalPosition - sprite.Position).AsVector2());

                if (sprite.Speed < 0)
                {
                    goal = goal.Opposite;
                }
                goal = _movement.RotateForGlide(goal, sprite);
                sprite.ChangeDirection(sprite.TurnWhileMoving(goal, elapsed));

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
            sprite.ChangeDirection(sprite.TurnWhileMoving(goal, elapsed));
            sprite.TurnTicks -= Math.Sign(sprite.TurnTicks);
        }

        if (next == sprite.Position)
            return;

        if (_movement.Move(sprite, next))
        {
            RepelFromMapEdge(sprite, elapsed);
        }
        else if (sprite.TurnTicks == 0)
        {
            sprite.TurnTicks = (_random.Next(2) == 0 ? -1 : 1) * framesPerSecond / 2;
        }
    }

    public void Decide(Sprite sprite, Sprite player, int elapsed, int time)
    {
        if (sprite.Definition.Object.SpriteClass == SpriteClass.Creature)
        {
            DecideCreature(sprite, elapsed, time);

            return;
        }

        Act(sprite, player, elapsed);
    }

    [EngineRoutine(0x0044d880)]
    private void DecideCreature(Sprite sprite, int elapsed, int time)
    {
        if (sprite.Animation >= SpriteAnimation.Death)
            return;

        if (sprite.Animation is SpriteAnimation.Fight or SpriteAnimation.Wound)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (sprite is { HasGoal: true, Command: Command.Move })
        {
            if (!sprite.Speed.IsZero())
            {
                if (sprite.Animation != SpriteAnimation.Go)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Go);
                }

                return;
            }

            sprite.ChangeAnimation(SpriteAnimation.Stand);

            return;
        }

        bool newPeriod = (uint)sprite.LastTickTime < (uint)(time - (time & CreaturePeriodMask));

        if (sprite.Command is Command.Attack or Command.OneAttack || newPeriod)
        {
            sprite.AttackTact(Math.Max(elapsed, sprite.Definition.Layout.GetFrameDuration(sprite.Animation)));
        }

        int frameTime = (byte)sprite.Definition.Layout.GetFrameDuration(sprite.Animation);

        if (sprite.Command is not (Command.Move or Command.Attack or Command.OneAttack))
        {
            if (sprite.Animation == SpriteAnimation.LRotate && _random.Next() % 3 != 0)
            {
                TurnStep(sprite, -CreatureTurn, frameTime);
            }

            if (sprite.Animation == SpriteAnimation.RRotate && _random.Next() % 3 != 0)
            {
                TurnStep(sprite, CreatureTurn, frameTime);
            }

            if (newPeriod)
            {
                if ((_random.Next() & 3) == 0)
                {
                    _motion.StartMove(sprite);
                }
                else if (sprite.Speed.IsZero() && _random.Next() % 3 == 0 && HasUnload(sprite.Definition))
                {
                    sprite.ChangeAnimation(SpriteAnimation.Unload);
                }
                else if ((_random.Next() & 3) == 0)
                {
                    TurnStep(sprite, (_random.Next() & 1) == 0 ? CreatureTurn : -CreatureTurn, frameTime);
                }
                else
                {
                    SpriteKinematics.Stop(sprite);
                }
            }
        }

        if (sprite.Animation is SpriteAnimation.Wound or SpriteAnimation.Salut)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }
    }

    private static bool HasUnload(SpriteDefinition definition) =>
        definition.Object.FrameCounts[(int)SpriteAnimation.Unload] != 0 || definition.Children[(int)SpriteAnimation.Unload] != 0;

    private static void TurnStep(Sprite sprite, int step, int elapsed) =>
        sprite.RotateTact(sprite.Direction + step, elapsed);

    [EngineRoutine(0x00440830)]
    public static void Pause(Sprite sprite, int time)
    {
        if (!sprite.Speed.IsZero())
        {
            SpriteKinematics.Stop(sprite);
        }

        sprite.Timer = time;
        SpriteKinematics.SetCommand(sprite, time != 0 ? Command.Pause : Command.None);
    }

    [EngineRoutine(0x00445fb0)]
    public static void Attack(Sprite sprite, Sprite? target)
    {
        var definition = sprite.Definition;
        bool accepts = target is null || definition.IsArmed && definition.FightChild != 0 && (definition.Targets & target.Definition.Object.SpriteType) != 0;

        if (accepts || definition.Object.SpriteClass == SpriteClass.Cannon)
        {
            SpriteKinematics.SetCommand(sprite, Command.Attack, target);
        }
    }

    [EngineRoutine(0x00447500)]
    private void Act(Sprite sprite, Sprite player, int elapsed)
    {
        if (sprite.Animation >= SpriteAnimation.Death)
            return;

        if (sprite.Speed.IsZero())
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }
        else if (sprite.Animation != SpriteAnimation.Go)
        {
            sprite.ChangeAnimation(SpriteAnimation.Go);
        }

        var definition = sprite.Definition;
        var link = sprite.ArmedLink;
        var result = AttackResult.Pursue;

        if (link is not null && sprite.Command is Command.Attack or Command.OneAttack && sprite.Goal is not null && link.Goal is null)
        {
            SpriteKinematics.SetCommand(link, sprite.Command, sprite.Goal);
        }

        if (link is not null || sprite.TurnTicks == 0)
        {
            result = sprite.AttackTact(Math.Max(elapsed, definition.Layout.GetFrameDuration(sprite.Animation)));

            switch (result)
            {
                case AttackResult.NoAmmo or AttackResult.GiveUp:
                    SpriteKinematics.SetCommand(sprite, Command.None);
                    break;
                case AttackResult.Approach when definition.Speed.IsZero():
                    if (link is not null && link.HasGoal || sprite.HasGoal)
                    {
                        SpriteKinematics.SetCommand(sprite, Command.None);
                    }
                    break;
                case AttackResult.Approach when sprite.Speed.IsZero():
                    _motion.StartMove(sprite);
                    break;
                case AttackResult.Attacked when !sprite.Speed.IsZero() && (link is null || sprite.Goal == link.Goal):
                    SpriteKinematics.Stop(sprite);
                    break;
            }

            if (result == AttackResult.Approach && link is not null && sprite.Command is Command.Attack or Command.OneAttack &&
                sprite.IsAggressive && (link.Timer != 0 || _random.Next() % LinkScanOdds == 0))
            {
                var target = SeekEnemy(sprite, player);

                if (target is not null)
                {
                    SpriteKinematics.SetCommand(link, Command.OneAttack, target);
                }
            }
        }

        if (result == AttackResult.Pursue && sprite.IsActive && sprite.Speed.IsZero())
        {
            _motion.StartMove(sprite);
        }

        if (result == AttackResult.NoOrder && sprite is { IsAggressive: true, IsActive: true } ||
            result is AttackResult.Pursue or AttackResult.NoTarget && sprite is { IsAggressive: true, Command: Command.None or Command.Move or Command.OneAttack } &&
            (sprite.LastFrame > sprite.FirstFrame || sprite.Timer != 0 || _random.Next(IdleScanOdds) == 0))
        {
            Engage(sprite, player);
        }

        if (sprite is { Command: Command.None, HasGoal: false, Timer: 0 })
        {
            SpriteKinematics.Stop(sprite);
        }
    }

    [EngineRoutine(0x00447500)]
    public void Wander(Sprite sprite)
    {
        if (sprite.Command != Command.None || sprite.Definition.Speed.IsZero())
            return;

        var vector = new Angle((byte)_random.Next()).Vector;
        MoveTo(sprite, sprite.Position.AsVector2() + vector * WanderReach);
    }

    [EngineRoutine(0x004447b0)]
    public void MoveTo(Sprite sprite, Vector2 point)
    {
        if (!SpriteKinematics.SetCommand(sprite, Command.Move, point: new Vector3(point, sprite.Position.Z)) && !_motion.StartMove(sprite))
        {
            SpriteKinematics.SetCommand(sprite, Command.None);
        }
    }

    private void Engage(Sprite sprite, Sprite player)
    {
        var target = SeekEnemy(sprite, player);

        if (target is not null)
        {
            SpriteKinematics.SetCommand(sprite, Command.OneAttack, target);
        }
    }

    [EngineRoutine(0x00444a80)]
    private Sprite? SeekEnemy(Sprite sprite, Sprite player)
    {
        var definition = sprite.Definition;

        if (definition.DetectRange.IsZero() || definition.Targets == 0)
            return null;

        Sprite? best = null;
        float bestDistance = definition.DetectRange + 1;

        if (sprite.Army == Army.Monsters)
            return TryTake(sprite, player, ref best, ref bestDistance) ? player : best;

        if (TryTake(sprite, player, ref best, ref bestDistance))
            return player;

        foreach (var candidate in _candidates)
        {
            if (candidate != sprite && TryTake(sprite, candidate, ref best, ref bestDistance))
                return candidate;
        }

        return best;
    }

    private bool TryTake(Sprite sprite, Sprite candidate, ref Sprite? best, ref float bestDistance)
    {
        var definition = sprite.Definition;

        if ((definition.Targets & candidate.Definition.Object.SpriteType) == 0 || candidate.Definition.MaxHp == 0 || !IsEnemy(sprite, candidate) ||
            (candidate.Definition.Object.SpriteType.HasFlag(SpriteType.Avia) && candidate.Owner is not null))
            return false;

        if (definition.NeedsForwardTarget &&
            sprite.Direction.MeasureTurnTo(Angle.FromOffset((candidate.Position - sprite.Position).AsVector2())) > ForwardCone)
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

    private bool IsEnemy(Sprite sprite, Sprite target)
    {
        bool ownArmy = sprite.Definition.WeaponFlags.HasFlag(WeaponFlags.TargetsOwnArmy);

        return (target.Army != sprite.Army || ownArmy) && !target.Definition.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) &&
            (target.Army != Army.Neutral || ownArmy ||
                (_session.CanMonstersAttackNeutralTrains && sprite.Army == Army.Monsters && target.Definition.Object.SpriteClass == SpriteClass.Engine));
    }

    [EngineRoutine(0x0043fa00)]
    private void RepelFromMapEdge(Sprite sprite, int elapsed)
    {
        var position = sprite.Position;
        var size = _movement.MapSize;
        int goal = position.X < 0 ? Angle.QuarterTurn : position.Y < 0 ? Angle.HalfTurn : position.X >= size.Width ? Angle.HalfTurn + Angle.QuarterTurn : position.Y >= size.Height ? 0 : -1;

        if (goal >= 0)
        {
            sprite.RotateTact(new Angle((byte)goal), elapsed);
        }
    }
}
