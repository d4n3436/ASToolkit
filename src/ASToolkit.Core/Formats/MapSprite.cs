using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly record struct MapSprite
{
    /// <summary>
    /// Creates a new instance of the <see cref="MapSprite"/> structure, optionally reinterpreting the values of X, Y and Z from old map versions.
    /// </summary>
    /// <param name="sprite">The sprite</param>
    /// <param name="reinterpret">Whether to reinterpret the values of X, Y and Z.</param>
    public MapSprite(MapSprite sprite, bool reinterpret = false)
    {
        Handle = sprite.Handle;
        Id = sprite.Id;
        X = reinterpret ? BitConverter.SingleToInt32Bits(sprite.X) : sprite.X;
        Y = reinterpret ? BitConverter.SingleToInt32Bits(sprite.Y) : sprite.Y;
        Z = reinterpret ? BitConverter.SingleToInt32Bits(sprite.Z) : sprite.Z;
        Dir = sprite.Dir;
        Pad1 = sprite.Pad1;
        Pad2 = sprite.Pad2;
        Army = sprite.Army;
    }
    
    /// <summary>
    /// The sprite handle that links the entry to its payload.
    /// </summary>
    [FieldOffset(0)]
    public readonly uint Handle;

    /// <summary>
    /// The object ID in objects.res.
    /// </summary>
    [FieldOffset(4)]
    public readonly KnownObject Id;

    /// <summary>
    /// The x position in map pixels.
    /// </summary>
    [FieldOffset(8)]
    public readonly float X;

    /// <summary>
    /// The y position in map pixels.
    /// </summary>
    [FieldOffset(12)]
    public readonly float Y;

    /// <summary>
    /// The height above the floor; negative values raise built-in doors.
    /// </summary>
    [FieldOffset(16)]
    public readonly float Z;

    /// <summary>
    /// The facing.
    /// </summary>
    [FieldOffset(20)]
    public readonly byte Dir;

    /// <summary>
    /// Padding.
    /// </summary>
    [FieldOffset(21)]
    public readonly short Pad1;

    /// <summary>
    /// Padding.
    /// </summary>
    [FieldOffset(23)]
    public readonly byte Pad2;

    /// <summary>
    /// The side; 1 for enemies, 2 for breakable objects, 0 for the rest.
    /// </summary>
    [FieldOffset(24)]
    public readonly uint Army;
}
