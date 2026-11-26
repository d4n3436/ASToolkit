using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a flying unit sprite.
/// </summary>
public class Plane : Unit
{
    private const int AvoidTurn = Angle.FullTurn / 16;
    private const int FreeTurn = Angle.FullTurn / 8;
    private const float GoalHeight = 80;
    private const float HeightBand = 10;
    private const float ProbeDistance = 128;
    private const int ScanOdds = 21;
    private const int TurnEndOdds = 3;
    private const int TurnStartOdds = 11;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plane"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044ce60)]
    public Plane(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        StartMove();
        VerticalSpeed = Vid.ZSpeed;
    }

    [EngineRoutine(0x0044ceb0)]
    internal override int Action(ActionCode act, int var1, int var2, int var3)
    {
        switch (act)
        {
            case ActionCode.Move:
                Move(Map.ConstructGoal(new Vector3(var1, var2, Map.GetGroundZForMoveTo(Vid, new Vector2(var1, var2)) + GoalHeight)));

                return 0;
            case ActionCode.Damage:
                return SpriteAction(act, var1, var2, var3);
            case ActionCode.NextCommand:
                PlaneNextCommand(var1, var2);

                return 0;
        }

        return base.Action(act, var1, var2, var3);
    }

    [EngineRoutine(0x0044cfb0)]
    internal virtual void FreeFlight()
    {
        var state = Animation;
        if (state == SpriteAnimation.LRotate)
        {
            if (Map.Random.Next() % TurnEndOdds == 0)
            {
                ChangeAnimation(SpriteAnimation.Go);
            }
            else
            {
                ChangeDirection(Direction - FreeTurn);
            }
        }
        else if (state == SpriteAnimation.RRotate)
        {
            if (Map.Random.Next() % TurnEndOdds == 0)
            {
                ChangeAnimation(SpriteAnimation.Go);
            }
            else
            {
                ChangeDirection(Direction + FreeTurn);
            }
        }
        else if (state == SpriteAnimation.Go)
        {
            if (Map.Random.Next() % TurnStartOdds == 0)
            {
                ChangeAnimation(Map.Random.Next() % 2 == 0 ? SpriteAnimation.RRotate : SpriteAnimation.LRotate);
            }
        }
        else
        {
            ChangeAnimation(SpriteAnimation.Go);
        }

        if (Map.Random.Next() % ScanOdds == 0 && IsAggressive)
        {
            var enemy = SeekEnemy();

            if (enemy is not null)
            {
                SetCommand(Command.OneAttack, enemy);
            }
        }
    }

    [EngineRoutine(0x0044d0a0)]
    internal virtual void CheckFlightProperties()
    {
    }

    [EngineRoutine(0x0044d0b0)]
    internal virtual void FlightToTargetAdditionalActions()
    {
        var result = AttackTact((int)Math.Max((uint)Map.FrameTime, (uint)Vid.GetVisualFrameDuration(Animation)));
        AttackResult = result;

        if (IsAggressive && (result != AttackResult.NoOrder || Map.Random.Next() % ScanOdds == 0))
        {
            var enemy = SeekEnemy();

            if (enemy is not null)
            {
                SetCommand(Command.OneAttack, enemy);
            }
        }
    }

    [EngineRoutine(0x0044d120)]
    internal virtual void FlightToTarget()
    {
        var goalDirection = DirectionTo(Goal!);
        int elapsed = (int)Math.Max((uint)Map.FrameTime, (uint)Vid.GetVisualFrameDuration(Animation));

        if (RotateTact(goalDirection, elapsed) == default)
        {
            ChangeAnimation(SpriteAnimation.Go);
        }

        FlightToTargetAdditionalActions();
    }

    [EngineRoutine(0x0044d1c0)]
    internal virtual void ZSpeedInitialization()
    {
        var vid = Vid;
        var position = Position;
        float ground = Map.GetGroundZForMoveTo(vid, position.AsVector2());

        if (ground + vid.TopZ - HeightBand > position.Z)
        {
            VerticalSpeed = vid.ZSpeed;

            return;
        }

        ground = Map.GetGroundZForMoveTo(vid, position.AsVector2());
        VerticalSpeed = ground + vid.TopZ + HeightBand < position.Z ? -vid.ZSpeed : 0;
    }

    [EngineRoutine(0x0044d250)]
    internal virtual bool WayBlocked() => FindBlockerAt(Position.AsVector2() + Direction.Vector * ProbeDistance) is not null;

    [EngineRoutine(0x0044d2b0)]
    internal virtual void FlightIfWayBlocked()
    {
        if (FindBlockerAt(Position.AsVector2() + (Direction + AvoidTurn).Vector * ProbeDistance) is not null)
        {
            ChangeAnimation(SpriteAnimation.RRotate);
            ChangeDirection(Direction + AvoidTurn);
        }
        else
        {
            ChangeAnimation(SpriteAnimation.LRotate);
            ChangeDirection(Direction - AvoidTurn);
        }
    }

    [EngineRoutine(0x0044d370)]
    internal virtual void PlaneNextCommand(int _, int _1)
    {
        var state = Animation;
        if (state is >= SpriteAnimation.Death or SpriteAnimation.Unload)
            return;

        if (state > SpriteAnimation.Open && state != SpriteAnimation.StandOpen)
        {
            ChangeAnimation(SpriteAnimation.Stand);
        }

        ZSpeedInitialization();
        CheckFlightProperties();

        if (Owner is not null)
            return;

        IsMoving = true;

        if (Goal is not null)
        {
            FlightToTargetAdditionalActions();
        }
        else
        {
            FreeFlight();
        }
    }
}
