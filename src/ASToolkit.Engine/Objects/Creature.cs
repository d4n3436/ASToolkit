using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a creature sprite.
/// </summary>
public class Creature : Unit
{
    private const int BlockedTurnTicks = 10;
    private const float CreatureAlertReach = 150;
    private const int CreaturePeriodMask = 0x7ff;
    private const int CreatureTurn = 0x20;

    /// <summary>
    /// Initializes a new instance of the <see cref="Creature"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044d3e0)]
    public Creature(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        IsSeekingRegion = true;
        VerticalSpeed = Vid.ZSpeed;
        StartMove();
        Region = FindRegion(Position.AsVector2());
    }

    internal bool IsSeekingRegion { get; set; }

    internal Region? Region { get; set; }

    [EngineRoutine(0x0044d880)]
    internal override int Action(ActionCode act, int var1, int var2, int var3, Sprite? target)
    {
        if (act == ActionCode.Damage && var1 > 0)
        {
            AlertCreatures();
        }

        if (act != ActionCode.NextCommand)
            return base.Action(act, var1, var2, var3, target);

        if (Animation >= SpriteAnimation.Death)
            return 0;

        if (Animation is SpriteAnimation.Fight or SpriteAnimation.Wound)
        {
            ChangeAnimation(SpriteAnimation.Stand);
        }

        if (this is { IsSeekingRegion: true, Region: null })
        {
            IsSeekingRegion = false;
            Region = FindRegion(Position.AsVector2());
        }

        if (this is { HasGoal: true, Command: Command.Move })
        {
            if (!Speed.IsExactlyZero())
            {
                if (Animation != SpriteAnimation.Go)
                {
                    ChangeAnimation(SpriteAnimation.Go);
                }

                return 0;
            }

            ChangeAnimation(SpriteAnimation.Stand);

            return 0;
        }

        bool newPeriod = (uint)LastTickTime < (uint)(Map.Time - (Map.Time & CreaturePeriodMask));

        if (Command is Command.Attack or Command.OneAttack || newPeriod)
        {
            TactCreatureAttack();
        }

        int frameTime = (byte)Vid.GetVisualFrameDuration(Animation);

        if (Command is not (Command.Move or Command.Attack or Command.OneAttack))
        {
            WanderCreature(frameTime, newPeriod);
        }

        if (Animation is SpriteAnimation.Wound or SpriteAnimation.Salut)
        {
            ChangeAnimation(SpriteAnimation.Stand);
        }

        return 0;
    }

    [EngineRoutine(0x0044d480)]
    internal override void MoveTact()
    {
        if (IsSeekingRegion && Region is null)
        {
            IsSeekingRegion = false;
            Region = FindRegion(Position.AsVector2());
        }

        int elapsed = Map.FrameTime;
        var next = CalculateNewCoordinates(elapsed);

        if (TurnTicks != 0)
        {
            if ((TurnTicks & 1) != 0)
            {
                bool left = Animation == SpriteAnimation.LRotate || Animation != SpriteAnimation.RRotate && Map.Random.Next() % 2 == 0;
                RotateTact(Direction + (left ? -Angle.QuarterTurn : Angle.QuarterTurn), elapsed);
            }

            TurnTicks += TurnTicks < 0 ? 1 : -1;
        }
        else if (HasGoal && !Speed.IsExactlyZero())
        {
            var goal = DirectionTo(Goal!);

            if (Speed < 0)
            {
                goal = goal.Opposite;
            }

            RotateTact(RotateForGlide(goal), elapsed);

            if (HasReachedX && HasReachedY)
            {
                Stop();
            }
        }

        if (Vid.Properties.HasFlag(ObjectProperties.ZeroZ))
        {
            next.Z = Map.GetGroundZForMoveTo(Vid, next.AsVector2());
        }

        if (CanPlace(next) is null && StaysInRegion(next.AsVector2()))
        {
            MoveTactMapLimit(next.AsVector2(), elapsed);
            ChangeCoor(next);

            return;
        }

        Speed = 0;

        if (TurnTicks == 0)
        {
            TurnTicks = BlockedTurnTicks;
        }
    }

    [EngineRoutine(0x0044d460)]
    internal override void DeletePointerToSprite(Sprite removed)
    {
        if (Region == removed)
        {
            Region = null;
        }

        base.DeletePointerToSprite(removed);
    }

    [EngineRoutine(0x0044d7b0)]
    internal Region? FindRegion(Vector2 point)
    {
        var list = Map.GetLayer((int)ObjectLayer.Region);

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is not Region sprite || !sprite.Vid.SpriteType.HasFlag(OldSpriteType.Region) || sprite.Vid.Class != SpriteClass.Region)
                continue;

            float halfWidth = sprite.Size.X * 0.5f;

            if (sprite.Position.X - halfWidth > point.X || point.X > halfWidth + sprite.Position.X)
                continue;

            float halfHeight = sprite.Size.Y * 0.5f;

            if (sprite.Position.Y - halfHeight <= point.Y && point.Y <= halfHeight + sprite.Position.Y)
                return sprite;
        }

        return null;
    }

    private bool StaysInRegion(Vector2 point)
    {
        if (Region is null)
            return true;

        var half = Region.Size * 0.5f;

        if (Region.Position.X - half.X <= point.X && point.X <= half.X + Region.Position.X && Region.Position.Y - half.Y <= point.Y && point.Y <= half.Y + Region.Position.Y)
            return true;

        var other = FindRegion(point);

        if (other is null || Region.Vid != other.Vid)
            return false;

        Region = other;

        return true;
    }

    private void AlertCreatures()
    {
        var center = Position.AsVector2();
        var reach = new Vector2(CreatureAlertReach);
        for (var other = Map.Collector.First(center - reach, center + reach); other is not null; other = Map.Collector.NextHashInBox())
        {
            if (other.Vid.Class == SpriteClass.Creature && center.MeasureOctagonalDistance(other.Position.AsVector2()) < CreatureAlertReach)
            {
                other.StartMove();
            }
        }
    }

    private void TactCreatureAttack()
    {
        var result = AttackTact(Math.Max(Map.FrameTime, Vid.GetVisualFrameDuration(Animation)));
        AttackResult = result;

        if (result == AttackResult.Approach && Speed.IsExactlyZero() && (ArmedLink is { HasGoal: true } || HasGoal))
        {
            SetCommand(Command.None);
        }

        if (result is AttackResult.Pursue or AttackResult.NoTarget && IsAggressive && Command is Command.None or Command.Move)
        {
            AttackSeenEnemy(this);
        }
    }

    private void WanderCreature(int frameTime, bool newPeriod)
    {
        if (Animation == SpriteAnimation.LRotate && Map.Random.Next() % 3 != 0)
        {
            RotateTact(Direction - CreatureTurn, frameTime);
        }

        if (Animation == SpriteAnimation.RRotate && Map.Random.Next() % 3 != 0)
        {
            RotateTact(Direction + CreatureTurn, frameTime);
        }

        if (!newPeriod)
            return;

        if ((Map.Random.Next() & 3) == 0)
        {
            StartMove();
        }
        else if (Speed.IsExactlyZero() && Map.Random.Next() % 3 == 0 && (Vid.FrameCounts[(int)SpriteAnimation.Unload] != 0 || Vid.Children[(int)SpriteAnimation.Unload] != 0 || Vid.Sounds[(int)SpriteAnimation.Unload] != 0))
        {
            ChangeAnimation(SpriteAnimation.Unload);
        }
        else if ((Map.Random.Next() & 3) == 0)
        {
            RotateTact(Direction + ((Map.Random.Next() & 1) == 0 ? CreatureTurn : -CreatureTurn), frameTime);
        }
        else
        {
            Stop();
        }
    }
}
