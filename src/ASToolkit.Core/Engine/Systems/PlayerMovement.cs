using ASToolkit.Core.Formats;
using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerMovement
{
    // Half size of the engine's default move-target object
    public static readonly Vector2 TargetHalf = new(12, 8);
    public const float TargetHeight = 20;

    private readonly UnitCollision _movement;

    public PlayerMovement(UnitCollision movement)
    {
        _movement = movement;
    }

    public static bool IsTouching(Unit player, Vector2 target) =>
        Math.Abs(player.Position.X - target.X) < player.Kind.Half.X + TargetHalf.X && Math.Abs(player.Position.Y - target.Y) < player.Kind.Half.Y + TargetHalf.Y;

    [EngineRoutine(0x00454670)]
    public void Tick(Unit player, float maxSpeed, int elapsed)
    {
        Vector2? target = player.Command == UnitCommand.Move && player.HasGoal ? player.GoalPosition.AsVector2() : null;
        player.Speed = UnitKinematics.Accelerate(player.Kind, player.Speed, maxSpeed, player.Moving, elapsed);
        var start = player.Position;
        var next = player.Speed.IsZero() ? start : UnitKinematics.Displace(start, player.Direction, player.Speed, elapsed);

        if (target is not null && player.Moving)
        {
            player.UpdateReach(start, next, target.Value);
        }

        if (next != start && _movement.IsInsideMap(next.AsVector2()))
        {
            _movement.Move(player, next);
        }

        if (target is not null)
        {
            var direction = Angle.FromOffset(target.Value - player.Position.AsVector2()) + (player.Speed < 0 ? Angle.HalfTurn : 0);
            direction = _movement.AvoidObstacles(direction, player);
            player.SetDirection(player.Direction.TurnToward(direction, player.Kind.Object.RotationPeriod, elapsed, out _));

            if (player is { ReachedX: true, ReachedY: true } || IsTouching(player, target.Value))
            {
                UnitKinematics.StopMoving(player);
            }
        }
    }
}
