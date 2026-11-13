using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class MonsterBrain
{
    private const int IdleScanOdds = 11;
    private const int RepeatTargetOdds = 3;
    private const int ForwardCone = 0x1f;
    private const float WanderReach = 64;

    private readonly UnitKinematics _motion;
    private readonly UnitCollision _movement;
    private readonly Random _random;

    public MonsterBrain(UnitKinematics motion, UnitCollision movement, Random random)
    {
        _motion = motion;
        _movement = movement;
        _random = random;
    }

    [EngineRoutine(0x00447260)]
    public void Move(Unit unit, int elapsed, int framesPerSecond)
    {
        ref readonly var obj = ref unit.Kind.Object;

        if (unit is { Moving: false, Speed: 0, TurnTicks: 0, VerticalSpeed: 0 } && (obj.Flags & (ObjectFlags.Flying | ObjectFlags.Heavy)) == 0)
            return;

        var next = _motion.Integrate(unit, elapsed);

        if (unit.TurnTicks == 0)
        {
            if (unit.HasGoal && !unit.Speed.IsZero())
            {
                var goal = Facing.FromOffset((unit.GoalPosition - unit.Position).AsVector2());

                if (unit.Speed < 0)
                {
                    goal = goal.Opposite;
                }
                goal = _movement.AvoidObstacles(goal, unit);
                unit.SetFacing(unit.TurnWhileMoving(goal, elapsed));

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
            var goal = unit.Facing + (unit.TurnTicks > 0 ? Facing.QuarterTurn : -Facing.QuarterTurn);
            unit.SetFacing(unit.TurnWhileMoving(goal, elapsed));
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

    public void Decide(Unit unit, Unit player, int elapsed)
    {
        if (unit.Command != UnitCommand.None || !RunProgram(unit))
        {
            Act(unit, player, elapsed);
        }
    }

    [EngineRoutine(0x00440110)]
    private bool RunProgram(Unit unit)
    {
        if (unit.OrderStep >= unit.Orders.Length)
            return false;

        var state = unit.Animation;

        if (state is > SpriteAnimation.Open and < SpriteAnimation.Death and not SpriteAnimation.StandOpen || state == SpriteAnimation.Go && unit.Speed.IsZero())
        {
            unit.SetState(SpriteAnimation.Stand);
        }

        var order = unit.Orders[unit.OrderStep++];

        if (order.Target is not null)
        {
            Attack(unit, order.Target);
        }
        else if (order.Point is not null)
        {
            MoveTo(unit, order.Point.Value);
        }

        return true;
    }

    [EngineRoutine(0x00445fb0)]
    private static void Attack(Unit unit, Unit target)
    {
        var kind = unit.Kind;
        bool accepts = kind.IsArmed && kind.FightChild != 0 && (kind.Targets & target.Kind.Object.SpriteType) != 0;

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
            unit.SetState(SpriteAnimation.Stand);
        }
        else if (unit.Animation != SpriteAnimation.Go)
        {
            unit.SetState(SpriteAnimation.Go);
        }

        var kind = unit.Kind;
        var result = AttackResult.Pursue;

        if (unit.TurnTicks == 0)
        {
            result = unit.TryAttack(Math.Max(elapsed, kind.Layout.GetFrameDuration(unit.Animation)));

            switch (result)
            {
                case AttackResult.NoAmmo or AttackResult.GiveUp:
                    UnitKinematics.SetCommand(unit, UnitCommand.None);
                    break;
                case AttackResult.Approach when kind.Speed.IsZero():
                    if (unit.HasGoal)
                    {
                        UnitKinematics.SetCommand(unit, UnitCommand.None);
                    }
                    break;
                case AttackResult.Approach when unit.Speed.IsZero():
                    _motion.StartTowardGoal(unit);
                    break;
                case AttackResult.Attacked when !unit.Speed.IsZero():
                    UnitKinematics.StopMoving(unit);
                    break;
            }
        }

        if (result == AttackResult.Pursue && kind.PursuesTargets && unit.Speed.IsZero())
        {
            _motion.StartTowardGoal(unit);
        }

        if (result == AttackResult.NoOrder && kind is { AcquiresTargets: true, PursuesTargets: true } ||
            result is AttackResult.Pursue or AttackResult.NoTarget && kind.AcquiresTargets &&
            unit.Command is UnitCommand.None or UnitCommand.Move or UnitCommand.Engage &&
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

        var direction = new Facing((byte)_random.Next()).Direction;
        MoveTo(unit, unit.Position.AsVector2() + direction * WanderReach);
    }

    [EngineRoutine(0x004447b0)]
    private void MoveTo(Unit unit, Vector2 point)
    {
        if (!UnitKinematics.SetCommand(unit, UnitCommand.Move, point: new Vector3(point, unit.Position.Z)) && !_motion.StartTowardGoal(unit))
        {
            UnitKinematics.SetCommand(unit, UnitCommand.None);
        }
    }

    private void Engage(Unit unit, Unit player)
    {
        var target = FindTarget(unit, player);

        if (target is not null)
        {
            UnitKinematics.SetCommand(unit, UnitCommand.Engage, target);
        }
    }

    [EngineRoutine(0x00444a80)]
    private Unit? FindTarget(Unit unit, Unit player)
    {
        var kind = unit.Kind;

        if (kind.DetectRange.IsZero() || kind.Targets == 0 || (kind.Targets & player.Kind.Object.SpriteType) == 0 || player.Kind.MaxHp == 0 ||
            player.Army == unit.Army)
            return null;

        if (kind.NeedsForwardTarget &&
            unit.Facing.MeasureTurnTo(Facing.FromOffset((player.Position - unit.Position).AsVector2())) > ForwardCone)
            return null;

        float distance = unit.Position.AsVector2().MeasureDistance(player.Position.AsVector2());

        if (distance > kind.DetectRange || distance < kind.MinRange)
            return null;

        if (kind.WeaponFlags.HasFlag(WeaponFlags.RandomTarget))
        {
            _random.Next(RepeatTargetOdds);
        }

        return player;
    }

    [EngineRoutine(0x0043fa00)]
    private void RepelFromMapEdge(Unit unit, int elapsed)
    {
        var position = unit.Position;
        var size = _movement.MapSize;
        int goal = position.X < 0 ? Facing.QuarterTurn : position.Y < 0 ? Facing.HalfTurn : position.X >= size.Width ? Facing.HalfTurn + Facing.QuarterTurn : position.Y >= size.Height ? 0 : -1;

        if (goal >= 0)
        {
            unit.SetFacing(unit.Facing.TurnToward(new Facing((byte)goal), unit.Kind.Object.TurnPeriod, elapsed, out _));
        }
    }
}
