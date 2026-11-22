using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class ProjectileCollision
{
    private const int InstantRayStep = 16;
    private const int CrushDamage = 5;

    private readonly Map _map;

    public ProjectileCollision(Map map)
    {
        _map = map;
    }

    [EngineRoutine(0x00444620)]
    public Vector3 Trace(SpriteDefinition definition, Vector3 from, Vector3 to, out bool hit)
    {
        hit = false;

        if (definition.MoveMask == 0)
        {
            return to;
        }

        hit = TraceInstant(definition, true, from, to, out var impact);

        return impact;
    }

    public bool IsOccupied(SpriteDefinition definition, Vector3 point)
    {
        if (_map.Movement.GetSupportHeight(definition.Object, point.AsVector2()) > point.Z)
            return true;

        return definition.MoveMask != 0 && FindSprite(definition, point) is not null;
    }

    public Sprite? FindSprite(SpriteDefinition definition, Vector3 point, Sprite? self = null) => _map.Collector.FindOverlap(self, definition.GetCollisionVolume(point));

    [EngineRoutine(0x00443810)]
    public bool IsBlockedAt(SpriteDefinition definition, Vector3 point, Sprite? self = null)
    {
        if (definition.MoveMask == 0)
            return false;

        if (IsBlocked(definition, point, false, false))
            return true;

        var volume = definition.GetCollisionVolume(point);
        _map.Collector.FirstHashInBox(point.AsVector2() - volume.Half, point.AsVector2() + volume.Half);
        for (var other = _map.Collector.NextHashInBox(); other is not null; other = _map.Collector.NextHashInBox())
        {
            var otherDefinition = other.Definition;
            if (other == self || other.Animation >= SpriteAnimation.Death || MathF.Abs(other.Position.X - point.X) >= otherDefinition.Half.X + volume.Half.X ||
                MathF.Abs(other.Position.Y - point.Y) >= otherDefinition.Half.Y + volume.Half.Y || point.Z > otherDefinition.Object.Size.Z + other.Position.Z ||
                other.Position.Z > point.Z + volume.Height)
                continue;

            if ((volume.Mask & otherDefinition.MoveMask) != 0)
                return true;

            if (otherDefinition.Object.Properties.HasFlag(ObjectProperties.Crush))
            {
                _map.Action(other, ActionCode.Damage, CrushDamage);
            }
        }

        return false;
    }

    public bool HasClearTerrain(SpriteDefinition definition, Vector3 from, Vector3 to, Vector2 targetHalf = default)
    {
        if (!TraceInstant(definition, false, from, to, out var impact))
            return true;

        var offset = Vector2.Abs(impact.AsVector2() - to.AsVector2());

        return offset.X < targetHalf.X + definition.Half.X && offset.Y < targetHalf.Y + definition.Half.Y;
    }

    [EngineRoutine(0x0042db40)]
    private bool TraceInstant(SpriteDefinition definition, bool testUnits, Vector3 from, Vector3 to, out Vector3 impact)
    {
        var position = new Point((int)from.X, (int)from.Y);
        int z = (int)from.Z;
        var distance = new Size((int)to.X - position.X, (int)to.Y - position.Y);
        var sign = new Size(Math.Sign(distance.Width), Math.Sign(distance.Height));
        distance = new Size(Math.Abs(distance.Width), Math.Abs(distance.Height));
        bool isXMajor = distance.Width >= distance.Height;
        int major = isXMajor ? distance.Width : distance.Height;
        int minor = isXMajor ? distance.Height : distance.Width;
        int error = -major;
        int heightStep = major == 0 ? 0 : ((int)to.Z - z) * InstantRayStep / major;
        for (int step = 0; step < major; step++)
        {
            error += 2 * minor;

            if (step > 0 && step % InstantRayStep == 0)
            {
                z += heightStep;
                var point = new Vector3(position.X, position.Y, z);

                if (IsBlocked(definition, point, true, testUnits))
                {
                    impact = point;

                    return true;
                }
            }

            if (error >= 0)
            {
                if (isXMajor)
                {
                    position.Y += sign.Height;
                }
                else
                {
                    position.X += sign.Width;
                }

                error -= 2 * major;
            }

            if (isXMajor)
            {
                position.X += sign.Width;
            }
            else
            {
                position.Y += sign.Height;
            }
        }

        impact = to;

        return false;
    }

    private bool IsBlocked(SpriteDefinition definition, Vector3 point, bool instant, bool testUnits, Sprite? self = null)
    {
        if (instant ? !_map.Movement.IsAboveGround(definition.Object, point) : !_map.Movement.CanStandOnGround(definition.Object, point))
        {
            return true;
        }

        return testUnits && FindSprite(definition, point, self) is not null;
    }
}
