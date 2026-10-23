using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
public readonly record struct MapSprite
{
    /// <summary>
    /// Creates a new instance of the <see cref="MapSprite"/> structure from a sprite of a map before version 16, optionally
    /// reinterpreting the position as integers.
    /// </summary>
    /// <param name="old">The sprite.</param>
    /// <param name="reinterpret">Whether to reinterpret the position.</param>
    internal MapSprite(OldMapSprite old, bool reinterpret)
    {
        Handle = old.Handle;
        Id = old.Id;
        Position = reinterpret ? Vector128.ConvertToSingle(old.Position.AsVector128().AsInt32()).AsVector3() : old.Position;
        Dir = old.Dir;
        Army = old.Army;
        Scale = Vector3.One;
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
    /// The position in map pixels, with the height above the floor.
    /// </summary>
    [FieldOffset(8)]
    public readonly Vector3 Position;

    /// <summary>
    /// The facing.
    /// </summary>
    [FieldOffset(20)]
    public readonly uint Dir;

    /// <summary>
    /// The army of the object.
    /// </summary>
    [FieldOffset(24)]
    public readonly uint Army;

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
    /// The scale of the sprite along x, y and the height. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(36)]
    public readonly Vector3 Scale;
}
