using ASToolkit.Core.Formats;
using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerMovement
{
    public static readonly Vector2 MoveTargetHalfSize = new(12, 8);
    public const float TargetHeight = 20;

    private readonly SpriteCollision _movement;

    public PlayerMovement(SpriteCollision movement)
    {
        _movement = movement;
    }

    public static bool IsTouching(Sprite player, Vector2 target) =>
        Math.Abs(player.Position.X - target.X) < player.Definition.Half.X + MoveTargetHalfSize.X && Math.Abs(player.Position.Y - target.Y) < player.Definition.Half.Y + MoveTargetHalfSize.Y;

    [EngineRoutine(0x00454670)]
    public void Tick(Sprite player, float maxSpeed, int elapsed)
    {
        Vector2? target = player is { Command: Command.Move, HasGoal: true } ? player.GoalPosition.AsVector2() : null;
        player.Speed = SpriteKinematics.Accelerate(player.Definition, player.Speed, maxSpeed, player.IsMoving, elapsed);
        var start = player.Position;
        var next = player.Speed.IsZero() ? start : SpriteKinematics.Displace(start, player.Direction, player.Speed, elapsed);

        if (target is not null && player.IsMoving)
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
            direction = _movement.RotateForGlide(direction, player);
            player.RotateTact(direction, elapsed);

            if (player is { HasReachedX: true, HasReachedY: true } || IsTouching(player, target.Value))
            {
                SpriteKinematics.Stop(player);
            }
        }
    }
}
