using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the placement of a sprite in a saved map, with float coordinates.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public readonly struct SpriteBody
{
    /// <summary>
    /// The object ID of the sprite.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Id;

    /// <summary>
    /// The position of the sprite.
    /// </summary>
    [FieldOffset(4)]
    public readonly Vector3 Position;

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
}
