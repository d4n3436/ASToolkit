using ASToolkit.Core.Formats;
using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerMovement
{
    private readonly SpriteCollision _movement;

    public PlayerMovement(SpriteCollision movement)
    {
        _movement = movement;
    }

    public static bool IsTouching(Sprite player, Vector2 target, Vector2 targetHalf)
    {
        var reach = player.Definition.Half + targetHalf;

        return Math.Abs(player.Position.X - target.X) < reach.X && Math.Abs(player.Position.Y - target.Y) < reach.Y;
    }

    [EngineRoutine(0x00454670)]
    public void Tick(Sprite player, float maxSpeed, int elapsed)
    {
        var goal = player.Command == Command.Move ? player.Goal : null;
        player.Speed = SpriteKinematics.Accelerate(player.Definition, player.Speed, maxSpeed, player.IsMoving, elapsed);
        var start = player.Position;
        var next = player.Speed.IsZero() ? start : SpriteKinematics.Displace(start, player.Direction, player.Speed, elapsed);

        if (goal is not null && player.IsMoving)
        {
            player.UpdateReach(start, next, goal.Position.AsVector2());
        }

        if (next != start && _movement.IsInsideMap(next.AsVector2()))
        {
            _movement.Move(player, next);
        }

        if (goal is not null)
        {
            var target = goal.Position.AsVector2();
            var direction = Angle.FromOffset(target - player.Position.AsVector2()) + (player.Speed < 0 ? Angle.HalfTurn : 0);
            direction = _movement.RotateForGlide(direction, player);
            player.RotateTact(direction, elapsed);

            if (player is { HasReachedX: true, HasReachedY: true } || IsTouching(player, target, goal.Definition.Half))
            {
                SpriteKinematics.Stop(player);
            }
        }
    }
}
