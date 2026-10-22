using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the graphics record of a map, which holds its ambient light.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
public readonly struct MapGraphics
{
    /// <summary>
    /// The graphics flags.
    /// </summary>
    [FieldOffset(0)]
    public readonly uint Flags;

    /// <summary>
    /// The negative part of the ambient light.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint AmbientNegative;

    /// <summary>
    /// The positive part of the ambient light.
    /// </summary>
    [FieldOffset(8)]
    public readonly uint AmbientPositive;

    /// <summary>
    /// The angle of the direction vector.
    /// </summary>
    [FieldOffset(12)]
    public readonly uint Direction;

    /// <summary>
    /// The length of the direction vector.
    /// </summary>
    [FieldOffset(16)]
    public readonly float DirectionLength;
}
