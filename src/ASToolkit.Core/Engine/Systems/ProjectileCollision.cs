using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class ProjectileCollision
{
    private const int InstantRayStep = 16;

    private readonly MovementMap _map;
    private readonly UnitGrid _grid;

    public ProjectileCollision(MovementMap map, UnitGrid grid)
    {
        _map = map;
        _grid = grid;
    }

    [EngineRoutine(0x00444620)]
    public Vector3 Trace(ObjectDefinition kind, Vector3 from, Vector3 to, out bool hit)
    {
        hit = false;

        if (kind.MoveMask == 0)
        {
            return to;
        }

        hit = TraceInstant(kind, true, from, to, out var impact);

        return impact;
    }

    [EngineRoutine(0x0042da30)]
    public bool IsOccupied(ObjectDefinition kind, Vector3 point)
    {
        if (_map.GetSupportHeight(kind.Object, point.AsVector2()) > point.Z)
            return true;

        return kind.MoveMask != 0 && FindUnit(kind, point) is not null;
    }

    private Unit? FindUnit(ObjectDefinition kind, Vector3 point, Unit? self = null) => _grid.FindOverlap(self, kind.GetCollisionVolume(point));

    [EngineRoutine(0x00443810)]
    public bool IsBlockedAt(ObjectDefinition kind, Vector3 point, Unit? self = null)
        => kind.MoveMask != 0 && IsBlocked(kind, point, false, true, self);

    public bool HasClearTerrain(ObjectDefinition kind, Vector3 from, Vector3 to, Vector2 targetHalf = default)
    {
        if (!TraceInstant(kind, false, from, to, out var impact))
            return true;

        var offset = Vector2.Abs(impact.AsVector2() - to.AsVector2());

        return offset.X < targetHalf.X + kind.Half.X && offset.Y < targetHalf.Y + kind.Half.Y;
    }

    [EngineRoutine(0x0042db40)]
    private bool TraceInstant(ObjectDefinition kind, bool testUnits, Vector3 from, Vector3 to, out Vector3 impact)
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

                if (IsBlocked(kind, point, true, testUnits))
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

    private bool IsBlocked(ObjectDefinition kind, Vector3 point, bool instant, bool testUnits, Unit? self = null)
    {
        if (instant ? !_map.IsAboveGround(kind.Object, point) : !_map.CanStandOnGround(kind.Object, point))
        {
            return true;
        }

        return testUnits && FindUnit(kind, point, self) is not null;
    }

}
