using System.Numerics;
using ASToolkit.Core.Formats;
using System.Drawing;

namespace ASToolkit.Core.Engine;

internal sealed class SpriteCollision
{
    private const int NorthEastBoundary = Angle.QuarterTurn / 2;
    private const int NorthWestBoundary = Angle.FullTurn - NorthEastBoundary;
    private const int SouthEastBoundary = Angle.HalfTurn - NorthEastBoundary;
    private const int SouthWestBoundary = Angle.HalfTurn + NorthEastBoundary;
    private const int CardinalHalfWidth = 16;
    private const int AvoidanceOffset = 24;
    private const int MaxPushDepth = 4;
    private const int OppositeDirectionTolerance = 16;

    private readonly MovementMap _movement;
    private readonly SpriteCollector _collector;
    
    public SpriteCollision(MovementMap movement, SpriteCollector collector)
    {
        _movement = movement;
        _collector = collector;
    }

    public SizeF MapSize => _movement.MapSize;

    public bool IsInsideMap(Vector2 point) => point is { X: >= 0, Y: >= 0 } && point.X < _movement.MapSize.Width && point.Y < _movement.MapSize.Height;

    public bool IsBlocked(Sprite sprite, Vector3 position) => FindCollision(sprite, position, out _);

    [EngineRoutine(0x00443c40)]
    public bool Move(Sprite sprite, Vector3 position, int depth = 0, bool commit = true)
    {
        var start = sprite.Position;
        bool blocked = FindCollision(sprite, position, out var other);

        if (blocked)
        {
            if (other is not null && other.Definition.CanBePushed && depth < MaxPushDepth)
            {
                var pushed = other.Position + position - start;
                bool opposite = sprite.Definition.CanBePushed && other.Direction.MeasureTurnTo(sprite.Direction.Opposite) < OppositeDirectionTolerance;

                Move(other, pushed with { Z = other.Position.Z }, depth + 1, !opposite || sprite.Direction.Value < Angle.HalfTurn);
            }

            var alongY = position with { X = start.X };

            if (!FindCollision(sprite, alongY, out _))
            {
                position = alongY;
            }
            else
            {
                var alongX = position with { Y = start.Y };

                if (!FindCollision(sprite, alongX, out _))
                {
                    position = alongX;
                }
                else if (other is null || !IsInsideIgnoringLayers(sprite, start, other))
                {
                    return false;
                }
            }
        }

        if (!commit)
            return true;

        sprite.ChangeCoor(sprite.Definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ)
            ? position with { Z = _movement.GetSupportHeight(sprite.Definition.Object, position.AsVector2()) } : position);

        return true;
    }

    private static bool IsInsideIgnoringLayers(Sprite sprite, Vector3 start, Sprite other)
    {
        var offset = Vector2.Abs(other.Position.AsVector2() - start.AsVector2());
        var reach = other.Definition.Half + sprite.Definition.Half;

        return other.Animation < SpriteAnimation.Death && offset.X < reach.X && offset.Y < reach.Y &&
            start.Z <= other.Definition.Object.Size.Z + other.Position.Z && other.Position.Z <= start.Z + sprite.Definition.Object.Size.Z;
    }

    private bool FindCollision(Sprite sprite, Vector3 position, out Sprite? collision)
    {
        collision = null;

        if (sprite.Definition.MoveMask == 0)
        {
            return false;
        }

        if (!_movement.CanStandOnGround(sprite.Definition.Object, position))
        {
            return true;
        }

        collision = _collector.FindOverlap(sprite, sprite.Definition.GetCollisionVolume(position));

        return collision is not null;
    }


    [EngineRoutine(0x00443ed0)]
    public Angle RotateForGlide(Angle direction, Sprite sprite)
    {
        var center = sprite.Position.AsVector2();
        var half = sprite.Definition.Half;

        if (direction.Value is <= SouthEastBoundary or >= SouthWestBoundary)
        {
            if (direction.Value is > NorthWestBoundary or < NorthEastBoundary)
            {
                if (IsBlockedAt(center - half) && !IsBlockedAt(center + half with { Y = 0 }))
                {
                    return Angle.East - AvoidanceOffset;
                }

                if (IsBlockedAt(center + half with { Y = -half.Y }) && !IsBlockedAt(center - half with { Y = 0 }))
                {
                    return Angle.West + AvoidanceOffset;
                }
            }
            else if (direction.Value <= Angle.West.Value - CardinalHalfWidth || direction.Value >= Angle.West.Value + CardinalHalfWidth)
            {
                if (direction.Value <= Angle.East.Value - CardinalHalfWidth || direction.Value >= Angle.East.Value + CardinalHalfWidth)
                    return direction;
                
                if (IsBlockedAt(center + half) && !IsBlockedAt(center - half with { X = 0 }))
                {
                    return Angle.East - AvoidanceOffset;
                }

                if (IsBlockedAt(center + half with { Y = -half.Y }) && !IsBlockedAt(center + half with { X = 0 }))
                {
                    return Angle.East + AvoidanceOffset;
                }
            }
            else
            {
                if (IsBlockedAt(center + half with { X = -half.X }) && !IsBlockedAt(center - half with { X = 0 }))
                {
                    return Angle.West + AvoidanceOffset;
                }

                if (IsBlockedAt(center - half) && !IsBlockedAt(center + half with { X = 0 }))
                {
                    return Angle.West - AvoidanceOffset;
                }
            }
        }
        else
        {
            if (IsBlockedAt(center + half with { X = -half.X }) && !IsBlockedAt(center + half with { Y = 0 }))
            {
                return Angle.East + AvoidanceOffset;
            }

            if (IsBlockedAt(center + half) && !IsBlockedAt(center - half with { Y = 0 }))
            {
                return Angle.West - AvoidanceOffset;
            }
        }

        return direction;

        bool IsBlockedAt(Vector2 point) => IsBlocked(sprite, new Vector3(point, sprite.Position.Z));
    }
}
