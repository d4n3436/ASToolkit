using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents one placed object of a map before version 16.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 28)]
public readonly struct OldMapSprite
{
    /// <summary>
    /// The sprite handle that links the entry to its payload.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Handle;

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
    /// The direction of the object.
    /// </summary>
    [FieldOffset(20)]
    public readonly Angle Direction;

    /// <summary>
    /// The army of the object.
    /// </summary>
    [FieldOffset(24)]
    public readonly Army Army;

    /// <summary>
    /// Initializes a new instance of the <see cref="OldMapSprite"/> structure.
    /// </summary>
    /// <param name="handle">The sprite handle that links the entry to its payload.</param>
    /// <param name="id">The object ID in objects.res.</param>
    /// <param name="position">The position in map pixels, with the height above the floor.</param>
    /// <param name="direction">The direction of the object.</param>
    /// <param name="army">The army of the object.</param>
    public OldMapSprite(int handle, KnownObject id, Vector3 position, Angle direction, Army army)
    {
        Handle = handle;
        Id = id;
        Position = position;
        Direction = direction;
        Army = army;
    }
}
