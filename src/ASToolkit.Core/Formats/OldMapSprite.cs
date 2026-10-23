using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map before version 16.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 28)]
internal readonly struct OldMapSprite
{
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
    /// The facing field.
    /// </summary>
    [FieldOffset(20)]
    public readonly uint Dir;

    /// <summary>
    /// The army of the object.
    /// </summary>
    [FieldOffset(24)]
    public readonly uint Army;
}
