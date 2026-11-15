using ASToolkit.Core.Formats;
using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerMovement
{
    // Half size of the engine's default move-target object
    public static readonly Vector2 TargetHalf = new(12, 8);
    public const float TargetHeight = 20;

    private readonly UnitCollision _movement;
    private Vector2? _target;
    private Vector2? _completed;

    public PlayerMovement(UnitCollision movement)
    {
        _movement = movement;
    }

    public void Reset()
    {
        _target = null;
        _completed = null;
    }

    public bool HasReached(Unit player, Vector2 target) => _completed == target ||
        Math.Abs(player.Position.X - target.X) < player.Kind.Half.X + TargetHalf.X && Math.Abs(player.Position.Y - target.Y) < player.Kind.Half.Y + TargetHalf.Y;

    [EngineRoutine(0x00454670)]
    public void Tick(Unit player, Vector2? target, float maxSpeed, int elapsed)
    {
        ref readonly var obj = ref player.Kind.Object;

        if (_target != target)
        {
            _target = target;
            _completed = null;
            player.ClearMove();

            if (target is not null)
            {
                player.StartMoving(maxSpeed);
            }
        }

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

        if (target is not null && player.Moving)
        {
            var direction = Angle.FromOffset(target.Value - player.Position.AsVector2());
            direction = _movement.AvoidObstacles(direction, player);
            player.SetDirection(player.Direction.TurnToward(direction, obj.RotationPeriod, elapsed, out _));

            if (player is { ReachedX: true, ReachedY: true } || HasReached(player, target.Value))
            {
                player.ClearMove();
                _completed = target;

                if (obj.Deceleration.IsNear(ObjectDefinition.Unlimited))
                {
                    player.Speed = 0;
                }
            }
        }
    }
}
