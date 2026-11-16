using System.Numerics;
using ASToolkit.Core.Formats;
using System.Drawing;

namespace ASToolkit.Core.Engine;

internal sealed class UnitCollision
{
    private const int NorthEastBoundary = Angle.QuarterTurn / 2;
    private const int NorthWestBoundary = Angle.FullTurn - NorthEastBoundary;
    private const int SouthEastBoundary = Angle.HalfTurn - NorthEastBoundary;
    private const int SouthWestBoundary = Angle.HalfTurn + NorthEastBoundary;
    private const int CardinalHalfWidth = 16;
    private const int AvoidanceOffset = 24;
    private const int MaxPushDepth = 4;
    private const int OppositeDirectionTolerance = 16;

    private readonly MovementMap _map;
    private readonly UnitGrid _units;
    
    public UnitCollision(MovementMap map, UnitGrid units)
    {
        _map = map;
        _units = units;
    }

    public SizeF MapSize => _map.MapSize;

    public bool IsInsideMap(Vector2 point) => point is { X: >= 0, Y: >= 0 } && point.X < _map.MapSize.Width && point.Y < _map.MapSize.Height;

    public bool IsBlocked(Unit unit, Vector3 position) => FindCollision(unit, position, out _);

    [EngineRoutine(0x00443c40)]
    public bool Move(Unit unit, Vector3 position, int depth = 0, bool commit = true)
    {
        var start = unit.Position;
        bool blocked = FindCollision(unit, position, out var other);

        if (blocked)
        {
            if (other is not null && other.Definition.CanBePushed && depth < MaxPushDepth)
            {
                var pushed = other.Position + position - start;
                bool opposite = unit.Definition.CanBePushed && other.Direction.MeasureTurnTo(unit.Direction.Opposite) < OppositeDirectionTolerance;

                Move(other, pushed with { Z = other.Position.Z }, depth + 1, !opposite || unit.Direction.Value < Angle.HalfTurn);
            }

            var alongY = position with { X = start.X };

            if (!FindCollision(unit, alongY, out _))
            {
                position = alongY;
            }
            else
            {
                var alongX = position with { Y = start.Y };

                if (!FindCollision(unit, alongX, out _))
                {
                    position = alongX;
                }
                else if (other is null || !IsInsideIgnoringLayers(unit, start, other))
                {
                    return false;
                }
            }
        }

        if (!commit)
            return true;

        unit.Position = unit.Definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ)
            ? position with { Z = _map.GetSupportHeight(unit.Definition.Object, position.AsVector2()) } : position;
        _units.ChangeCoor(unit);

        return true;
    }

    private static bool IsInsideIgnoringLayers(Unit unit, Vector3 start, Unit other)
    {
        var offset = Vector2.Abs(other.Position.AsVector2() - start.AsVector2());
        var reach = other.Definition.Half + unit.Definition.Half;

        return other.Animation < SpriteAnimation.Death && offset.X < reach.X && offset.Y < reach.Y &&
            start.Z <= other.Definition.Object.Size.Z + other.Position.Z && other.Position.Z <= start.Z + unit.Definition.Object.Size.Z;
    }

    private bool FindCollision(Unit unit, Vector3 position, out Unit? collision)
    {
        collision = null;

        if (unit.Definition.MoveMask == 0)
        {
            return false;
        }

        if (!_map.CanStandOnGround(unit.Definition.Object, position))
        {
            return true;
        }

        collision = _units.FindOverlap(unit, unit.Definition.GetCollisionVolume(position));

        return collision is not null;
    }


    [EngineRoutine(0x00443ed0)]
    public Angle AvoidObstacles(Angle direction, Unit unit)
    {
        var center = unit.Position.AsVector2();
        var half = unit.Definition.Half;

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

        bool IsBlockedAt(Vector2 point) => IsBlocked(unit, new Vector3(point, unit.Position.Z));
    }
}
