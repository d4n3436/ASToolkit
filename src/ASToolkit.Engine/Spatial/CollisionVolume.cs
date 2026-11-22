using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the box a sprite occupies for collisions, with the collision layers it moves on.
/// </summary>
public readonly struct CollisionVolume
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollisionVolume"/> structure.
    /// </summary>
    /// <param name="position">The position of the center of the box's base.</param>
    /// <param name="half">Half the width and depth of the box.</param>
    /// <param name="height">The height of the box.</param>
    /// <param name="mask">The collision layers the box moves on.</param>
    public CollisionVolume(Vector3 position, Vector2 half, float height, CollisionLayers mask)
    {
        Position = position;
        Half = half;
        Height = height;
        Mask = mask;
    }

    /// <summary>
    /// Gets the position of the center of the box's base.
    /// </summary>
    public Vector3 Position { get; }

    /// <summary>
    /// Gets half the width and depth of the box.
    /// </summary>
    public Vector2 Half { get; }

    /// <summary>
    /// Gets the height of the box.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the collision layers the box moves on.
    /// </summary>
    public CollisionLayers Mask { get; }

    /// <summary>
    /// Determines whether this volume overlaps another in all three dimensions and shares a collision layer with it.
    /// </summary>
    /// <param name="other">The other volume.</param>
    /// <returns><see langword="true"/> if the volumes collide; otherwise, <see langword="false"/>.</returns>
    public bool Overlaps(in CollisionVolume other) =>
        OverlapsFlat(other) && Position.Z <= other.Position.Z + other.Height && other.Position.Z <= Position.Z + Height;

    /// <summary>
    /// Determines whether this volume overlaps another on the ground plane and shares a collision layer with it.
    /// </summary>
    /// <param name="other">The other volume.</param>
    /// <returns><see langword="true"/> if the volumes overlap on the ground plane; otherwise, <see langword="false"/>.</returns>
    public bool OverlapsFlat(in CollisionVolume other)
    {
        var offset = Vector2.Abs(Position.AsVector2() - other.Position.AsVector2());
        var reach = Half + other.Half;

        return (Mask & other.Mask) != 0 && offset.X < reach.X && offset.Y < reach.Y;
    }

    /// <summary>
    /// Determines whether a point falls on the volume as drawn, raised by its height.
    /// </summary>
    /// <param name="point">The point, in map coordinates.</param>
    /// <returns><see langword="true"/> if the drawn volume covers the point; otherwise, <see langword="false"/>.</returns>
    public bool Covers(Vector2 point)
    {
        float top = Position.Y - Position.Z;

        return Position.X - Half.X <= point.X && point.X <= Position.X + Half.X && top - Height - Half.Y < point.Y && point.Y < top + Half.Y;
    }
}
