using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the placement of a sprite in a saved map from before <see cref="MapHeader.FloatVersion"/>, with integer coordinates.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public readonly struct LegacySpriteBody
{
    /// <summary>
    /// The object ID of the sprite.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Id;

    /// <summary>
    /// The x coordinate of the sprite.
    /// </summary>
    [FieldOffset(4)]
    public readonly int X;

    /// <summary>
    /// The y coordinate of the sprite.
    /// </summary>
    [FieldOffset(8)]
    public readonly int Y;

    /// <summary>
    /// The height of the sprite.
    /// </summary>
    [FieldOffset(12)]
    public readonly int Z;

    /// <summary>
    /// The direction of the sprite.
    /// </summary>
    [FieldOffset(16)]
    public readonly int Direction;

    /// <summary>
    /// The army of the sprite.
    /// </summary>
    [FieldOffset(20)]
    public readonly int Army;

    /// <summary>
    /// Gets the position of the sprite.
    /// </summary>
    public Vector3 Position => new(X, Y, Z);
}
