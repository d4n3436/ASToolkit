using System.Numerics;
using ASToolkit.Core.Formats;
using System.Drawing;

namespace ASToolkit.Core.Engine;

internal sealed class UnitCollision
{
    private const int NorthEastBoundary = Facing.QuarterTurn / 2;
    private const int NorthWestBoundary = Facing.FullTurn - NorthEastBoundary;
    private const int SouthEastBoundary = Facing.HalfTurn - NorthEastBoundary;
    private const int SouthWestBoundary = Facing.HalfTurn + NorthEastBoundary;
    private const int CardinalHalfWidth = 16;
    private const int AvoidanceOffset = 24;
    private const int MaxPushDepth = 4;
    private const int OppositeFacingTolerance = 16;

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
            if (other is not null && other.Kind.CanBePushed && depth < MaxPushDepth)
            {
                var pushed = other.Position + position - start;
                bool opposite = unit.Kind.CanBePushed && other.Facing.MeasureTurnTo(unit.Facing.Opposite) < OppositeFacingTolerance;

                Move(other, pushed with { Z = other.Position.Z }, depth + 1, !opposite || unit.Facing.Value < Facing.HalfTurn);
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
                else if (other is null || !IsInside(unit, start, other))
                {
                    return false;
                }
            }
        }

        if (!commit)
            return true;

        unit.Position = unit.Kind.Object.Properties.HasFlag(ObjectProperties.ZeroZ)
            ? position with { Z = _map.GetSupportHeight(unit.Kind.Object, position.AsVector2()) } : position;
        _units.ChangeCoor(unit);

        return true;
    }

    // Tests the overlap without layers
    private static bool IsInside(Unit unit, Vector3 start, Unit other)
    {
        var offset = Vector2.Abs(other.Position.AsVector2() - start.AsVector2());
        var reach = other.Kind.Half + unit.Kind.Half;

        return other.Animation < SpriteAnimation.Death && offset.X < reach.X && offset.Y < reach.Y &&
            start.Z <= other.Kind.Object.Size.Z + other.Position.Z && other.Position.Z <= start.Z + unit.Kind.Object.Size.Z;
    }

    private bool FindCollision(Unit unit, Vector3 position, out Unit? collision)
    {
        collision = null;

        if (unit.Kind.MoveMask == 0)
        {
            return false;
        }

        if (!_map.CanStandOnGround(unit.Kind.Object, position))
        {
            return true;
        }

        collision = _units.FindOverlap(unit, unit.Kind.GetCollisionVolume(position));

        return collision is not null;
    }


    [EngineRoutine(0x00443ed0)]
    public Facing AvoidObstacles(Facing facing, Unit unit)
    {
        var center = unit.Position.AsVector2();
        var half = unit.Kind.Half;

        if (facing.Value is <= SouthEastBoundary or >= SouthWestBoundary)
        {
            if (facing.Value is > NorthWestBoundary or < NorthEastBoundary)
            {
                if (IsBlockedAt(center - half) && !IsBlockedAt(center + half with { Y = 0 }))
                {
                    return Facing.East - AvoidanceOffset;
                }

                if (IsBlockedAt(center + half with { Y = -half.Y }) && !IsBlockedAt(center - half with { Y = 0 }))
                {
                    return Facing.West + AvoidanceOffset;
                }
            }
            else if (facing.Value <= Facing.West.Value - CardinalHalfWidth || facing.Value >= Facing.West.Value + CardinalHalfWidth)
            {
                if (facing.Value <= Facing.East.Value - CardinalHalfWidth || facing.Value >= Facing.East.Value + CardinalHalfWidth)
                    return facing;
                
                if (IsBlockedAt(center + half) && !IsBlockedAt(center - half with { X = 0 }))
                {
                    return Facing.East - AvoidanceOffset;
                }

                if (IsBlockedAt(center + half with { Y = -half.Y }) && !IsBlockedAt(center + half with { X = 0 }))
                {
                    return Facing.East + AvoidanceOffset;
                }
            }
            else
            {
                if (IsBlockedAt(center + half with { X = -half.X }) && !IsBlockedAt(center - half with { X = 0 }))
                {
                    return Facing.West + AvoidanceOffset;
                }

                if (IsBlockedAt(center - half) && !IsBlockedAt(center + half with { X = 0 }))
                {
                    return Facing.West - AvoidanceOffset;
                }
            }
        }
        else
        {
            if (IsBlockedAt(center + half with { X = -half.X }) && !IsBlockedAt(center + half with { Y = 0 }))
            {
                return Facing.East + AvoidanceOffset;
            }

            if (IsBlockedAt(center + half) && !IsBlockedAt(center - half with { Y = 0 }))
            {
                return Facing.West - AvoidanceOffset;
            }
        }

        return facing;

        bool IsBlockedAt(Vector2 point) => IsBlocked(unit, new Vector3(point, unit.Position.Z));
    }
}
