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
    /// Initializes a new instance of the <see cref="Creature"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Creature(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, definition, position, army, hitPoints, map, direction)
    {
    }

    internal bool IsSeekingRegion { get; set; }

    internal Region? Region { get; set; }

    [EngineRoutine(0x0044d880)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        if (code == ActionCode.Damage && var1 > 0)
        {
            AlertCreatures();
        }

        if (code != ActionCode.NextCommand)
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

        int frameTime = (byte)Definition.GetVisualFrameDuration(Animation);

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
        var next = Map.Motion.CalculateNewCoordinates(this, elapsed);

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

            RotateTact(Map.Collision.RotateForGlide(goal, this), elapsed);

            if (HasReachedX && HasReachedY)
            {
                SpriteKinematics.Stop(this);
            }
        }

        if (Definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ))
        {
            next.Z = Map.Movement.GetSupportHeight(Definition.Object, next.AsVector2());
        }

        if (Map.Projectiles.FindBlocker(this, next) is null && StaysInRegion(next.AsVector2()))
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
            if (list[i] is not Region sprite || !sprite.Definition.Object.SpriteType.ToOldSpriteType().HasFlag(OldSpriteType.Region) || sprite.Definition.Class != SpriteClass.Region)
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

        if (other is null || Region.Definition != other.Definition)
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
            if (other.Definition.Class == SpriteClass.Creature && center.MeasureOctagonalDistance(other.Position.AsVector2()) < CreatureAlertReach)
            {
                Map.Motion.StartMove(other);
            }
        }
    }

    private void TactCreatureAttack()
    {
        var result = AttackTact(Math.Max(Map.FrameTime, Definition.GetVisualFrameDuration(Animation)));
        AttackResult = result;

        if (result == AttackResult.Approach && Speed.IsExactlyZero() && (ArmedLink is { HasGoal: true } || HasGoal))
        {
            SpriteKinematics.SetCommand(this, Command.None);
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
            Map.Motion.StartMove(this);
        }
        else if (Speed.IsExactlyZero() && Map.Random.Next() % 3 == 0 && (Definition.Object.FrameCounts[(int)SpriteAnimation.Unload] != 0 || Definition.Children[(int)SpriteAnimation.Unload] != 0 || Definition.Sounds[(int)SpriteAnimation.Unload] != 0))
        {
            ChangeAnimation(SpriteAnimation.Unload);
        }
        else if ((Map.Random.Next() & 3) == 0)
        {
            RotateTact(Direction + ((Map.Random.Next() & 1) == 0 ? CreatureTurn : -CreatureTurn), frameTime);
        }
        else
        {
            SpriteKinematics.Stop(this);
        }
    }
}
