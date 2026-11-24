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
    public bool Trace(Sprite sprite, ref Vector3 target) => TraceInstant(sprite.Definition, sprite.Position, ref target);

    [EngineRoutine(0x00443810)]
    public Sprite? FindBlocker(Sprite sprite, Vector3 point)
    {
        var definition = sprite.Definition;

        if (definition.MoveMask == 0)
            return null;

        if (!_map.Movement.CanStandOnGround(definition.Object, point))
            return _map.CursorSprite;

        var volume = definition.GetCollisionVolume(point);
        _map.Collector.FirstHashInBox(point.AsVector2() - volume.Half, point.AsVector2() + volume.Half);
        for (var other = _map.Collector.NextHashInBox(); other is not null; other = _map.Collector.NextHashInBox())
        {
            var otherDefinition = other.Definition;
            if (other == sprite || other.Animation >= SpriteAnimation.Death || MathF.Abs(other.Position.X - point.X) >= otherDefinition.Half.X + volume.Half.X ||
                MathF.Abs(other.Position.Y - point.Y) >= otherDefinition.Half.Y + volume.Half.Y || point.Z > otherDefinition.Object.Size.Z + other.Position.Z ||
                other.Position.Z > point.Z + volume.Height)
                continue;

            if ((volume.Mask & otherDefinition.MoveMask) != 0)
                return other;

            if (otherDefinition.Object.Properties.HasFlag(ObjectProperties.Crush))
            {
                _map.Action(other, ActionCode.Damage, CrushDamage);
            }
        }

        return null;
    }

    [EngineRoutine(0x0042db40)]
    private bool TraceInstant(SpriteDefinition definition, Vector3 from, ref Vector3 to)
    {
        if (definition.MoveMask == 0)
            return false;

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

                if (_map.FindPlacementBlocker(definition, point) is not null)
                {
                    to = point;

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

        return false;
    }
}
