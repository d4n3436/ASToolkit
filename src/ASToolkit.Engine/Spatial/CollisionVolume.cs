using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal readonly struct CollisionVolume
{
    public CollisionVolume(Vector3 position, Vector2 half, float height, CollisionLayers mask)
    {
        Position = position;
        Half = half;
        Height = height;
        Mask = mask;
    }

    public Vector3 Position { get; }

    public Vector2 Half { get; }

    public float Height { get; }

    public CollisionLayers Mask { get; }

    public bool Overlaps(in CollisionVolume other) =>
        OverlapsFlat(other) && Position.Z <= other.Position.Z + other.Height && other.Position.Z <= Position.Z + Height;

    public bool OverlapsFlat(in CollisionVolume other)
    {
        var offset = Vector2.Abs(Position.AsVector2() - other.Position.AsVector2());
        var reach = Half + other.Half;

        return (Mask & other.Mask) != 0 && offset.X < reach.X && offset.Y < reach.Y;
    }

    // Tests the point against the sprite as drawn, raised by its height
    public bool Covers(Vector2 point)
    {
        float top = Position.Y - Position.Z;

        return Position.X - Half.X <= point.X && point.X <= Position.X + Half.X && top - Height - Half.Y < point.Y && point.Y < top + Half.Y;
    }
}
