using System.Numerics;

namespace ASToolkit.Core.Engine;

internal sealed class ProjectileCollision
{
    private const int InstantRayStep = 16;

    private readonly MovementMap _movement;
    private readonly SpriteCollector _collector;

    public ProjectileCollision(MovementMap movement, SpriteCollector collector)
    {
        _movement = movement;
        _collector = collector;
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
        if (_movement.GetSupportHeight(definition.Object, point.AsVector2()) > point.Z)
            return true;

        return definition.MoveMask != 0 && FindSprite(definition, point) is not null;
    }

    public Sprite? FindSprite(SpriteDefinition definition, Vector3 point, Sprite? self = null) => _collector.FindOverlap(self, definition.GetCollisionVolume(point));

    [EngineRoutine(0x00443810)]
    public bool IsBlockedAt(SpriteDefinition definition, Vector3 point, Sprite? self = null)
        => definition.MoveMask != 0 && IsBlocked(definition, point, false, true, self);

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
        int x = (int)from.X;
        int y = (int)from.Y;
        int z = (int)from.Z;
        int dx = (int)to.X - x;
        int dy = (int)to.Y - y;
        int sx = Math.Sign(dx);
        int sy = Math.Sign(dy);
        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        bool xMajor = dx >= dy;
        int major = xMajor ? dx : dy;
        int minor = xMajor ? dy : dx;
        int error = -major;
        int heightStep = major == 0 ? 0 : ((int)to.Z - z) * InstantRayStep / major;
        for (int step = 0; step < major; step++)
        {
            error += 2 * minor;

            if (step > 0 && step % InstantRayStep == 0)
            {
                z += heightStep;
                var point = new Vector3(x, y, z);

                if (IsBlocked(definition, point, true, testUnits))
                {
                    impact = point;

                    return true;
                }
            }

            if (error >= 0)
            {
                if (xMajor)
                {
                    y += sy;
                }
                else
                {
                    x += sx;
                }
                error -= 2 * major;
            }

            if (xMajor)
            {
                x += sx;
            }
            else
            {
                y += sy;
            }
        }

        impact = to;

        return false;
    }

    private bool IsBlocked(SpriteDefinition definition, Vector3 point, bool instant, bool testUnits, Sprite? self = null)
    {
        if (instant ? !_movement.IsAboveGround(definition.Object, point) : !_movement.CanStandOnGround(definition.Object, point))
        {
            return true;
        }

        return testUnits && FindSprite(definition, point, self) is not null;
    }

}
