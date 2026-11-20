using ASToolkit.Core.Formats;
using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class PlayerMovement
{
    private readonly SpriteCollision _movement;
    private readonly SpriteKinematics _motion;

    public PlayerMovement(SpriteCollision movement, SpriteKinematics motion)
    {
        _movement = movement;
        _motion = motion;
    }

    public static bool IsTouching(Sprite man, Vector2 target, Vector2 targetHalf)
    {
        var reach = man.Definition.Half + targetHalf;

        return Math.Abs(man.Position.X - target.X) < reach.X && Math.Abs(man.Position.Y - target.Y) < reach.Y;
    }

    [EngineRoutine(0x00454670)]
    public void MoveTact(Sprite man, int elapsed)
    {
        var next = _motion.CalculateNewCoordinates(man, elapsed);

        if (man.Position.AsVector2() != next.AsVector2() && _movement.IsInsideMap(next.AsVector2()))
        {
            _movement.Move(man, next);
        }

        var goal = man.Goal;

        if (goal is null || man.Command != Command.Move)
            return;

        var direction = man.MeasureDirectionTo(goal) + (man.Speed < 0 ? Angle.HalfTurn : 0);
        man.RotateTact(_movement.RotateForGlide(direction, man), elapsed);

        if (man is { HasReachedX: true, HasReachedY: true } || IsTouching(man, goal.Position.AsVector2(), goal.Definition.Half))
        {
            SpriteKinematics.Stop(man);
        }
    }
}
