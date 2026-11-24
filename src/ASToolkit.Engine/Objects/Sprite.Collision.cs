using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Sprite
{
    private const int NorthEastBoundary = Angle.QuarterTurn / 2;
    private const int NorthWestBoundary = Angle.FullTurn - NorthEastBoundary;
    private const int SouthEastBoundary = Angle.HalfTurn - NorthEastBoundary;
    private const int SouthWestBoundary = Angle.HalfTurn + NorthEastBoundary;
    private const int CardinalHalfWidth = 16;
    private const int CrushDamage = 5;
    private const int AvoidanceOffset = 24;
    private const int MaxPushDepth = 4;
    private const int OppositeDirectionTolerance = 16;

    [EngineRoutine(0x00443c40)]
    internal Sprite? CanPlaceWithCrush(ref Vector3 next, int depth = 0)
    {
        if (Definition.MoveMask == 0)
            return null;

        var start = Position;

        if (FindCollision(next, out var other))
        {
            bool isGround = other == Map.CursorSprite;

            if (Map.RunCollisionHandler(this, isGround ? null : other) != 0)
            {
                next = start;

                return null;
            }

            if (!isGround && other.Definition.CanBePushed && depth < MaxPushDepth)
            {
                var pushed = new Vector3(other.Position.X + next.X - start.X, other.Position.Y + next.Y - start.Y, other.Position.Z);

                if (other.CanPlaceWithCrush(ref pushed, depth + 1) is null)
                {
                    bool opposite = Definition.CanBePushed && other.Direction.MeasureTurnTo(Direction.Opposite) < OppositeDirectionTolerance;

                    if (!opposite || Direction.Value < Angle.HalfTurn)
                    {
                        other.ChangeCoor(pushed);
                    }
                }
            }

            if (!FindCollision(next with { X = start.X }, out _))
            {
                next.X = start.X;
            }
            else if (!FindCollision(next with { Y = start.Y }, out _))
            {
                next.Y = start.Y;
            }
            else if (isGround || !IsInsideIgnoringLayers(start, other))
            {
                next = start;

                return other;
            }
        }

        if (Definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ))
        {
            next.Z = Map.Movement.GetSupportHeight(Definition.Object, next.AsVector2());
        }

        return null;
    }

    [EngineRoutine(0x00443ed0)]
    internal Angle RotateForGlide(Angle direction)
    {
        var center = Position.AsVector2();
        var half = Definition.Half;

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

        bool IsBlockedAt(Vector2 point) => FindCollision(new Vector3(point, Position.Z), out _);
    }

    [EngineRoutine(0x00444620)]
    internal bool DirectRouteIntersection(ref Vector3 target) => Map.Collector.CheckLine(Definition, Position, ref target);

    [EngineRoutine(0x00443810)]
    internal Sprite? CanPlace(Vector3 point)
    {
        var definition = this.Definition;

        if (definition.MoveMask == 0)
            return null;

        if (!Map.Movement.CanStandOnGround(definition.Object, point))
            return Map.CursorSprite;

        var volume = definition.GetCollisionVolume(point);
        Map.Collector.FirstHashInBox(point.AsVector2() - volume.Half, point.AsVector2() + volume.Half);
        for (var other = Map.Collector.NextHashInBox(); other is not null; other = Map.Collector.NextHashInBox())
        {
            var otherDefinition = other.Definition;
            if (other == this || other.Animation >= SpriteAnimation.Death || MathF.Abs(other.Position.X - point.X) >= otherDefinition.Half.X + volume.Half.X ||
                MathF.Abs(other.Position.Y - point.Y) >= otherDefinition.Half.Y + volume.Half.Y || point.Z > otherDefinition.Object.Size.Z + other.Position.Z ||
                other.Position.Z > point.Z + volume.Height)
                continue;

            if ((volume.Mask & otherDefinition.MoveMask) != 0)
                return other;

            if (otherDefinition.Object.Properties.HasFlag(ObjectProperties.Crush))
            {
                Map.Action(other, ActionCode.Damage, CrushDamage);
            }
        }

        return null;
    }

    private bool IsInsideIgnoringLayers(Vector3 start, Sprite other)
    {
        var offset = Vector2.Abs(other.Position.AsVector2() - start.AsVector2());
        var reach = other.Definition.Half + Definition.Half;

        return other.Animation < SpriteAnimation.Death && offset.X < reach.X && offset.Y < reach.Y &&
            start.Z <= other.Definition.Object.Size.Z + other.Position.Z && other.Position.Z <= start.Z + Definition.Object.Size.Z;
    }

    private bool FindCollision(Vector3 position, [NotNullWhen(true)] out Sprite? collision)
    {
        collision = FindOverlap(position);

        return collision is not null;
    }

    [EngineRoutine(0x004433f0)]
    private Sprite? FindOverlap(Vector3 position)
    {
        var definition = Definition;

        if (definition.MoveMask == 0)
        {
            return null;
        }

        if (!Map.Movement.CanStandOnGround(definition.Object, position))
        {
            return Map.CursorSprite;
        }

        var collector = Map.Collector;
        var half = definition.Half;
        float height = definition.Object.Size.Z;
        collector.FirstHashInBox(position.AsVector2() - half, position.AsVector2() + half);
        for (var other = collector.NextHashInBox(); other is not null; other = collector.NextHashInBox())
        {
            var otherDefinition = other.Definition;
            if (other != this && other.Animation < SpriteAnimation.Death && MathF.Abs(other.Position.X - position.X) < otherDefinition.Half.X + half.X &&
                MathF.Abs(other.Position.Y - position.Y) < otherDefinition.Half.Y + half.Y && position.Z <= otherDefinition.Object.Size.Z + other.Position.Z &&
                other.Position.Z <= position.Z + height && (definition.MoveMask & otherDefinition.MoveMask) != 0)
            {
                return other;
            }
        }

        return null;
    }
}
