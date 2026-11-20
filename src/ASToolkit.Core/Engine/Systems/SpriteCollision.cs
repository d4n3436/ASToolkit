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

    private readonly Map _map;

    public SpriteCollision(Map map)
    {
        _map = map;
    }

    public SizeF MapSize => _map.Movement.MapSize;

    public bool IsInsideMap(Vector2 point) => point is { X: >= 0, Y: >= 0 } && point.X < _map.Movement.MapSize.Width && point.Y < _map.Movement.MapSize.Height;

    public bool IsBlocked(Sprite sprite, Vector3 position) => FindCollision(sprite, position, out _);

    public bool Move(Sprite sprite, Vector3 position)
    {
        if (!Move(sprite, ref position))
            return false;

        sprite.ChangeCoor(position);

        return true;
    }

    [EngineRoutine(0x00443c40)]
    public bool Move(Sprite sprite, ref Vector3 next, int depth = 0)
    {
        if (sprite.Definition.MoveMask == 0)
            return true;

        var start = sprite.Position;

        if (FindCollision(sprite, next, out var other))
        {
            if (_map.RunCollisionHandler(sprite, other) != 0)
            {
                next = start;

                return true;
            }

            if (other is not null && other.Definition.CanBePushed && depth < MaxPushDepth)
            {
                var pushed = new Vector3(other.Position.X + next.X - start.X, other.Position.Y + next.Y - start.Y, other.Position.Z);

                if (Move(other, ref pushed, depth + 1))
                {
                    bool opposite = sprite.Definition.CanBePushed && other.Direction.MeasureTurnTo(sprite.Direction.Opposite) < OppositeDirectionTolerance;

                    if (!opposite || sprite.Direction.Value < Angle.HalfTurn)
                    {
                        other.ChangeCoor(pushed);
                    }
                }
            }

            if (!FindCollision(sprite, next with { X = start.X }, out _))
            {
                next.X = start.X;
            }
            else if (!FindCollision(sprite, next with { Y = start.Y }, out _))
            {
                next.Y = start.Y;
            }
            else if (other is null || !IsInsideIgnoringLayers(sprite, start, other))
            {
                next = start;

                return false;
            }
        }

        if (sprite.Definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ))
        {
            next.Z = _map.Movement.GetSupportHeight(sprite.Definition.Object, next.AsVector2());
        }

        return true;
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

        if (!_map.Movement.CanStandOnGround(sprite.Definition.Object, position))
        {
            return true;
        }

        collision = _map.Collector.FindOverlap(sprite, sprite.Definition.GetCollisionVolume(position));

        return collision is not null;
    }
}
