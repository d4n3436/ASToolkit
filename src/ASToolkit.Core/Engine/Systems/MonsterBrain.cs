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

    private readonly UnitKinematics _motion;
    private readonly UnitCollision _movement;
    private readonly Random _random;
    private readonly IReadOnlyList<Unit> _candidates;

    public MonsterBrain(UnitKinematics motion, UnitCollision movement, Random random, IReadOnlyList<Unit> candidates)
    {
        _motion = motion;
        _movement = movement;
        _random = random;
        _candidates = candidates;
    }

    [EngineRoutine(0x00447260)]
    public void Move(Unit unit, int elapsed, int framesPerSecond)
    {
        ref readonly var obj = ref unit.Kind.Object;

        if (unit is { Moving: false, Speed: 0, TurnTicks: 0, VerticalSpeed: 0 } && (obj.Properties & (ObjectProperties.Gravity | ObjectProperties.Gravity2)) == 0)
            return;

        var next = _motion.Move(unit, elapsed);

        if (unit.TurnTicks == 0)
        {
            if (unit.HasGoal && !unit.Speed.IsZero())
            {
                var goal = Angle.FromOffset((unit.GoalPosition - unit.Position).AsVector2());

                if (unit.Speed < 0)
                {
                    goal = goal.Opposite;
                }
                goal = _movement.AvoidObstacles(goal, unit);
                unit.SetDirection(unit.TurnWhileMoving(goal, elapsed));

                if (unit is { ReachedX: true, ReachedY: true })
                {
                    UnitKinematics.StopMoving(unit);
                }
            }
        }
        else
        {
            if (Math.Abs(unit.TurnTicks) > framesPerSecond)
            {
                unit.TurnTicks = Math.Sign(unit.TurnTicks) * framesPerSecond / 2;
            }
            var goal = unit.Direction + (unit.TurnTicks > 0 ? Angle.QuarterTurn : -Angle.QuarterTurn);
            unit.SetDirection(unit.TurnWhileMoving(goal, elapsed));
            unit.TurnTicks -= Math.Sign(unit.TurnTicks);
        }

        if (next == unit.Position)
            return;

        if (_movement.Move(unit, next))
        {
            RepelFromMapEdge(unit, elapsed);
        }
        else if (unit.TurnTicks == 0)
        {
            unit.TurnTicks = (_random.Next(2) == 0 ? -1 : 1) * framesPerSecond / 2;
        }
    }

    public void Decide(Unit unit, Unit player, int elapsed, int time)
    {
        if (unit.Kind.Object.SpriteClass == SpriteClass.Creature)
        {
            DecideCreature(unit, elapsed, time);

            return;
        }

        if (unit.Command != UnitCommand.None || !RunProgram(unit))
        {
            Act(unit, player, elapsed);
        }
    }

    [EngineRoutine(0x0044d880)]
    private void DecideCreature(Unit unit, int elapsed, int time)
    {
        if (unit.Animation >= SpriteAnimation.Death)
            return;

        if (unit.Animation is SpriteAnimation.Fight or SpriteAnimation.Wound)
        {
            unit.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (unit is { HasGoal: true, Command: UnitCommand.Move })
        {
            if (!unit.Speed.IsZero())
            {
                if (unit.Animation != SpriteAnimation.Go)
                {
                    unit.ChangeAnimation(SpriteAnimation.Go);
                }

                return;
            }

            unit.ChangeAnimation(SpriteAnimation.Stand);

            return;
        }

        bool newPeriod = (uint)unit.LastTickTime < (uint)(time - (time & CreaturePeriodMask));

        if (unit.Command is UnitCommand.Attack or UnitCommand.OneAttack || newPeriod)
        {
            unit.TryAttack(Math.Max(elapsed, unit.Kind.Layout.GetFrameDuration(unit.Animation)));
        }

        int frameTime = (byte)unit.Kind.Layout.GetFrameDuration(unit.Animation);

        if (unit.Command is not (UnitCommand.Move or UnitCommand.Attack or UnitCommand.OneAttack))
        {
            if (unit.Animation == SpriteAnimation.LRotate && _random.Next() % 3 != 0)
            {
                TurnStep(unit, -CreatureTurn, frameTime);
            }

            if (unit.Animation == SpriteAnimation.RRotate && _random.Next() % 3 != 0)
            {
                TurnStep(unit, CreatureTurn, frameTime);
            }

            if (newPeriod)
            {
                if ((_random.Next() & 3) == 0)
                {
                    unit.StartMoving(unit.MaxSpeed);
                }
                else if (unit.Speed.IsZero() && _random.Next() % 3 == 0 && HasUnload(unit.Kind))
                {
                    unit.ChangeAnimation(SpriteAnimation.Unload);
                }
                else if ((_random.Next() & 3) == 0)
                {
                    TurnStep(unit, (_random.Next() & 1) == 0 ? CreatureTurn : -CreatureTurn, frameTime);
                }
                else
                {
                    UnitKinematics.StopMoving(unit);
                }
            }
        }

        if (unit.Animation is SpriteAnimation.Wound or SpriteAnimation.Salut)
        {
            unit.ChangeAnimation(SpriteAnimation.Stand);
        }
    }

    private static bool HasUnload(ObjectDefinition kind) =>
        kind.Object.FrameCounts[(int)SpriteAnimation.Unload] != 0 || kind.Object.Children[(int)SpriteAnimation.Unload] != 0;

    private static void TurnStep(Unit unit, int step, int elapsed) =>
        unit.SetDirection(unit.Direction.TurnToward(unit.Direction + step, unit.Kind.Object.RotationPeriod, elapsed, out _));

    [EngineRoutine(0x00440110)]
    private bool RunProgram(Unit unit)
    {
        if (unit.OrderStep >= unit.Orders.Length)
            return false;

        var order = unit.Orders[unit.OrderStep];
        var state = unit.Animation;

        if ((int)order.Code > UnitOrder.LastState &&
            (state is > SpriteAnimation.Open and < SpriteAnimation.Death and not SpriteAnimation.StandOpen || state == SpriteAnimation.Go && unit.Speed.IsZero()))
        {
            unit.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (order.Code == ActionCode.StopStack)
            return true;

        unit.OrderStep++;

        if ((int)order.Code <= UnitOrder.LastState)
        {
            unit.ChangeAnimation((SpriteAnimation)order.Code);

            return true;
        }

        switch (order.Code)
        {
            case ActionCode.Attack:
                Attack(unit, order.Target);
                break;
            case ActionCode.Move when order.Point is not null:
                MoveTo(unit, order.Point.Value);
                break;
            case ActionCode.Pause:
                Pause(unit, order.Var1 + _random.Next() % (order.Var2 + 1));
                break;
            case ActionCode.Stop:
                UnitKinematics.StopMoving(unit);

                if (order.Var1 != 0)
                {
                    unit.Speed = 0;
                }
                break;
            case ActionCode.ClearCommand:
                UnitKinematics.SetCommand(unit, UnitCommand.None);
                break;
        }

        return true;
    }

    [EngineRoutine(0x00440830)]
    private static void Pause(Unit unit, int time)
    {
        if (!unit.Speed.IsZero())
        {
            UnitKinematics.StopMoving(unit);
        }

        unit.Timer = time;
        UnitKinematics.SetCommand(unit, time != 0 ? UnitCommand.Pause : UnitCommand.None);
    }

    [EngineRoutine(0x00445fb0)]
    public static void Attack(Unit unit, Unit? target)
    {
        var kind = unit.Kind;
        bool accepts = target is null || kind.IsArmed && kind.FightChild != 0 && (kind.Targets & target.Kind.Object.SpriteType) != 0;

        if (accepts || kind.Object.SpriteClass == SpriteClass.Cannon)
        {
            UnitKinematics.SetCommand(unit, UnitCommand.Attack, target);
        }
    }

    [EngineRoutine(0x00447500)]
    private void Act(Unit unit, Unit player, int elapsed)
    {
        if (unit.Animation >= SpriteAnimation.Death)
            return;

        if (unit.Speed.IsZero())
        {
            unit.ChangeAnimation(SpriteAnimation.Stand);
        }
        else if (unit.Animation != SpriteAnimation.Go)
        {
            unit.ChangeAnimation(SpriteAnimation.Go);
        }

        var kind = unit.Kind;
        var link = unit.ArmedLink;
        var result = AttackResult.Pursue;

        if (link is not null && unit.Command is UnitCommand.Attack or UnitCommand.OneAttack && unit.Goal is not null && link.Goal is null)
        {
            UnitKinematics.SetCommand(link, unit.Command, unit.Goal);
        }

        if (link is not null || unit.TurnTicks == 0)
        {
            result = unit.TryAttack(Math.Max(elapsed, kind.Layout.GetFrameDuration(unit.Animation)));

            switch (result)
            {
                case AttackResult.NoAmmo or AttackResult.GiveUp:
                    UnitKinematics.SetCommand(unit, UnitCommand.None);
                    break;
                case AttackResult.Approach when kind.Speed.IsZero():
                    if (link is not null && link.HasGoal || unit.HasGoal)
                    {
                        UnitKinematics.SetCommand(unit, UnitCommand.None);
                    }
                    break;
                case AttackResult.Approach when unit.Speed.IsZero():
                    _motion.StartMove(unit);
                    break;
                case AttackResult.Attacked when !unit.Speed.IsZero() && (link is null || unit.Goal == link.Goal):
                    UnitKinematics.StopMoving(unit);
                    break;
            }

            if (result == AttackResult.Approach && link is not null && unit.Command is UnitCommand.Attack or UnitCommand.OneAttack &&
                unit.IsAggressive && (link.Timer != 0 || _random.Next() % LinkScanOdds == 0))
            {
                var target = FindTarget(unit, player);

                if (target is not null)
                {
                    UnitKinematics.SetCommand(link, UnitCommand.OneAttack, target);
                }
            }
        }

        if (result == AttackResult.Pursue && unit.IsActive && unit.Speed.IsZero())
        {
            _motion.StartMove(unit);
        }

        if (result == AttackResult.NoOrder && unit is { IsAggressive: true, IsActive: true } ||
            result is AttackResult.Pursue or AttackResult.NoTarget && unit is { IsAggressive: true, Command: UnitCommand.None or UnitCommand.Move or UnitCommand.OneAttack } &&
            (unit.LastFrame > unit.FirstFrame || unit.Timer != 0 || _random.Next(IdleScanOdds) == 0))
        {
            Engage(unit, player);
        }

        if (unit is { Command: UnitCommand.None, HasGoal: false, Timer: 0 })
        {
            UnitKinematics.StopMoving(unit);
        }
    }

    [EngineRoutine(0x00447500)]
    public void Wander(Unit unit)
    {
        if (unit.Command != UnitCommand.None || unit.Kind.Speed.IsZero())
            return;

        var vector = new Angle((byte)_random.Next()).Vector;
        MoveTo(unit, unit.Position.AsVector2() + vector * WanderReach);
    }

    [EngineRoutine(0x004447b0)]
    private void MoveTo(Unit unit, Vector2 point)
    {
        if (!UnitKinematics.SetCommand(unit, UnitCommand.Move, point: new Vector3(point, unit.Position.Z)) && !_motion.StartMove(unit))
        {
            UnitKinematics.SetCommand(unit, UnitCommand.None);
        }
    }

    private void Engage(Unit unit, Unit player)
    {
        var target = FindTarget(unit, player);

        if (target is not null)
        {
            UnitKinematics.SetCommand(unit, UnitCommand.OneAttack, target);
        }
    }

    [EngineRoutine(0x00444a80)]
    private Unit? FindTarget(Unit unit, Unit player)
    {
        var kind = unit.Kind;

        if (kind.DetectRange.IsZero() || kind.Targets == 0)
            return null;

        Unit? best = null;
        float bestDistance = kind.DetectRange + 1;

        if (unit.Army == Army.Monsters)
            return TryTake(unit, player, ref best, ref bestDistance) ? player : best;

        if (TryTake(unit, player, ref best, ref bestDistance))
            return player;

        foreach (var candidate in _candidates)
        {
            if (candidate != unit && TryTake(unit, candidate, ref best, ref bestDistance))
                return candidate;
        }

        return best;
    }

    private bool TryTake(Unit unit, Unit candidate, ref Unit? best, ref float bestDistance)
    {
        var kind = unit.Kind;

        if ((kind.Targets & candidate.Kind.Object.SpriteType) == 0 || candidate.Kind.MaxHp == 0 || !IsEnemy(unit, candidate))
            return false;

        if (kind.NeedsForwardTarget &&
            unit.Direction.MeasureTurnTo(Angle.FromOffset((candidate.Position - unit.Position).AsVector2())) > ForwardCone)
            return false;

        float distance = unit.Position.AsVector2().MeasureDistance(candidate.Position.AsVector2());

        if (distance > kind.DetectRange || distance < kind.MinRange)
            return false;

        bool accepts;

        if (kind.Object.SpriteType.HasFlag(SpriteType.Avia) && unit.Goal is null)
        {
            accepts = distance < bestDistance;
        }
        else if (bestDistance <= kind.BattleRange)
        {
            accepts = distance <= kind.BattleRange && IsBetter(unit, distance, bestDistance, candidate, best);
        }
        else
        {
            accepts = distance <= kind.BattleRange || IsBetter(unit, distance, bestDistance, candidate, best);
        }

        if (!accepts || unit.Army == Army.Player && candidate.Kind.Object.SpriteClass == SpriteClass.Engine)
            return false;

        best = candidate;
        bestDistance = distance;

        return kind.WeaponFlags.HasFlag(WeaponFlags.RandomTarget) && _random.Next() % RepeatTargetOdds == 0;
    }

    [EngineRoutine(0x00445c80)]
    private static bool IsBetter(Unit unit, float distance, float bestDistance, Unit candidate, Unit? best)
    {
        if (best is null)
            return true;

        if (unit.Kind.WeaponFlags.HasFlag(WeaponFlags.PrefersNearest))
            return distance < bestDistance;

        if (unit.Goal == best)
            return false;

        if (unit.Goal == candidate)
            return true;

        if (candidate.Army == Army.Neutral && best.Army != Army.Neutral)
            return false;

        bool bestFlies = best.Kind.Object.SpriteType.HasFlag(SpriteType.Avia);
        bool candidateFlies = candidate.Kind.Object.SpriteType.HasFlag(SpriteType.Avia);

        if (bestFlies != candidateFlies)
            return candidateFlies;

        float range = (unit.ArmedLink?.Kind ?? unit.Kind).BattleRange;

        if (range < bestDistance && distance <= range)
            return true;

        bool candidateArmed = candidate.Kind.Object.SpriteType.HasFlag(SpriteType.Unit) && candidate.Rounds != 0;
        bool bestArmed = best.Kind.Object.SpriteType.HasFlag(SpriteType.Unit) && best.Rounds != 0;

        if (candidateArmed != bestArmed)
            return candidateArmed;

        int candidateRank = (candidate.ArmedLink?.Kind ?? candidate.Kind).Rank;
        int bestRank = (best.ArmedLink?.Kind ?? best.Kind).Rank;

        return bestRank < candidateRank || bestRank <= candidateRank && distance < bestDistance;
    }

    private static bool IsEnemy(Unit unit, Unit target)
    {
        bool ownArmy = unit.Kind.WeaponFlags.HasFlag(WeaponFlags.TargetsOwnArmy);

        return (target.Army != unit.Army || ownArmy) && !target.Kind.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) &&
            (target.Army != Army.Neutral || ownArmy) &&
            target.Kind.Id != KnownObject.PistolMuzzleFlash;
    }

    [EngineRoutine(0x0043fa00)]
    private void RepelFromMapEdge(Unit unit, int elapsed)
    {
        var position = unit.Position;
        var size = _movement.MapSize;
        int goal = position.X < 0 ? Angle.QuarterTurn : position.Y < 0 ? Angle.HalfTurn : position.X >= size.Width ? Angle.HalfTurn + Angle.QuarterTurn : position.Y >= size.Height ? 0 : -1;

        if (goal >= 0)
        {
            unit.SetDirection(unit.Direction.TurnToward(new Angle((byte)goal), unit.Kind.Object.RotationPeriod, elapsed, out _));
        }
    }
}
