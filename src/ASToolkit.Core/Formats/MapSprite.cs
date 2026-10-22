using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
public readonly record struct MapSprite
{
    /// <summary>
    /// Creates a new instance of the <see cref="MapSprite"/> structure from a sprite of a map before version 16, optionally
    /// reinterpreting the values of X, Y and Z as integers.
    /// </summary>
    /// <param name="old">The sprite.</param>
    /// <param name="reinterpret">Whether to reinterpret the values of X, Y and Z.</param>
    internal MapSprite(OldMapSprite old, bool reinterpret)
    {
        Handle = old.Handle;
        Id = old.Id;
        X = reinterpret ? BitConverter.SingleToInt32Bits(old.X) : old.X;
        Y = reinterpret ? BitConverter.SingleToInt32Bits(old.Y) : old.Y;
        Z = reinterpret ? BitConverter.SingleToInt32Bits(old.Z) : old.Z;
        Dir = old.Dir;
        Side = old.Side;
        ScaleX = 1;
        ScaleY = 1;
        ScaleZ = 1;
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
    public readonly uint Dir;

    /// <summary>
    /// The side of the object.
    /// </summary>
    [FieldOffset(24)]
    public readonly uint Side;

    /// <summary>
    /// The negative part of the tint of the placed object. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(28)]
    public readonly uint NegativeTint;

    /// <summary>
    /// The positive part of the tint of the placed object. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(32)]
    public readonly uint PositiveTint;

    /// <summary>
    /// The scale of the sprite along x. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(36)]
    public readonly float ScaleX;

    /// <summary>
    /// The scale of the sprite along y. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(40)]
    public readonly float ScaleY;

    /// <summary>
    /// The scale of the sprite along the height. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(44)]
    public readonly float ScaleZ;
}
