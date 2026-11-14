using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class ProjectileCollision
{
    private const int InstantRayStep = 16;

    private readonly MovementMap _map;
    private readonly UnitGrid _grid;
    private readonly List<Unit> _candidates = [];

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

        var margin = kind.Half + new Vector2(UnitGrid.MaxHalfSize);
        _grid.Collect(Vector3.Min(from, to).AsVector2() - margin, Vector3.Max(from, to).AsVector2() + margin, _candidates);
        hit = TraceInstant(kind, true, from, to, out var impact);

        return impact;
    }

    [EngineRoutine(0x0042da30)]
    public bool IsOccupied(ObjectDefinition kind, Vector3 point)
    {
        if (_map.GetSupportHeight(kind.Object, point.AsVector2()) > point.Z)
            return true;

        if (kind.MoveMask == 0)
            return false;

        var margin = kind.Half + new Vector2(UnitGrid.MaxHalfSize);
        _grid.Collect(point.AsVector2() - margin, point.AsVector2() + margin, _candidates);
        foreach (var unit in _candidates)
        {
            if (unit.Animation < SpriteAnimation.Death && kind.GetCollisionVolume(point).Overlaps(unit.CollisionVolume))
                return true;
        }

        return false;
    }

    [EngineRoutine(0x00443810)]
    public bool IsBlockedAt(ObjectDefinition kind, Vector3 point)
    {
        if (kind.MoveMask == 0)
            return false;

        var margin = kind.Half + new Vector2(UnitGrid.MaxHalfSize);
        _grid.Collect(point.AsVector2() - margin, point.AsVector2() + margin, _candidates);

        return IsBlocked(kind, point, false, true);
    }

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

    private bool IsBlocked(ObjectDefinition kind, Vector3 point, bool instant, bool testUnits)
    {
        if (instant ? _map.BlocksInstantProjectile(kind.Object, point) : !_map.CanStand(kind.Object, point))
        {
            return true;
        }

        if (!testUnits)
        {
            return false;
        }

        foreach (var unit in _candidates)
        {
            if (unit.Animation < SpriteAnimation.Death && kind.GetCollisionVolume(point).Overlaps(unit.CollisionVolume))
                return true;
        }

        return false;
    }

}
